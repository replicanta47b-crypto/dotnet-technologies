using System.Diagnostics;
using Billing.Bll.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Billing.Api.Metrics;

/// <summary>MVC-фільтр: тривалість кожної дії (успіх і помилка) + бізнес-лічильники після успішної зміни рахунку.</summary>
public sealed class BillingMetricsFilter(BillingMetrics metrics) : IAsyncActionFilter
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

            if (success && controller == "Invoices" && executed?.Result is ObjectResult { Value: InvoiceDetailsDto invoice })
            {
                if (action == "Create")
                    metrics.InvoiceCreated(invoice.Status, invoice.Currency, invoice.TotalAmount);
                else if (context.HttpContext.Request.Method != HttpMethods.Get)
                    metrics.StatusChanged(invoice.Status);   // pay, void, reschedule, category
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
