namespace Disputes.Application.Dtos;

/// <summary>Короткий опис спору для списків. Reason/Status — snake_case, як у документі MongoDB.</summary>
public record DisputeDto(
    string Id,
    long PaymentId,
    long InvoiceId,
    long MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency,
    string Reason,
    string Status,
    string OpenedByUserId,
    string OpenedByName,
    DateTime OpenedAt,
    int CommentsCount,
    int Version);

public record EvidenceDto(string Type, string? Url, DateTime AddedAt);

public record CommentDto(string Author, string Text, DateTime CreatedAt);

/// <summary>Повний опис спору з вкладеними доказами й коментарями.</summary>
public record DisputeDetailsDto(
    string Id,
    long PaymentId,
    long InvoiceId,
    long MerchantId,
    string MerchantName,
    decimal Amount,
    string Currency,
    string Reason,
    string Status,
    string OpenedByUserId,
    string OpenedByName,
    DateTime OpenedAt,
    DateTime? ResolvedAt,
    string? ThreadId,
    int Version,
    IReadOnlyList<EvidenceDto> Evidence,
    IReadOnlyList<CommentDto> Comments);
