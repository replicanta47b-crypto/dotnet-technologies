using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Platform.Security;

/// <summary>
/// Keycloak кладе ролі в JSON-claim realm_access ({"roles":["manager"]}). Перетворюємо їх на звичайні claims «role»,
/// щоб User.IsInRole і логи бачили ролі однаково для всіх провайдерів. Рішення про доступ приймається за permissions, не за роллю.
/// </summary>
public sealed class KeycloakClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated) return Task.FromResult(principal);
        if (identity.HasClaim(c => c.Type == "role")) return Task.FromResult(principal);   // уже перетворено / ролі від іншого IdP

        var realmAccess = principal.FindFirst("realm_access")?.Value;
        if (realmAccess is null) return Task.FromResult(principal);

        try
        {
            using var doc = JsonDocument.Parse(realmAccess);
            if (doc.RootElement.TryGetProperty("roles", out var roles))
                foreach (var role in roles.EnumerateArray())
                    if (role.GetString() is { Length: > 0 } r)
                        identity.AddClaim(new Claim("role", r));
        }
        catch (JsonException) { /* ігноруємо пошкоджений claim */ }

        return Task.FromResult(principal);
    }
}
