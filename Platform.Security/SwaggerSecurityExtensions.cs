using Microsoft.Extensions.Configuration;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Platform.Security;

public static class SwaggerSecurityExtensions
{
    /// <summary>
    /// Swagger UI: кнопка Authorize з двома способами входу —
    ///  • «Bearer»: вставити готовий токен (Auth Service, Keycloak RPT, IdentityServer);
    ///  • «IdentityServer»: OAuth 2.0 Authorization Code + PKCE (публічний client «swagger», без секрету).
    /// service — ім'я ресурсу (payments, billing, ...): визначає scopes, які просить Swagger.
    /// </summary>
    public static void AddPlatformSecurity(this SwaggerGenOptions c, IConfiguration configuration, string service)
    {
        var options = new PlatformAuthOptions();
        configuration.GetSection(PlatformAuthOptions.Section).Bind(options);
        var authority = options.IdentityServer.Authority.TrimEnd('/');

        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT access token (Auth Service, IdentityServer або Keycloak RPT для цього сервісу)."
        });

        var scopes = new Dictionary<string, string> { ["openid"] = "OpenID", ["profile"] = "Профіль" };
        if (PlatformPermissions.ByService.TryGetValue(service, out var permissions))
            foreach (var permission in permissions)
                scopes[PlatformPermissions.ToScope(permission)] = permission;

        c.AddSecurityDefinition("IdentityServer", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Description = "Duende IdentityServer: Authorization Code + PKCE (client «swagger»).",
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri($"{authority}/connect/authorize"),
                    TokenUrl = new Uri($"{authority}/connect/token"),
                    Scopes = scopes
                }
            }
        });

        // два альтернативні способи автентифікації
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }] = []
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "IdentityServer" } }] = scopes.Keys.ToList()
        });
    }
}
