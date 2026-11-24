using FluentValidation;
using MediatR;

namespace Disputes.Application.Behaviors;

/// <summary>
/// Виконує всі валідатори FluentValidation для запиту ще ДО обробника. Збирає всі помилки разом
/// і кидає ValidationException — обробник при цьому не викликається.
/// </summary>
public class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

            if (failures.Count > 0)
                throw new ValidationException(failures);   // обробник не викличеться
        }

        return await next();
    }
}
