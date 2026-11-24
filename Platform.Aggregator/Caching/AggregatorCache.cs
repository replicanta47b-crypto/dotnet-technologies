using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Caching;

/// <summary>
/// Кеш зібраних карток платежу в Redis (IDistributedCache, Meter "Aggregator.Cache").
/// Кешуються лише ПОВНІ відповіді (без warnings): часткову відповідь зберігати не можна, інакше «дірка» житиме до кінця TTL.
/// Збій Redis ніколи не ламає запит: таймаут + try/catch → поводимось як при промаху.
/// Узгодженість: TTL короткий (Aggregator:CacheSeconds), зміни в сервісах підхоплюються після його спливу.
/// </summary>
public sealed class AggregatorCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(400);

    private readonly IDistributedCache _cache;
    private readonly ILogger<AggregatorCache> _logger;
    private readonly TimeSpan _ttl;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _errors;

    public AggregatorCache(IDistributedCache cache, IMeterFactory meters, IConfiguration config, ILogger<AggregatorCache> logger)
    {
        _cache = cache;
        _logger = logger;
        _ttl = TimeSpan.FromSeconds(config.GetValue("Aggregator:CacheSeconds", 30));

        var meter = meters.Create("Aggregator.Cache");
        _hits = meter.CreateCounter<long>("cache.hits", description: "Влучання в кеш агрегованих карток");
        _misses = meter.CreateCounter<long>("cache.misses", description: "Промахи кешу агрегованих карток");
        _errors = meter.CreateCounter<long>("cache.errors", description: "Помилки/таймаути Redis");
    }

    public static string Key(long paymentId) => $"aggregator:payment-card:{paymentId}";

    public async Task<FullPaymentDto?> GetAsync(long paymentId, CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetAsync(Key(paymentId), ct).WaitAsync(Timeout, ct);
            if (bytes is null)
            {
                _misses.Add(1, new KeyValuePair<string, object?>("cache.level", "l2"));
                return null;
            }
            _hits.Add(1, new KeyValuePair<string, object?>("cache.level", "l2"));
            _logger.LogInformation("Aggregator cache HIT for payment {PaymentId}", paymentId);
            return JsonSerializer.Deserialize<FullPaymentDto>(bytes, Json);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _errors.Add(1, new KeyValuePair<string, object?>("operation", "get"));
            _logger.LogWarning("Aggregator cache unavailable on get ({Error}), continuing without it", ex.GetType().Name);
            return null;
        }
    }

    public async Task SetAsync(long paymentId, FullPaymentDto card, CancellationToken ct)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(card, Json);
            await _cache.SetAsync(Key(paymentId), bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _ttl }, ct).WaitAsync(Timeout, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _errors.Add(1, new KeyValuePair<string, object?>("operation", "set"));
            _logger.LogWarning("Aggregator cache unavailable on set ({Error})", ex.GetType().Name);
        }
    }
}
