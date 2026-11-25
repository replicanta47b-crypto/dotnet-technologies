using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Перевіряє downstream gRPC-сервіс стандартним gRPC Health Checking Protocol (grpc.health.v1.Health/Check).
/// Дедлайн 2 с: недоступність виявляється швидко. Перевірка ідемпотентна й не має побічних ефектів у сервісі.
/// Статус збою задає реєстрація: критичний сервіс → Unhealthy, некритичний → Degraded (Aggregator віддасть часткову відповідь).
/// </summary>
public sealed class GrpcDependencyHealthCheck(
    GrpcClientFactory clients, ILogger<GrpcDependencyHealthCheck> logger, string clientName, string service) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var client = clients.CreateClient<Health.HealthClient>(clientName);
            var reply = await client.CheckAsync(new HealthCheckRequest(), deadline: DateTime.UtcNow.AddSeconds(2), cancellationToken: ct);
            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{service} is serving")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{service} status: {reply.Status}");
        }
        catch (RpcException ex)
        {
            logger.LogWarning("{Service} gRPC health check failed with {StatusCode}", service, ex.StatusCode);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{service} is unavailable ({ex.StatusCode})", ex);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("{Service} health check failed: {Error}", service, ex.GetType().Name);
            return new HealthCheckResult(context.Registration.FailureStatus, $"{service} is unavailable", ex);
        }
    }
}
