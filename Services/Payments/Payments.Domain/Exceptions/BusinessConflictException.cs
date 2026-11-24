namespace Payments.Domain.Exceptions;

/// <summary>
/// Порушення бізнес-правила / конфлікт стану → HTTP 409.
/// (недопустимий перехід статусу — THROW 50002, дубль унікального ключа — 2601/2627 тощо)
/// </summary>
public class BusinessConflictException : Exception
{
    public BusinessConflictException(string message) : base(message) { }
    public BusinessConflictException(string message, Exception? inner) : base(message, inner) { }
}
