namespace Payments.Domain.Exceptions;

/// <summary>Некоректні вхідні дані (бізнес-валідація в BLL) → HTTP 400.</summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
