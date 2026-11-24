namespace Payments.Domain.Exceptions;

/// <summary>Ресурс не знайдено → HTTP 404. (THROW 50001 / 50006 / 50008 у процедурах)</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }
    public NotFoundException(string message, Exception? inner) : base(message, inner) { }
}
