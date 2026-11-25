using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Platform.EventBus;

public static class EventBusExtensions
{
    /// <summary>Ім'я ресурсу RabbitMQ в AppHost (builder.AddRabbitMQ("messaging")); Aspire передає ConnectionStrings:messaging.</summary>
    public const string ConnectionName = "messaging";

    /// <summary>
    /// Підключення до RabbitMQ: реєструє BrokerConnection (спільне з'єднання, singleton), health check «messaging»
    /// ; рядок підключення передає AppHost через WithReference. Readiness-перевірка з тегом "ready".
    /// </summary>
    public static IHostApplicationBuilder AddEventBus(this IHostApplicationBuilder builder)
    {
        builder.Services.AddSingleton<BrokerConnection>();
        builder.Services.AddHealthChecks()
            .AddCheck<RabbitMqHealthCheck>("messaging", HealthStatus.Degraded, ["messaging", "rabbitmq", "ready"],
                TimeSpan.FromSeconds(5));
        return builder;
    }

    /// <summary>Видавець подій (сервіс-власник даних).</summary>
    public static IServiceCollection AddEventPublisher(this IServiceCollection services)
    {
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        return services;
    }

    /// <summary>Оголошує exchange і черги споживачів при старті видавця (щоб події не губилися, поки споживач вимкнений).</summary>
    public static IServiceCollection AddTopologyInitializer(this IServiceCollection services, params ConsumerQueue[] queues)
    {
        services.AddHostedService(sp => new TopologyInitializer(
            sp.GetRequiredService<BrokerConnection>(), queues, sp.GetRequiredService<ILogger<TopologyInitializer>>()));
        return services;
    }

    /// <summary>Споживач черги як hosted service. Обробники реєструються в options.Handle&lt;TEvent, THandler&gt;() і в DI (Scoped).</summary>
    public static IServiceCollection AddEventConsumer(this IServiceCollection services, ConsumerQueue queue,
        Action<EventConsumerOptions> configure)
    {
        var options = new EventConsumerOptions { Queue = queue };
        configure(options);

        foreach (var registration in options.Handlers.Values)
            services.AddScoped(registration.HandlerType);

        services.AddHostedService(sp => new EventConsumerService(
            sp.GetRequiredService<BrokerConnection>(),
            sp.GetRequiredService<IServiceScopeFactory>(),
            options,
            sp.GetRequiredService<ILogger<EventConsumerService>>()));
        return services;
    }
}
