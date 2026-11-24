using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Disputes.Infrastructure.Seeding;

/// <summary>Запускає всі IDataSeeder при старті (після створення індексів). У Production вимкнено (Database:Seed).</summary>
public class DataSeedingService(
    IEnumerable<IDataSeeder> seeders,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DataSeedingService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (environment.IsProduction() || !configuration.GetValue("Database:Seed", true))
        {
            logger.LogInformation("Data seeding is disabled");
            return;
        }

        try
        {
            foreach (var seeder in seeders)
                await seeder.SeedAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Data seeding failed");
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
