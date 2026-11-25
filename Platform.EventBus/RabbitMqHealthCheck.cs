using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Readiness брокера: з'єднання відкрите і канал створюється. Збій — Degraded, а не Unhealthy: сервіс і без брокера
/// обслуговує читання й запити до власної БД, а події наздоженуть після відновлення (eventual consistency).
/// </summary>
public sealed class RabbitMqHealthCheck(BrokerConnection connection) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var conn = await connection.GetAsync(ct);
            await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);
            return HealthCheckResult.Healthy("RabbitMQ connection is open");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("RabbitMQ is unavailable", ex);
        }
    }
}
