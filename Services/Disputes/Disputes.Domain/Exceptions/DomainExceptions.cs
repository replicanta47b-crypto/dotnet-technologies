namespace Disputes.Domain.Exceptions;

/// <summary>Порушення бізнес-правила сутності. → HTTP 400.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message) { }
}

/// <summary>Ресурс не знайдено. → HTTP 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string entity, object key)
        : base($"{entity} with id '{key}' was not found.") { }
}

/// <summary>Конфлікт зі станом даних (зокрема конфлікт версій документа). → HTTP 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}
