using Billing.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Infrastructure;

/// <summary>
/// Глобальний обробник помилок (ProblemDetails, RFC 9457). Мапінг виключень:
/// EntityNotFoundException → 404, ValidationException → 400, ConflictException/DomainException → 409, решта → 500.
/// Кожен виняток логується рівно один раз — тут. Технічні деталі повертаються лише в Development.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetails;
    private readonly IHostEnvironment _env;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment env,
                                  ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetails = problemDetails;
        _env = env;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title) = ex switch
        {
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            DomainException => (StatusCodes.Status409Conflict, "Business rule violation"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            _logger.LogError(ex, "Request {Method} {Path} failed", http.Request.Method, http.Request.Path);
        else
            _logger.LogWarning("Request {Method} {Path} rejected with {Status}: {Reason}",
                http.Request.Method, http.Request.Path, status, ex.Message);

        http.Response.StatusCode = status;
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = ex,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                // у production для 500 — лише безпечний опис; технічні деталі тільки в Development
                Detail = status < 500 || _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.",
                Instance = $"{http.Request.Method} {http.Request.Path}"
            }
        });
    }
}
