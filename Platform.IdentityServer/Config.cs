using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Platform.Security;

namespace Platform.IdentityServer;

/// <summary>
/// Декларативна конфігурація IdentityServer. Scopes і ApiResources ГЕНЕРУЮТЬСЯ з PlatformPermissions — єдиного списку permissions
/// платформи: payments.read, payments.create, ... (крапка замість двокрапки — формат scope в OAuth).
/// Один ApiResource на мікросервіс (payments_api, billing_api, ...): його ім'я потрапляє в claim «aud» токена,
/// і сервіс приймає лише токени зі своїм audience.
/// </summary>
public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
        new IdentityResource("roles", "Ролі", ["role"])
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
        PlatformPermissions.All.Select(p =>
        {
            var scope = PlatformPermissions.ToScope(p);
            return new ApiScope(scope, $"Дозвіл {p}");
        });

    public static IEnumerable<ApiResource> ApiResources =>
        PlatformPermissions.ByService.Select(kv => new ApiResource($"{kv.Key}_api", $"{kv.Key} API")
        {
            Scopes = kv.Value.Select(PlatformPermissions.ToScope).ToList(),
            UserClaims = ["role", "name", "email"]
        });

    private static readonly string[] AllScopes = PlatformPermissions.All.Select(PlatformPermissions.ToScope).ToArray();

    // origins, з яких працюють Swagger UI сервісів (токен-endpoint викликається з браузера → CORS)
    private static readonly int[] ServicePorts = [5080, 5090, 5100, 5110, 5120];

    public static IEnumerable<Client> Clients(IConfiguration config)
    {
        var origins = ServicePorts.SelectMany(p => new[] { $"http://localhost:{p}", $"https://localhost:{p + 2000}" })
            .Concat(["http://localhost:5000"]).ToArray();

        // 1) Postman — конфіденційний клієнт, Authorization Code + PKCE (від імені користувача)
        yield return new Client
        {
            ClientId = "postman",
            ClientName = "Postman",
            ClientSecrets = { new Secret(config["Seed:PostmanSecret"]!.Sha256()) },
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireConsent = false,
            RedirectUris = { "https://oauth.pstmn.io/v1/callback", "https://oauth.pstmn.io/v1/browser-callback" },
            PostLogoutRedirectUris = { "https://oauth.pstmn.io/v1/callback" },
            AllowedScopes = ["openid", "profile", "email", "roles", .. AllScopes],
            AllowOfflineAccess = true,
            AccessTokenLifetime = 900
        };

        // 2) aggregator_service — Client Credentials (сервіс-до-сервісу, без користувача: у токені немає «sub»)
        yield return new Client
        {
            ClientId = "aggregator_service",
            ClientName = "Platform Aggregator (service)",
            ClientSecrets = { new Secret(config["Seed:AggregatorSecret"]!.Sha256()) },
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            AllowedScopes = ["payments.read", "billing.read", "disputes.read"],
            AccessTokenLifetime = 3600
        };

        // 3) swagger — ПУБЛІЧНИЙ клієнт (SPA у браузері, секрет зберігати ніде): Authorization Code + обов'язковий PKCE.
        //    Без PKCE запит відхиляється (контрольна точка завдання).
        yield return new Client
        {
            ClientId = "swagger",
            ClientName = "Swagger UI",
            RequireClientSecret = false,
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireConsent = false,
            RedirectUris = origins.Select(o => $"{o}/swagger/oauth2-redirect.html").ToList(),
            AllowedCorsOrigins = origins,
            AllowedScopes = ["openid", "profile", "email", "roles", .. AllScopes],
            AccessTokenLifetime = 900
        };
    }
}
