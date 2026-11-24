using FluentValidation;
using Disputes.Domain.Exceptions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Disputes.Application.Behaviors;

/// <summary>
/// Централізоване логування винятків на рівні MediatR. Виняток не поглинається, а прокидається далі:
/// у HTTP-статус його перетворює глобальний обробник у WebApi (ProblemDetails).
/// </summary>
public class ExceptionHandlingBehavior<TRequest, TResponse>(
    ILogger<ExceptionHandlingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (ValidationException ex)
        {
            logger.LogWarning("{RequestName} rejected by validation: {Errors}",
                typeof(TRequest).Name, ex.Errors.Select(e => $"{e.PropertyName}: {e.ErrorMessage}"));
            throw;
        }
        catch (Exception ex) when (ex is DomainException or NotFoundException or ConflictException)
        {
            // очікувані бізнес-помилки: попередження без стек-трейсу
            logger.LogWarning("{RequestName} rejected: {Message}", typeof(TRequest).Name, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{RequestName} failed with {ExceptionType}", typeof(TRequest).Name, ex.GetType().Name);
            throw;
        }
    }
}
