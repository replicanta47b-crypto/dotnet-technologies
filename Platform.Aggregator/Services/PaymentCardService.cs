using System.Diagnostics;
using System.Diagnostics.Metrics;
using Grpc.Core;
using Platform.Aggregator.Caching;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>Результат: картка + ким вона отримана (для заголовків X-Transport / X-Cache і для звіту).</summary>
public sealed record CardResult(FullPaymentDto? Card, string Transport, string Cache);

/// <summary>
/// Aggregator Pattern: картка платежу з трьох сервісів. Без платежу агрегувати нічого, тому він запитується першим;
/// рахунки Білінгу й спори Disputes — незалежні й опитуються паралельно (Task.WhenAll). Недоступність одного
/// з них не «валить» запит: відповідь містить доступні дані й перелік попереджень.
/// Лаб. 06: транспорт обирається (HTTP або gRPC), готові картки кешуються в Redis.
/// </summary>
public class PaymentCardService
{
    private readonly HttpPaymentCardSource _http;
    private readonly GrpcPaymentCardSource _grpc;
    private readonly AggregatorCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<PaymentCardService> _logger;
    private readonly Histogram<double> _upstreamDuration;

    public PaymentCardService(HttpPaymentCardSource http, GrpcPaymentCardSource grpc, AggregatorCache cache,
        IMeterFactory meters, IConfiguration config, ILogger<PaymentCardService> logger)
    {
        _http = http;
        _grpc = grpc;
        _cache = cache;
        _config = config;
        _logger = logger;
        // час відповіді кожного сервісу з розбивкою за транспортом → порівняння HTTP і gRPC в Dashboard
        _upstreamDuration = meters.Create("Aggregator.Grpc")
            .CreateHistogram<double>("aggregator.upstream.duration", unit: "ms", description: "Тривалість виклику сервісу з Aggregator");
    }

    public async Task<CardResult> GetFullPaymentAsync(long paymentId, string? transport, bool bypassCache, CancellationToken ct)
    {
        var source = Pick(transport);

        if (!bypassCache)
        {
            var cached = await _cache.GetAsync(paymentId, ct);
            if (cached is not null)
                return new CardResult(cached, source.Name, "HIT");
        }

        var card = await BuildAsync(paymentId, source, ct);
        var cacheState = bypassCache ? "BYPASS" : "MISS";

        if (card is { Warnings.Count: 0 } && !bypassCache)
            await _cache.SetAsync(paymentId, card, ct);   // часткову відповідь не кешуємо

        return new CardResult(card, source.Name, cacheState);
    }

    private IPaymentCardSource Pick(string? transport)
    {
        var name = string.IsNullOrWhiteSpace(transport) ? _config["Aggregator:DefaultTransport"] ?? "grpc" : transport;
        return name.Equals("http", StringComparison.OrdinalIgnoreCase) ? _http : _grpc;
    }

    private async Task<FullPaymentDto?> BuildAsync(long paymentId, IPaymentCardSource source, CancellationToken ct)
    {
        var warnings = new List<string>();

        _logger.LogInformation("Calling payments-api for payment {PaymentId} via {Transport}", paymentId, source.Name);
        var payment = await Timed("payments-api", source.Name, () => source.GetPaymentAsync(paymentId, ct));   // недоступність → виняток → 503 у контролері
        if (payment is null) return null;

        var invoiceIds = payment.Allocations.Select(a => a.InvoiceId).Distinct().ToList();

        // незалежні виклики — одночасно; CancellationToken передається в усі
        var invoicesTask = SafeAsync("billing-api", source.Name, warnings, ct, async () =>
        {
            var results = await Task.WhenAll(invoiceIds.Select(id => FetchInvoiceAsync(source, id, warnings, ct)));
            return results.Where(i => i is not null).Select(i => i!).ToList();
        });
        var disputesTask = SafeAsync("disputes-api", source.Name, warnings, ct, () => source.GetDisputesAsync(paymentId, ct));

        await Task.WhenAll(invoicesTask, disputesTask);

        // узгодженість: спори стосуються саме цього платежу
        var paymentDisputes = (await disputesTask ?? []).Where(d => d.PaymentId == paymentId).ToList();

        return new FullPaymentDto(payment, await invoicesTask ?? [], paymentDisputes, warnings);
    }

    private async Task<BillingInvoiceDto?> FetchInvoiceAsync(IPaymentCardSource source, long invoiceId, List<string> warnings, CancellationToken ct)
    {
        var invoice = await source.GetInvoiceAsync(invoiceId, ct);
        if (invoice is null)
        {
            _logger.LogWarning("Invoice {InvoiceId} was not found in billing-api", invoiceId);
            lock (warnings) warnings.Add($"Invoice {invoiceId} was not found in billing-api");
        }
        return invoice;
    }

    /// <summary>Виконує виклик сервісу з логуванням тривалості; збій перетворює на попередження.</summary>
    private async Task<T?> SafeAsync<T>(string service, string transport, List<string> warnings, CancellationToken ct, Func<Task<T>> call)
    {
        _logger.LogInformation("Calling {Service} via {Transport}", service, transport);
        var sw = Stopwatch.StartNew();
        try
        {
            return await Timed(service, transport, call);
        }
        catch (RpcException ex) when (!ct.IsCancellationRequested
                                      && ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded)
        {
            // gRPC: сервіс недоступний або не вклався в дедлайн → попередження, а не 500
            _logger.LogWarning("{Service} gRPC {StatusCode} after {Elapsed} ms", service, ex.StatusCode, sw.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable ({ex.StatusCode})");
            return default;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // HttpRequestException, RpcException(Unavailable), timeout, розімкнений circuit breaker (Polly) тощо
            _logger.LogWarning(ex, "{Service} is unavailable after {Elapsed} ms", service, sw.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable");
            return default;
        }
    }

    private async Task<T> Timed<T>(string service, string transport, Func<Task<T>> call)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await call();
        }
        finally
        {
            _upstreamDuration.Record(sw.Elapsed.TotalMilliseconds,
                new KeyValuePair<string, object?>("service", service),
                new KeyValuePair<string, object?>("transport", transport));
            _logger.LogInformation("{Service} responded via {Transport} in {Elapsed} ms", service, transport, sw.ElapsedMilliseconds);
        }
    }
}
