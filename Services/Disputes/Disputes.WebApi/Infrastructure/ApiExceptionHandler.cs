using Disputes.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;

namespace Disputes.WebApi.Infrastructure;

/// <summary>
/// Глобальний обробник помилок (IExceptionHandler + ProblemDetails, RFC 9457), зокрема MongoDB-специфічних:
/// NotFound → 404; Validation / Domain → 400; Conflict (у т. ч. конфлікт версій) → 409;
/// MongoWriteException(DuplicateKey) → 409, інші помилки запису → 400; MongoConnectionException / таймаут → 503.
/// </summary>
public sealed class ApiExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ValidationException => (StatusCodes.Status400BadRequest, "One or more validation errors occurred."),
            DomainException => (StatusCodes.Status400BadRequest, "Business rule violation"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey }
                => (StatusCodes.Status409Conflict, "Duplicate key"),
            MongoWriteException => (StatusCodes.Status400BadRequest, "Write to database failed"),
            MongoConnectionException => (StatusCodes.Status503ServiceUnavailable, "Database unavailable"),
            TimeoutException => (StatusCodes.Status503ServiceUnavailable, "Database unavailable"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            logger.LogError(exception, "Request {Method} {Path} failed with {Status}",
                http.Request.Method, http.Request.Path, status);
        else
            logger.LogWarning("Request {Method} {Path} rejected with {Status}: {Reason}",
                http.Request.Method, http.Request.Path, status, exception.Message);

        ProblemDetails body;
        if (exception is ValidationException validation)
        {
            var errors = validation.Errors
                .GroupBy(e => string.IsNullOrEmpty(e.PropertyName) ? "request" : e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
            body = new ValidationProblemDetails(errors) { Status = status, Title = title };
        }
        else
        {
            body = new ProblemDetails
            {
                Status = status,
                Title = title,
                // технічні деталі 5xx — лише в Development
                Detail = status < 500 || environment.IsDevelopment()
                    ? exception.Message
                    : "An unexpected error occurred."
            };
        }
        body.Instance = $"{http.Request.Method} {http.Request.Path}";

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            Exception = exception,
            ProblemDetails = body
        });
    }
}
