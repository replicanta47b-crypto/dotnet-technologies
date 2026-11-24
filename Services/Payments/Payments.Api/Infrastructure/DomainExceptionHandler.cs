using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Payments.Domain.Exceptions;

namespace Payments.Api.Infrastructure;

/// <summary>
/// Централізований перехоплювач помилок (RFC 9457 ProblemDetails).
/// NotFound → 404, BusinessConflict → 409, Validation → 400, усе інше → 500 без технічних деталей.
/// Контролери не містять try/catch.
/// </summary>
public sealed class DomainExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<DomainExceptionHandler> _logger;

    public DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Business rule conflict"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            _logger.LogError(exception, "Unhandled exception for {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            _logger.LogInformation("Handled {Type}: {Message}", exception.GetType().Name, exception.Message);

        context.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // для 500 повідомлення виключення не розкриваємо (там можуть бути SQL-деталі)
                Detail = status >= 500 ? "An unexpected error occurred." : exception.Message,
                Instance = $"{context.Request.Method} {context.Request.Path}"
            }
        });
    }
}
