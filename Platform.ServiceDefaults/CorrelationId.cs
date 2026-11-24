using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Platform.ServiceDefaults;

/// <summary>
/// Наскрізний CorrelationId. Береться із заголовка X-Correlation-Id або генерується (для запитів в обхід Gateway),
/// кладеться в LogContext (потрапляє в усі логи запиту), у відповідь клієнту й у тег поточного span.
/// На початку й у кінці запиту логується межа запиту (boundary logging).
/// CorrelationId ≠ TraceId: перший створює й передає застосунок, другий генерує OpenTelemetry.
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var generated = false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = value;   // далі його побачать YARP і HttpClient
            generated = true;
        }

        var correlationId = value.ToString();
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);   // trace можна шукати за CorrelationId

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
        {
            if (generated)
                logger.LogInformation("CorrelationId {CorrelationId} generated: request had no {Header} header",
                    correlationId, HeaderName);

            logger.LogInformation("Request started {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            await next(context);
            logger.LogInformation("Request finished with {StatusCode}", context.Response.StatusCode);
        }
    }
}

/// <summary>Додає той самий CorrelationId у вихідні HTTP-запити до інших сервісів.</summary>
public class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);

        return base.SendAsync(request, cancellationToken);
    }
}
