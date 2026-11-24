using Disputes.Domain.Common;
using Disputes.Domain.Exceptions;

namespace Disputes.Domain.ValueObjects;

/// <summary>Коментар до спору (елемент вкладеного масиву comments): { author, text, createdAt }.</summary>
public sealed class Comment : ValueObject
{
    public string AuthorName { get; }
    public string Text { get; }
    public DateTime CreatedAt { get; }

    public Comment(string authorName, string text, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(authorName))
            throw new DomainException("Comment author is required");
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Comment text is required");
        if (text.Length > 2000)
            throw new DomainException("Comment text is too long (max 2000)");

        AuthorName = authorName.Trim();
        Text = text.Trim();
        CreatedAt = createdAt;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return AuthorName;
        yield return Text;
        yield return CreatedAt;
    }
}
