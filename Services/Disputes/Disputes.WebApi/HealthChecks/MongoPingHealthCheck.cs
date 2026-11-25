using Disputes.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Disputes.WebApi.HealthChecks;

/// <summary>Легка перевірка MongoDB: команда ping (без читання колекцій). Тип збою задає реєстрація (failureStatus).</summary>
public sealed class MongoPingHealthCheck(MongoDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken ct = default)
    {
        try
        {
            await context.Database.RunCommandAsync(
                new BsonDocumentCommand<BsonDocument>(new BsonDocument("ping", 1)), cancellationToken: ct)
                .WaitAsync(TimeSpan.FromSeconds(4), ct);   // сервер MongoDB недоступний — не чекаємо весь serverSelectionTimeout
            return HealthCheckResult.Healthy("MongoDB responds (ping)");
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return new HealthCheckResult(healthContext.Registration.FailureStatus, "MongoDB is unreachable", ex);
        }
    }
}
