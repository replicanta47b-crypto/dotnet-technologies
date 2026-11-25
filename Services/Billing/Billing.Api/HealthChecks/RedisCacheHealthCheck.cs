using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Billing.Api.HealthChecks;

/// <summary>
/// Redis — необов'язкова залежність (кеш), тому збій дає Degraded, а не Unhealthy: Aggregator працює й без кешу,
/// просто кожного разу опитує сервіси. Перевірка легка: читання неіснуючого ключа (один round-trip до Redis).
/// </summary>
public sealed class RedisCacheHealthCheck(IDistributedCache cache, ILogger<RedisCacheHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await cache.GetAsync("health:probe", ct).WaitAsync(TimeSpan.FromSeconds(3), ct);
            return HealthCheckResult.Healthy("Redis responds");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("Redis health check failed ({Error}): cache is disabled, serving without it", ex.GetType().Name);
            return new HealthCheckResult(context.Registration.FailureStatus,
                "Redis unavailable: working without cache", ex);
        }
    }
}
