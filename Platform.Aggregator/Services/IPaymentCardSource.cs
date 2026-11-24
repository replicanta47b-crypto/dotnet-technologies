using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>
/// Джерело даних для картки платежу. Дві реалізації з однаковою поведінкою: REST (HTTP/1.1 + JSON) і gRPC (HTTP/2 + protobuf).
/// Завдяки цьому перемикач ?transport=http|grpc порівнює протоколи на одному й тому самому сценарії.
/// </summary>
public interface IPaymentCardSource
{
    string Name { get; }

    /// <summary>null — платежу немає. Недоступність сервісу — виняток.</summary>
    Task<PaymentDto?> GetPaymentAsync(long id, CancellationToken ct);

    Task<BillingInvoiceDto?> GetInvoiceAsync(long id, CancellationToken ct);

    Task<IReadOnlyList<DisputeDto>> GetDisputesAsync(long paymentId, CancellationToken ct);
}
