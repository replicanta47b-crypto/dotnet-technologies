using System.Security.Claims;
using Auth.Api.Data;

namespace Auth.Api.Services;

public interface IJwtTokenService
{
    /// <summary>JWT на 15 хв: claims sub, userId, unique_name, email, jti, role (по одному на роль).</summary>
    AccessToken GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles);

    /// <summary>Криптографічно випадковий refresh-токен (64 байти, base64url). У БД зберігається лише його хеш.</summary>
    string GenerateRefreshToken();

    /// <summary>Перевіряє підпис, issuer, audience і строк дії; null — токен недійсний.</summary>
    ClaimsPrincipal? ValidateToken(string token);

    /// <summary>SHA-256 (hex) — те, що лежить у таблиці RefreshTokens.</summary>
    string Hash(string token);
}

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);
