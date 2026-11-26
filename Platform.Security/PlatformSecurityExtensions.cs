using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Platform.Security;

public static class PlatformSecurityExtensions
{
    /// <summary>Складена схема: за issuer токена перенаправляє перевірку на потрібний JwtBearer.</summary>
    public const string PlatformScheme = "Platform";
    public const string AuthServiceScheme = "AuthService";
    public const string IdentityServerScheme = "IdentityServer";
    public const string KeycloakScheme = "Keycloak";

    /// <summary>
    /// Автентифікація JWT для мікросервісу. «service» — ім'я ресурсу: з нього виводиться audience
    /// (IdentityServer: payments_api, Keycloak: payments-api). Для Gateway й Aggregator (service = null) audience не перевіряється:
    /// вони приймають токен будь-якого сервісу платформи, а кожен мікросервіс далі валідує свій audience САМ.
    /// Мікросервіс НЕ звертається до IdP на кожен запит: підпис перевіряється ключами, завантаженими один раз (OIDC discovery).
    /// </summary>
    public static WebApplicationBuilder AddPlatformAuthentication(this WebApplicationBuilder builder, string? service)
    {
        var options = new PlatformAuthOptions();
        builder.Configuration.GetSection(PlatformAuthOptions.Section).Bind(options);
        builder.Services.AddOptions<PlatformAuthOptions>().Bind(builder.Configuration.GetSection(PlatformAuthOptions.Section));

        var validateAudience = service is not null;
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.AddAuthentication(o =>
            {
                o.DefaultScheme = PlatformScheme;
                o.DefaultAuthenticateScheme = PlatformScheme;
                o.DefaultChallengeScheme = PlatformScheme;
            })
            .AddPolicyScheme(PlatformScheme, "Auth Service | IdentityServer | Keycloak", o =>
                o.ForwardDefaultSelector = context => SelectScheme(context, options))
            // 1) Auth Service: підписує HS256 спільним секретом; ролі — claim «role»
            .AddJwtBearer(AuthServiceScheme, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = options.AuthService.Issuer,
                    ValidateAudience = validateAudience,
                    ValidAudience = options.AuthService.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.AuthService.SecretKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "unique_name",
                    RoleClaimType = "role",
                };
            })
            // 2) Duende IdentityServer: Authority → discovery → ключі підпису; audience = ApiResource (payments_api)
            .AddJwtBearer(IdentityServerScheme, o =>
            {
                o.Authority = options.IdentityServer.Authority;
                o.RequireHttpsMetadata = !isDevelopment;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = validateAudience,
                    ValidAudience = service is null ? null : $"{service}_api",
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "name",
                    RoleClaimType = "role",
                };
            })
            // 3) Keycloak: Authority = URL realm; audience = resource server (payments-api), RPT містить authorization.permissions
            .AddJwtBearer(KeycloakScheme, o =>
            {
                o.Authority = options.Keycloak.Authority;
                o.RequireHttpsMetadata = !isDevelopment;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateAudience = validateAudience,
                    ValidAudience = service is null ? null : $"{service}-api",
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "preferred_username",
                    RoleClaimType = "role",
                };
            });

        builder.Services.AddSingleton<IClaimsTransformation, KeycloakClaimsTransformation>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

        builder.Services.AddAuthorization(o =>
        {
            // політика на кожен permission (таблиці 8–10): «payments:update» — для Keycloak/Auth Service,
            // «payments.update» — для scope-based авторизації IdentityServer ([Authorize(Policy = "payments.update")])
            foreach (var permission in PlatformPermissions.All)
            {
                o.AddPolicy(permission, p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
                o.AddPolicy(PlatformPermissions.ToScope(permission), p => p.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission)));
            }

            o.AddPolicy("authenticated", p => p.RequireAuthenticatedUser());
        });

        return builder;
    }

    /// <summary>Порядок важливий: UseAuthentication → UseAuthorization (після UseRouting, до MapControllers).</summary>
    public static WebApplication UsePlatformAuthorization(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    private static string SelectScheme(HttpContext context, PlatformAuthOptions options)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthServiceScheme;                         // без токена — будь-яка схема дасть 401

        var token = header["Bearer ".Length..].Trim();
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            return AuthServiceScheme;                         // не JWT → AuthService відхилить із 401

        var issuer = handler.ReadJwtToken(token).Issuer;      // лише читання (без довіри): підпис перевіряє вибраний JwtBearer

        if (string.Equals(issuer, options.AuthService.Issuer, StringComparison.Ordinal)) return AuthServiceScheme;
        if (string.Equals(issuer.TrimEnd('/'), options.IdentityServer.Authority.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return IdentityServerScheme;
        if (string.Equals(issuer.TrimEnd('/'), options.Keycloak.Authority.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return KeycloakScheme;

        return AuthServiceScheme;                             // невідомий issuer → 401
    }
}
