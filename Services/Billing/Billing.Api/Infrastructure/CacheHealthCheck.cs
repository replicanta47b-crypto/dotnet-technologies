using Billing.Bll.Caching;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Billing.Api.Infrastructure;

/// <summary>
/// Перевірка кешу для /health: пише й читає службовий ключ через обидва рівні.
/// Якщо Redis недоступний, сервіс працює далі (L1 + БД), тому статус Degraded, а не Unhealthy.
/// </summary>
public sealed class CacheHealthCheck(ITwoLevelCache cache) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var probe = await cache.GetOrCreateAsync("billing:health-probe", "health", CachePolicy.List,
                _ => Task.FromResult("ok"), ct);
            return probe == "ok"
                ? HealthCheckResult.Healthy("L1 (memory) and L2 (Redis) respond")
                : HealthCheckResult.Degraded("unexpected probe value");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("cache unavailable, serving from database", ex);
        }
    }
}
