using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Billing.Bll.Caching;

/// <summary>
/// Cache-aside з двома рівнями.
///  • L1 — IMemoryCache: SizeLimit + Size, absolute і sliding expiration, пріоритети, callback витіснення (метрика).
///  • L2 — IDistributedCache (Redis): JSON, absolute expiration. Redis може лягти — запобіжник і таймаут не дають
///    сервісу «зависнути»: у такому разі працюємо лише з L1 та БД (graceful degradation).
///  • Захист від cache stampede: для одного ключа фабрика виконується один раз (striped lock).
/// </summary>
public sealed class TwoLevelCache : ITwoLevelCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IMemoryCache _l1;
    private readonly IDistributedCache _l2;
    private readonly CacheMetrics _metrics;
    private readonly CacheOptions _options;
    private readonly ILogger<TwoLevelCache> _logger;

    // 64 «смуг» блокування замість словника семафорів на кожен ключ — без витоку пам'яті
    private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private int _l2Failures;
    private long _l2OpenUntilTicks;   // UTC ticks, до якого L2 вважається «вимкненим»

    public TwoLevelCache(IMemoryCache l1, IDistributedCache l2, CacheMetrics metrics,
        IOptions<CacheOptions> options, ILogger<TwoLevelCache> logger)
    {
        _l1 = l1;
        _l2 = l2;
        _metrics = metrics;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<T> GetOrCreateAsync<T>(string key, string name, CachePolicy policy,
        Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        // ---- L1 ----
        if (_l1.TryGetValue(key, out T? cached) && cached is not null)
        {
            _metrics.Hit("l1", name);
            _logger.LogInformation("Cache HIT L1 {CacheKey}", key);
            return cached;
        }

        var gate = _locks[(uint)key.GetHashCode() % (uint)_locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            // повторна перевірка: поки чекали, інший запит міг уже заповнити кеш
            if (_l1.TryGetValue(key, out cached) && cached is not null)
            {
                _metrics.Hit("l1", name);
                return cached;
            }

            // ---- L2 ----
            var fromL2 = await TryGetL2<T>(key, ct);
            if (fromL2 is not null)
            {
                _metrics.Hit("l2", name);
                _logger.LogInformation("Cache HIT L2 {CacheKey} (promoted to L1)", key);
                SetL1(key, fromL2, policy);   // «прогріваємо» L1
                return fromL2;
            }

            // ---- джерело (БД) ----
            _metrics.Miss(name);
            _logger.LogInformation("Cache MISS {CacheKey} → loading from source", key);
            var value = await factory(ct);
            if (value is null) return value!;

            SetL1(key, value, policy);
            await TrySetL2(key, value, policy, ct);
            return value;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task RemoveAsync(string key, string name, string reason, CancellationToken ct = default)
    {
        _l1.Remove(key);
        if (L2Available())
        {
            try
            {
                await _l2.RemoveAsync(key, ct).WaitAsync(TimeSpan.FromMilliseconds(_options.L2TimeoutMs), ct);
                L2Success();
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                L2Failure("remove", ex);
            }
        }
        _metrics.Invalidated(name);
        _logger.LogInformation("Cache INVALIDATE {CacheKey}, reason: {Reason}", key, reason);
    }

    // ------------------------------------------------------------------ L1

    private void SetL1<T>(string key, T value, CachePolicy policy)
    {
        var entry = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,
            SlidingExpiration = policy.L1Sliding,
            Priority = policy.Priority,
            Size = policy.Size                                  // обов'язково при SizeLimit
        };
        entry.RegisterPostEvictionCallback((k, _, reason, _) =>
        {
            // Replaced/Removed — наша ж інвалідація; рахуємо як витіснення лише «природні» причини
            if (reason is EvictionReason.Capacity or EvictionReason.Expired or EvictionReason.TokenExpired)
            {
                _metrics.Eviction(reason.ToString());
                _logger.LogDebug("L1 evicted {CacheKey} ({Reason})", k, reason);
            }
        });
        _l1.Set(key, value, entry);
    }

    // ------------------------------------------------------------------ L2 (Redis) з таймаутом і запобіжником

    private bool L2Available() => DateTime.UtcNow.Ticks >= Interlocked.Read(ref _l2OpenUntilTicks);

    private void L2Success() => Interlocked.Exchange(ref _l2Failures, 0);

    private void L2Failure(string operation, Exception ex)
    {
        _metrics.L2Error(operation);
        var failures = Interlocked.Increment(ref _l2Failures);
        // збій кешу ніколи не провалює запит: логуємо й читаємо з бази даних (graceful degradation)
        _logger.LogWarning("Redis unavailable on {Operation} ({Failures}/{Threshold}), falling back to database: {Error}",
            operation, failures, _options.L2BreakerThreshold, ex.GetType().Name);

        if (failures >= _options.L2BreakerThreshold)
        {
            Interlocked.Exchange(ref _l2OpenUntilTicks, DateTime.UtcNow.AddSeconds(_options.L2BreakerSeconds).Ticks);
            Interlocked.Exchange(ref _l2Failures, 0);
            _logger.LogError("L2 (Redis) circuit OPEN for {Seconds}s: working with L1 + database only", _options.L2BreakerSeconds);
        }
    }

    private async Task<T?> TryGetL2<T>(string key, CancellationToken ct)
    {
        if (!L2Available()) return default;
        try
        {
            var bytes = await _l2.GetAsync(key, ct).WaitAsync(TimeSpan.FromMilliseconds(_options.L2TimeoutMs), ct);
            L2Success();
            return bytes is null ? default : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            L2Failure("get", ex);
            return default;
        }
    }

    private async Task TrySetL2<T>(string key, T value, CachePolicy policy, CancellationToken ct)
    {
        if (!L2Available()) return;
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
            var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = policy.L2Absolute };
            await _l2.SetAsync(key, bytes, options, ct).WaitAsync(TimeSpan.FromMilliseconds(_options.L2TimeoutMs), ct);
            L2Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            L2Failure("set", ex);
        }
    }
}
