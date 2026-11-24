using Disputes.Application.Common.Interfaces;

namespace Disputes.Application.Commands.CreateDispute;

/// <summary>Відкриття спору. Reason — snake_case (fraud, service_not_provided, duplicate, amount_mismatch, other).</summary>
public record CreateDisputeCommand(
    long PaymentId,
    long InvoiceId,
    long MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency,
    string Reason,
    string OpenedByUserId,
    string OpenedByName) : ICommand<string>;
