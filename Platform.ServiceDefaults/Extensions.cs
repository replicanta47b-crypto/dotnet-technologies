using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Polly;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Platform.ServiceDefaults;

/// <summary>
/// Спільні налаштування для всіх компонентів платформи: Serilog (+ передача в OpenTelemetry), CorrelationId,
/// OpenTelemetry (HTTP, SQL, MongoDB; OTLP-експорт у Aspire Dashboard), health checks, service discovery, resilience.
/// </summary>
public static class Extensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Aspire передає ім'я ресурсу (payments-api, gateway, ...) в OTEL_SERVICE_NAME
        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? builder.Environment.ApplicationName;
        var isDevelopment = builder.Environment.IsDevelopment();

        // ---- Serilog: кожен запис — об'єкт із властивостями; writeToProviders → OpenTelemetry → Structured logs у Dashboard ----
        builder.Logging.ClearProviders();   // без цього записи дублювались би стандартним консольним провайдером
        builder.Services.AddSerilog((services, cfg) => cfg
            .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            // IExceptionHandler уже логує й перетворює виняток на ProblemDetails — дубльований запис middleware зайвий
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Extensions.Http", LogEventLevel.Warning)
            .MinimumLevel.Override("Yarp", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("ServiceName", serviceName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(new CompactJsonFormatter()),
            writeToProviders: true);

        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler(o =>               // retry / circuit breaker / timeout (детально — Лаб. 07)
            {
                // швидка деградація: недоступний сервіс не має тримати агрегатор десятки секунд
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
                o.Retry.MaxRetryAttempts = 2;
                o.Retry.Delay = TimeSpan.FromMilliseconds(300);
                o.Retry.BackoffType = DelayBackoffType.Exponential;   // 300 мс, 600 мс, ...
                o.Retry.UseJitter = true;                             // випадкове зміщення: клієнти не повторюють одночасно (thundering herd)
                o.Retry.DisableForUnsafeHttpMethods();                // POST/PUT/DELETE не повторюються: повтор міг би дублювати зміни
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);   // має бути >= 2 × AttemptTimeout
            });
            http.AddServiceDiscovery();                          // http://orders-api → реальна адреса від Aspire
            http.AddHttpMessageHandler<CorrelationIdHandler>();  // CorrelationId у вихідних запитах
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                // Лаб. 06: власні метрики кешу (cache.hits / cache.misses / cache.evictions ...) та вбудовані метрики gRPC
                .AddMeter("Billing.Cache", "Aggregator.Cache", "Aggregator.Grpc",
                          "Grpc.AspNetCore.Server", "Grpc.Net.Client")
                // Лаб. 07: метрики Polly (resilience.polly.strategy.events: OnRetry, OnCircuitOpened, ...), бізнес-метрики сервісів,
                // метрики health checks. Назва Meter = назва сервісу.
                .AddMeter("Polly", "Payments.Api", "Billing.Api", "Disputes.Api",
                          "Microsoft.Extensions.Diagnostics.HealthChecks"))
            .WithTracing(t =>
            {
                // 100 % у Development; у Production відсоток задається змінною OTEL_TRACES_SAMPLER / OTEL_TRACES_SAMPLER_ARG
                if (builder.Environment.IsDevelopment())
                    t.SetSampler(new AlwaysOnSampler());

                t.AddSource(builder.Environment.ApplicationName)   // власні spans (ActivitySource сервісу)
                    .AddSource("MongoDB.Driver")                   // spans драйвера MongoDB 3.7+ (команди й колекції)
                    .AddAspNetCoreInstrumentation(o =>
                        // health checks не засмічують trace
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health")
                                          && !ctx.Request.Path.StartsWithSegments("/alive"))
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation(o => o.RecordException = true);   // і Dapper, і EF Core (обидва через SqlClient)
            });

        // адресу OTLP (Aspire Dashboard) задає Aspire через OTEL_EXPORTER_OTLP_ENDPOINT
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// Теги перевірок: "live" — процес живий (без зовнішніх залежностей, &lt; 100 мс); "ready" — готовність приймати трафік
    /// (бази, кеш, downstream-сервіси); "startup" — завершення запуску. Сервіси додають власні перевірки з тегом "ready".
    /// </summary>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("process is running"), ["live"])
            .AddCheck<StartupHealthCheck>("startup", tags: ["startup"]);
        return builder;
    }

    /// <summary>
    /// /health/live — liveness (збій → оркестратор перезапускає процес); /health/ready — readiness (збій → сервіс тимчасово
    /// виключається з балансування, процес не перезапускається); /health/startup — чи завершено запуск;
    /// /health — усі перевірки; /alive — сумісність з шаблоном Aspire. Відповіді — JSON із деталями кожної перевірки.
    /// Degraded повертає HTTP 200 (сервіс працює, але без необов'язкової залежності), Unhealthy — 503.
    /// Endpoints доступні в усіх середовищах, а не лише в Development.
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
        app.MapHealthChecks("/health/live", Options(r => r.Tags.Contains("live")));
        app.MapHealthChecks("/health/ready", Options(r => r.Tags.Contains("ready")));
        app.MapHealthChecks("/health/startup", Options(r => r.Tags.Contains("startup")));
        app.MapHealthChecks("/alive", Options(r => r.Tags.Contains("live")));
        return app;

        static HealthCheckOptions Options(Func<HealthCheckRegistration, bool> predicate) =>
            new() { Predicate = predicate, ResponseWriter = HealthResponseWriter.WriteAsync };
    }
}
