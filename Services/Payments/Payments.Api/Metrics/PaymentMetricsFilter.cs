using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Payments.Bll.Dto;

namespace Payments.Api.Metrics;

/// <summary>
/// MVC-фільтр: міряє тривалість кожної дії API (успіх і помилка) і збільшує бізнес-лічильники ПІСЛЯ успішної операції.
/// Контролери лишаються тонкими й про метрики не знають.
/// </summary>
public sealed class PaymentMetricsFilter(PaymentMetrics metrics) : IAsyncActionFilter
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

            if (success && executed?.Result is ObjectResult { Value: PaymentDto payment })
            {
                switch (action)
                {
                    case "Create": metrics.PaymentCreated(payment.Status, payment.Currency, payment.TotalAmount); break;
                    case "AddAllocation": metrics.AllocationAdded(); break;
                    case "ChangeStatus" or "Authorize": metrics.StatusChanged(payment.Status); break;
                }
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
