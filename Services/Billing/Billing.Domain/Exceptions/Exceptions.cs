namespace Billing.Domain.Exceptions;

/// <summary>Порушення бізнес-правила сутності (наприклад, недопустимий перехід статусу). → HTTP 409.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>Ресурс не знайдено. → HTTP 404.</summary>
public class EntityNotFoundException : Exception
{
    public EntityNotFoundException(string entityName, object key)
        : base($"{entityName} with key '{key}' was not found.") { }

    public EntityNotFoundException(string message) : base(message) { }
}

/// <summary>Конфлікт зі станом даних (дубль унікального ключа, конкурентна зміна). → HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
    public ConflictException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Некоректні вхідні дані, виявлені в бізнес-логіці (не DataAnnotations/FluentValidation). → HTTP 400.</summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
