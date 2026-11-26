namespace Platform.Security;

/// <summary>
/// Налаштування (секція «Auth» конфігурації). Значення за замовчуванням — для локальної розробки; AppHost перевизначає
/// секретний ключ змінною Auth__AuthService__SecretKey, щоб він був спільним для Auth Service і всіх сервісів.
/// </summary>
public sealed class PlatformAuthOptions
{
    public const string Section = "Auth";

    public AuthServiceOptions AuthService { get; set; } = new();
    public IdentityServerOptions IdentityServer { get; set; } = new();
    public KeycloakOptions Keycloak { get; set; } = new();

    /// <summary>Підхід 1: власний Auth Service (ASP.NET Identity + JWT, симетричний ключ HS256).</summary>
    public sealed class AuthServiceOptions
    {
        public string Issuer { get; set; } = "platform-auth";
        public string Audience { get; set; } = "platform-api";
        public string SecretKey { get; set; } = "dev-only-secret-key-change-me-0123456789-abcdef";
        public int AccessTokenMinutes { get; set; } = 15;
        public int RefreshTokenDays { get; set; } = 7;
    }

    /// <summary>Підхід 2: Duende IdentityServer (OAuth 2.0 / OIDC). Ключі підпису беруться з discovery document.</summary>
    public sealed class IdentityServerOptions
    {
        public string Authority { get; set; } = "http://localhost:5300";
    }

    /// <summary>Підхід 3: Keycloak, realm DevRealmDrivers. Authority = URL realm.</summary>
    public sealed class KeycloakOptions
    {
        public string Authority { get; set; } = "http://localhost:8080/realms/DevRealmDrivers";
    }
}
