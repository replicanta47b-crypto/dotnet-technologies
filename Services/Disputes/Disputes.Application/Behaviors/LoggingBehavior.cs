using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Disputes.Application.Behaviors;

/// <summary>
/// Зовнішній behavior pipeline: структуровано логує кожен запит і відповідь.
/// Якщо ValidationBehavior відхилив запит — у лозі є «Handling …», а «Handled …» немає: обробник не виконувався.
/// </summary>
public class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName} {@Request}", name, request);

        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        logger.LogInformation("Handled {RequestName} in {ElapsedMs} ms → {ResponseType}",
            name, sw.ElapsedMilliseconds, typeof(TResponse).Name);
        logger.LogDebug("Response of {RequestName}: {@Response}", name, response);
        return response;
    }
}
