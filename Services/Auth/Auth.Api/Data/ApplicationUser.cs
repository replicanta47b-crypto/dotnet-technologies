using Microsoft.AspNetCore.Identity;

namespace Auth.Api.Data;

/// <summary>Користувач платформи. Пароль (хеш), lockout, підтвердження email — з IdentityUser.</summary>
public class ApplicationUser : IdentityUser
{
    public string? FullName { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}
