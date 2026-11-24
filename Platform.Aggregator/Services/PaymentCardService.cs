using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>
/// Aggregator Pattern: картка платежу з трьох сервісів. Без платежу агрегувати нічого, тому він запитується першим;
/// рахунки Білінгу й спори Disputes — незалежні й опитуються паралельно (Task.WhenAll). Недоступність одного
/// з них не «валить» запит: відповідь містить доступні дані й перелік попереджень.
/// </summary>
public class PaymentCardService(
    PaymentsClient payments, BillingClient billing, DisputesClient disputes, ILogger<PaymentCardService> logger)
{
    public async Task<FullPaymentDto?> GetFullPaymentAsync(long paymentId, CancellationToken ct)
    {
        var warnings = new List<string>();

        logger.LogInformation("Calling payments-api for payment {PaymentId}", paymentId);
        var sw = Stopwatch.StartNew();
        var payment = await payments.GetPaymentAsync(paymentId, ct);   // недоступність → виняток → 503 у контролері
        logger.LogInformation("payments-api responded in {Elapsed} ms", sw.ElapsedMilliseconds);
        if (payment is null) return null;

        var invoiceIds = payment.Allocations.Select(a => a.InvoiceId).Distinct().ToList();

        // незалежні виклики — одночасно; CancellationToken передається в усі
        var invoicesTask = SafeAsync("billing-api", warnings, ct, async () =>
        {
            var results = await Task.WhenAll(invoiceIds.Select(id => FetchInvoiceAsync(id, warnings, ct)));
            return results.Where(i => i is not null).Select(i => i!).ToList();
        });
        var disputesTask = SafeAsync("disputes-api", warnings, ct, () => disputes.GetByPaymentAsync(paymentId, ct));

        await Task.WhenAll(invoicesTask, disputesTask);

        // узгодженість: спори стосуються саме цього платежу
        var paymentDisputes = (await disputesTask ?? []).Where(d => d.PaymentId == paymentId).ToList();

        return new FullPaymentDto(payment, await invoicesTask ?? [], paymentDisputes, warnings);
    }

    private async Task<BillingInvoiceDto?> FetchInvoiceAsync(long invoiceId, List<string> warnings, CancellationToken ct)
    {
        var invoice = await billing.GetInvoiceAsync(invoiceId, ct);
        if (invoice is null)
        {
            logger.LogWarning("Invoice {InvoiceId} was not found in billing-api", invoiceId);
            lock (warnings) warnings.Add($"Invoice {invoiceId} was not found in billing-api");
        }
        return invoice;
    }

    /// <summary>Виконує виклик сервісу з логуванням тривалості; збій перетворює на попередження.</summary>
    private async Task<T?> SafeAsync<T>(string service, List<string> warnings, CancellationToken ct, Func<Task<T>> call)
    {
        logger.LogInformation("Calling {Service}", service);
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await call();
            logger.LogInformation("{Service} responded in {Elapsed} ms", service, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // HttpRequestException, timeout, розімкнений circuit breaker (Polly) тощо
            logger.LogWarning(ex, "{Service} is unavailable after {Elapsed} ms", service, sw.ElapsedMilliseconds);
            lock (warnings) warnings.Add($"{service} is unavailable");
            return default;
        }
    }
}
