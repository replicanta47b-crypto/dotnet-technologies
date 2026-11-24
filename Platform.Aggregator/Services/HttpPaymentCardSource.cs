using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>REST-транспорт (Лаб. 05): типізовані HttpClient.</summary>
public sealed class HttpPaymentCardSource(PaymentsClient payments, BillingClient billing, DisputesClient disputes)
    : IPaymentCardSource
{
    public string Name => "http";

    public Task<PaymentDto?> GetPaymentAsync(long id, CancellationToken ct) => payments.GetPaymentAsync(id, ct);

    public Task<BillingInvoiceDto?> GetInvoiceAsync(long id, CancellationToken ct) => billing.GetInvoiceAsync(id, ct);

    public Task<IReadOnlyList<DisputeDto>> GetDisputesAsync(long paymentId, CancellationToken ct)
        => disputes.GetByPaymentAsync(paymentId, ct);
}
