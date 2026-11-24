using System.Diagnostics.Metrics;

namespace Billing.Bll.Caching;

/// <summary>
/// Метрики кешу (OpenTelemetry/Meter "Billing.Cache"): видно в Aspire Dashboard → Metrics.
/// Тег cache.level = l1 (пам'ять) | l2 (Redis); cache.name — вид даних.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "Billing.Cache";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _evictions;
    private readonly Counter<long> _errors;
    private readonly Counter<long> _invalidations;

    public CacheMetrics(IMeterFactory factory)
    {
        var meter = factory.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", description: "Влучання в кеш");
        _misses = meter.CreateCounter<long>("cache.misses", description: "Промахи кешу (довелося йти в БД)");
        _evictions = meter.CreateCounter<long>("cache.evictions", description: "Витіснення записів з L1");
        _errors = meter.CreateCounter<long>("cache.errors", description: "Помилки/таймаути L2 (Redis)");
        _invalidations = meter.CreateCounter<long>("cache.invalidations", description: "Інвалідації після змін");
    }

    public void Hit(string level, string name) =>
        _hits.Add(1, new("cache.level", level), new("cache.name", name));

    public void Miss(string name) =>
        _misses.Add(1, new("cache.level", "all"), new("cache.name", name));

    public void Eviction(string reason) =>
        _evictions.Add(1, new("cache.level", "l1"), new("reason", reason));

    public void L2Error(string operation) =>
        _errors.Add(1, new("cache.level", "l2"), new("operation", operation));

    public void Invalidated(string name) =>
        _invalidations.Add(1, new("cache.name", name));
}
