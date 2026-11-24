using Serilog.Context;

namespace Billing.Api.Infrastructure;

/// <summary>
/// Додає до кожного запису логу властивості CorrelationId, RequestPath та UserId (якщо користувач автентифікований).
/// CorrelationId береться із заголовка X-Correlation-ID або генерується, і повертається у відповіді.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var header) && !string.IsNullOrWhiteSpace(header)
            ? header.ToString()
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
        using (LogContext.PushProperty("UserId", context.User.Identity?.Name ?? "anonymous"))
        {
            await _next(context);
        }
    }
}
