namespace Auth.Api.Data;

/// <summary>
/// Refresh-токен у БД. Сам токен НЕ зберігається — лише SHA-256 хеш (витік БД не дає робочих токенів).
/// Rotation: при кожному оновленні старий токен відкликається (RevokedAtUtc, ReplacedByTokenHash), видається новий.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;

    public string TokenHash { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string? CreatedByIp { get; set; }

    public DateTime? RevokedAtUtc { get; set; }
    public string? RevokedByIp { get; set; }
    public string? ReasonRevoked { get; set; }
    public string? ReplacedByTokenHash { get; set; }

    public bool IsRevoked => RevokedAtUtc is not null;
    public bool IsExpired => DateTime.UtcNow >= ExpiresAtUtc;
    public bool IsActive => !IsRevoked && !IsExpired;
}
