using System.Security.Claims;
using System.Text.Json;

namespace Platform.Security;

/// <summary>
/// Дістає permissions користувача з токена. Токен може прийти від одного з трьох провайдерів, тому форм кілька:
///  1) Keycloak RPT: claim «authorization» = {"permissions":[{"rsname":"payments-api","scopes":["payments:read",...]}]};
///  2) IdentityServer: claim «scope» (payments.read) — Authorization Code (від імені користувача) або Client Credentials (сервіс);
///  3) Auth Service: лише ролі (User/Admin) — permissions виводяться з матриці PlatformRoles.Matrix.
/// Звернень до провайдера ідентичності немає: усе береться з підписаного JWT (offline-валідація).
/// </summary>
public static class PermissionClaims
{
    private static readonly string[] RoleClaimTypes = ["role", ClaimTypes.Role];

    public static HashSet<string> GetPermissions(ClaimsPrincipal user, PlatformAuthOptions options)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1) Keycloak: authorization.permissions[].scopes[]
        foreach (var claim in user.FindAll("authorization"))
            AddFromAuthorizationClaim(claim.Value, result);

        // 2) IdentityServer: scope — окремі claims або рядок через пробіл
        foreach (var claim in user.FindAll("scope"))
            foreach (var scope in claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (scope.Contains('.') || scope.Contains(':'))
                    result.Add(PlatformPermissions.Normalize(scope));

        // 3) Auth Service: ролі → матриця. Лише для токенів, виданих нашим Auth Service (перевіряємо issuer),
        //    щоб «role» у чужому токені не давало прав.
        if (IsFromAuthService(user, options))
            foreach (var role in RoleClaimTypes.SelectMany(user.FindAll).Select(c => c.Value))
                if (PlatformRoles.Matrix.TryGetValue(role, out var permissions))
                    foreach (var permission in permissions)
                        result.Add(permission);

        return result;
    }

    public static bool IsFromAuthService(ClaimsPrincipal user, PlatformAuthOptions options)
        => string.Equals(user.FindFirst("iss")?.Value, options.AuthService.Issuer, StringComparison.Ordinal);

    private static void AddFromAuthorizationClaim(string json, HashSet<string> result)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("permissions", out var permissions)) return;
            foreach (var permission in permissions.EnumerateArray())
            {
                if (!permission.TryGetProperty("scopes", out var scopes)) continue;
                foreach (var scope in scopes.EnumerateArray())
                    if (scope.GetString() is { Length: > 0 } s)
                        result.Add(PlatformPermissions.Normalize(s));
            }
        }
        catch (JsonException)
        {
            // пошкоджений claim не дає жодного permission — запит буде відхилено (403)
        }
    }
}
