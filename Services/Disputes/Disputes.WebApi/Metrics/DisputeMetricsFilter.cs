using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Disputes.WebApi.Metrics;

/// <summary>MVC-фільтр: тривалість кожної дії (успіх і помилка) + лічильники після успішного відкриття спору й коментаря.</summary>
public sealed class DisputeMetricsFilter(DisputeMetrics metrics) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller);
        context.ActionDescriptor.RouteValues.TryGetValue("action", out var action);
        var operation = $"{controller}.{action}";

        var sw = Stopwatch.StartNew();
        metrics.OperationStarted();
        ActionExecutedContext? executed = null;
        try
        {
            executed = await next();
        }
        finally
        {
            metrics.OperationFinished();
            var success = executed is { Exception: null } && !IsError(executed.Result);
            metrics.RecordDuration(operation, success, sw.Elapsed.TotalMilliseconds);

            if (success)
            {
                if (action == "Create") metrics.DisputeOpened();
                else if (action == "AddComment") metrics.CommentAdded();
            }
        }
    }

    private static bool IsError(IActionResult? result) => result switch
    {
        ObjectResult { StatusCode: >= 400 } => true,
        ObjectResult { Value: ProblemDetails { Status: >= 400 } } => true,
        StatusCodeResult { StatusCode: >= 400 } => true,
        _ => false
    };
}
