using Disputes.Domain.Common;
using Disputes.Domain.Exceptions;

namespace Disputes.Domain.ValueObjects;

/// <summary>Доказ у спорі (елемент масиву evidence): { type, url?, addedAt }.</summary>
public sealed class EvidenceItem : ValueObject
{
    public string Type { get; }
    public string? Url { get; }
    public DateTime AddedAt { get; }

    public EvidenceItem(string type, string? url, DateTime addedAt)
    {
        if (string.IsNullOrWhiteSpace(type))
            throw new DomainException("Evidence type is required");
        if (url is { Length: > 500 })
            throw new DomainException("Evidence url is too long (max 500)");

        Type = type.Trim();
        Url = string.IsNullOrWhiteSpace(url) ? null : url.Trim();
        AddedAt = addedAt;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Type;
        yield return Url;
        yield return AddedAt;
    }
}
