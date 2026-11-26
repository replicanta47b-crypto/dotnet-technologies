using Auth.Api.Data;
using Auth.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Security;

namespace Auth.Api.Services;

/// <summary>Видача пари токенів: access (тіло відповіді) + refresh (HttpOnly cookie, хеш — у БД); rotation і відкликання.</summary>
public sealed class TokenIssuer(
    ApplicationDbContext db, UserManager<ApplicationUser> users, IJwtTokenService jwt, IOptions<PlatformAuthOptions> options)
{
    public const string CookieName = "refreshToken";

    public async Task<AuthResponse> IssueAsync(ApplicationUser user, HttpContext http, RefreshToken? replacing = null)
    {
        var roles = await users.GetRolesAsync(user);
        var access = jwt.GenerateAccessToken(user, roles);

        var refresh = jwt.GenerateRefreshToken();
        var refreshHash = jwt.Hash(refresh);
        var days = options.Value.AuthService.RefreshTokenDays;
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(days),
            CreatedByIp = Ip(http)
        };
        db.RefreshTokens.Add(entity);

        if (replacing is not null)   // rotation: старий токен відкликається і вказує на наступний
        {
            replacing.RevokedAtUtc = DateTime.UtcNow;
            replacing.RevokedByIp = Ip(http);
            replacing.ReasonRevoked = "Replaced by new token (rotation)";
            replacing.ReplacedByTokenHash = refreshHash;
        }
        await db.SaveChangesAsync();

        SetCookie(http, refresh, entity.ExpiresAtUtc);
        var expiresIn = (int)(access.ExpiresAtUtc - DateTime.UtcNow).TotalSeconds;
        return new AuthResponse(access.Value, "Bearer", expiresIn, access.ExpiresAtUtc, await ToDtoAsync(user));
    }

    public async Task<UserDto> ToDtoAsync(ApplicationUser u)
        => new(u.Id, u.UserName!, u.Email!, u.EmailConfirmed, u.FullName, u.CreatedAtUtc, (await users.GetRolesAsync(u)).ToList());

    public async Task RevokeAllAsync(string userId, HttpContext http, string reason)
    {
        var active = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null).ToListAsync();
        foreach (var t in active)
        {
            t.RevokedAtUtc = DateTime.UtcNow;
            t.RevokedByIp = Ip(http);
            t.ReasonRevoked = reason;
        }
        await db.SaveChangesAsync();
    }

    public static void SetCookie(HttpContext http, string token, DateTime expiresUtc) =>
        http.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,                       // JavaScript не бачить cookie → XSS не вкраде refresh-токен
            Secure = http.Request.IsHttps,         // на https — лише через TLS (локально по http Secure-cookie браузер не прийняв би)
            SameSite = SameSiteMode.Strict,        // захист від CSRF
            Expires = expiresUtc,
            Path = "/auth"                         // cookie їде лише на /auth/* (refresh, revoke, logout)
        });

    public static void DeleteCookie(HttpContext http) =>
        http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/auth" });

    public static string? Ip(HttpContext http) => http.Connection.RemoteIpAddress?.ToString();
}
