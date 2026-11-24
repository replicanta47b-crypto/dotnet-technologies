using Disputes.Application.Common.Interfaces;
using Disputes.Domain.Interfaces;
using Disputes.Infrastructure.Persistence;
using Disputes.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Disputes.Infrastructure;

public static class DependencyInjection
{
    /// <summary>MongoDB: BSON-мапінг (один раз), контекст, репозиторії, запити читання, індекси та сідінг.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        MongoMappings.Register();   // до першого звернення до БД

        services.Configure<MongoDbOptions>(config.GetSection("MongoDb"));
        // Aspire передає рядок підключення через WithReference → ConnectionStrings__DisputesDb (має пріоритет)
        services.PostConfigure<MongoDbOptions>(o =>
        {
            var fromAppHost = config.GetConnectionString("DisputesDb");
            if (!string.IsNullOrWhiteSpace(fromAppHost))
                o.ConnectionString = fromAppHost;
        });

        services.AddSingleton<MongoDbContext>();
        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<IDisputeThreadRepository, DisputeThreadRepository>();
        services.AddScoped<IDisputeQueries, DisputeQueries>();
        services.AddSingleton<MongoHealthCheck>();

        services.AddSingleton<IDataSeeder, DisputeSeeder>();

        // порядок має значення: спочатку індекси, потім сідінг
        services.AddHostedService<IndexCreationService>();
        services.AddHostedService<DataSeedingService>();
        return services;
    }
}
