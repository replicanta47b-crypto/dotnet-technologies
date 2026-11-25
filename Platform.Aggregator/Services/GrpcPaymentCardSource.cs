using System.Globalization;
using Grpc.Core;
using Platform.Aggregator.Models;
using Platform.Aggregator.Resilience;
using Platform.Contracts.Billing;
using Platform.Contracts.Disputes;
using Platform.Contracts.Payments;

namespace Platform.Aggregator.Services;

/// <summary>
/// gRPC-транспорт (Лаб. 06): згенеровані клієнти (AddGrpcClient) + мапінг proto → DTO агрегатора.
/// RpcException(NotFound) → null, як 404 у REST-шляху; решта помилок (Unavailable, DeadlineExceeded…) → виняток.
/// </summary>
public sealed class GrpcPaymentCardSource(
    PaymentsService.PaymentsServiceClient payments,
    BillingService.BillingServiceClient billing,
    DisputesService.DisputesServiceClient disputes) : IPaymentCardSource
{
    // дедлайн — частина контракту виклику: клієнт не чекає довше, ніж потрібно; значення залежить від критичності сервісу
    private static DateTime Deadline(ClientPolicy p) => DateTime.UtcNow.Add(p.GrpcDeadline);

    public string Name => "grpc";

    public async Task<PaymentDto?> GetPaymentAsync(long id, CancellationToken ct)
    {
        try
        {
            var r = await payments.GetPaymentAsync(new GetPaymentRequest { Id = id }, deadline: Deadline(ClientPolicies.Payments), cancellationToken: ct);
            return new PaymentDto(
                r.Id, r.CustomerId, r.Currency, r.Status,
                DateTime.Parse(r.InitiatedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Dec(r.TotalAmount),
                r.Allocations.Select(a => new PaymentAllocationDto(
                    a.InvoiceId, a.InvoiceNumber, Dec(a.InvoiceAmount), Dec(a.AllocatedAmount))).ToList());
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<BillingInvoiceDto?> GetInvoiceAsync(long id, CancellationToken ct)
    {
        try
        {
            var r = await billing.GetInvoiceAsync(new GetInvoiceRequest { Id = id }, deadline: Deadline(ClientPolicies.Billing), cancellationToken: ct);
            return new BillingInvoiceDto(
                r.Id, r.Number, r.MerchantId, r.MerchantName, r.Currency, Dec(r.TotalAmount),
                DateOnly.ParseExact(r.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture), r.Status);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<DisputeDto>> GetDisputesAsync(long paymentId, CancellationToken ct)
    {
        var r = await disputes.GetDisputesByPaymentAsync(
            new GetDisputesByPaymentRequest { PaymentId = paymentId, PageSize = 50 }, deadline: Deadline(ClientPolicies.Disputes), cancellationToken: ct);
        return r.Items.Select(d => new DisputeDto(
            d.Id, d.PaymentId, d.InvoiceId, d.MerchantId, d.MerchantName, Dec(d.Amount), d.Currency, d.Reason, d.Status,
            DateTime.Parse(d.OpenedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), d.CommentsCount)).ToList();
    }

    private static decimal Dec(string value) => decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
}
