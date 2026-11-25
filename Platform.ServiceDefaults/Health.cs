using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Platform.ServiceDefaults;

/// <summary>Startup-перевірка: Healthy лише після того, як застосунок повністю запущений (ApplicationStarted).</summary>
public sealed class StartupHealthCheck(IHostApplicationLifetime lifetime) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(lifetime.ApplicationStarted.IsCancellationRequested
            ? HealthCheckResult.Healthy("application started")
            : HealthCheckResult.Unhealthy("application is still starting"));
}

/// <summary>JSON-відповідь health check: загальний статус і деталі кожної перевірки (ім'я, статус, опис, тривалість, теги).</summary>
public static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        var body = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds, 1),
                tags = e.Value.Tags,
                // тип винятку без стек-трейсу й рядків підключення
                error = e.Value.Exception?.GetType().Name
            })
        };
        return context.Response.WriteAsync(JsonSerializer.Serialize(body, Json));
    }
}
