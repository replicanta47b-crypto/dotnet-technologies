using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
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
                .AddRuntimeInstrumentation())
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

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>/health (усі перевірки) і /alive (лише «self»). У Production відкривати обережно.</summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}
