using Pulumi;
using Kc = Pulumi.Keycloak;

namespace DevRealmDrivers.Iac;

/// <summary>Клієнт Keycloak разом з контекстом, потрібним для розширень (WithDefaultScopes, WithAudience).</summary>
public sealed class ManagedClient(Kc.OpenId.Client client, string clientId, Kc.Realm realm, CustomResourceOptions options)
{
    public Kc.OpenId.Client Client { get; } = client;
    public string ClientId { get; } = clientId;
    public Kc.Realm Realm { get; } = realm;
    public CustomResourceOptions Options { get; } = options;
}

/// <summary>
/// Фабрика клієнтів (Factory Pattern): один метод на тип потоку OAuth 2.0 — замість копіювання десятка прапорців у кожному місці.
/// </summary>
public static class ClientFactory
{
    /// <summary>Authorization Code + PKCE (S256): від імені користувача (Postman, веб-застосунок).</summary>
    public static ManagedClient CreateAuthorizationCode(
        Kc.Realm realm, CustomResourceOptions options, string clientId, string name,
        string[] redirectUris, string[] webOrigins, bool allowDirectAccessGrants = false)
    {
        var client = new Kc.OpenId.Client(clientId, new Kc.OpenId.ClientArgs
        {
            RealmId = realm.Id,
            ClientId = clientId,
            Name = name,
            Enabled = true,
            AccessType = "CONFIDENTIAL",
            StandardFlowEnabled = true,
            PkceCodeChallengeMethod = "S256",
            // DEV ONLY: Resource Owner Password дозволяє тестам отримати токен ролі одним запитом (перевірка матриці).
            DirectAccessGrantsEnabled = allowDirectAccessGrants,
            ServiceAccountsEnabled = false,
            ValidRedirectUris = redirectUris.ToList(),
            WebOrigins = webOrigins.ToList(),
            FullScopeAllowed = true,
        }, options);
        return new ManagedClient(client, clientId, realm, options);
    }

    /// <summary>Client Credentials: сервіс-до-сервісу, без користувача (service account).</summary>
    public static ManagedClient CreateClientCredentials(
        Kc.Realm realm, CustomResourceOptions options, string clientId, string name)
    {
        var client = new Kc.OpenId.Client(clientId, new Kc.OpenId.ClientArgs
        {
            RealmId = realm.Id,
            ClientId = clientId,
            Name = name,
            Enabled = true,
            AccessType = "CONFIDENTIAL",
            StandardFlowEnabled = false,
            DirectAccessGrantsEnabled = false,
            ServiceAccountsEnabled = true,
        }, options);
        return new ManagedClient(client, clientId, realm, options);
    }
}

public static class ClientExtensions
{
    /// <summary>Набір default client scopes. «basic» обов'язковий у Keycloak 26: у ньому mapper claim «sub».</summary>
    public static ManagedClient WithDefaultScopes(this ManagedClient c, params string[] scopes)
    {
        _ = new Kc.OpenId.ClientDefaultScopes($"{c.ClientId}-default-scopes", new Kc.OpenId.ClientDefaultScopesArgs
        {
            RealmId = c.Realm.Id,
            ClientId = c.Client.Id,
            DefaultScopes = scopes.ToList(),
        }, c.Options);
        return c;
    }

    /// <summary>Audience mapper (audienceClientId — Output створюваного resource server: провайдер перевіряє, що такий клієнт існує, лише коли він уже створений): додає ім'я resource server (payments-api) у claim «aud» access-токена клієнта.</summary>
    public static ManagedClient WithAudience(this ManagedClient c, string audienceKey, Input<string> audienceClientId)
    {
        _ = new Kc.OpenId.AudienceProtocolMapper($"{c.ClientId}-aud-{audienceKey}", new Kc.OpenId.AudienceProtocolMapperArgs
        {
            RealmId = c.Realm.Id,
            ClientId = c.Client.Id,
            Name = $"audience-{audienceKey}",
            IncludedClientAudience = audienceClientId,
            AddToAccessToken = true,
            AddToIdToken = false,
        }, c.Options);
        return c;
    }

    /// <summary>Роль realm для service account клієнта (Client Credentials без ролей не отримає жодного permission).</summary>
    public static ManagedClient WithServiceAccountRole(this ManagedClient c, string roleName)
    {
        _ = new Kc.OpenId.ClientServiceAccountRealmRole($"{c.ClientId}-sa-{roleName}", new Kc.OpenId.ClientServiceAccountRealmRoleArgs
        {
            RealmId = c.Realm.Id,
            ServiceAccountUserId = c.Client.ServiceAccountUserId,
            Role = roleName,
        }, c.Options);
        return c;
    }
}
