namespace Disputes.WebApi.Contracts;

/// <summary>Тіло POST /api/disputes. Reason: fraud | service_not_provided | duplicate | amount_mismatch | other.</summary>
public record CreateDisputeRequest(
    long PaymentId,
    long InvoiceId,
    long MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency,
    string Reason,
    string OpenedByUserId,
    string OpenedByName);

/// <summary>Тіло PUT /api/disputes/{id}. Status: open | under_review | won | lost | closed.</summary>
public record UpdateDisputeRequest(string Status);

public record AddCommentRequest(string Author, string Text);
