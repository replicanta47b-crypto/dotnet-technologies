using Disputes.Domain.Common;
using Disputes.Domain.Exceptions;

namespace Disputes.Domain.ValueObjects;

/// <summary>Автор/ініціатор спору. Вкладений документ openedBy: { userId, displayName }.</summary>
public sealed class Author : ValueObject
{
    public string UserId { get; }
    public string DisplayName { get; }

    public Author(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("Author userId is required");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Author displayName is required");
        if (displayName.Length > 100)
            throw new DomainException("Author displayName is too long (max 100)");

        UserId = userId.Trim();
        DisplayName = displayName.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return UserId;
        yield return DisplayName;
    }
}
