using DevRealmDrivers.Iac;
using Pulumi;
using Kc = Pulumi.Keycloak;

return await Deployment.RunAsync(() =>
{
    var config = new Config();
    var url = config.Get("keycloakUrl") ?? "http://localhost:8080";
    var adminUser = config.Get("keycloakAdminUser") ?? "admin";
    var adminPassword = config.Get("keycloakAdminPassword") ?? "admin";                 // dev-значення = KC_BOOTSTRAP_ADMIN_PASSWORD в AppHost
    var testUserPassword = config.Get("testUserPassword") ?? "Test#User12345";          // тимчасовий: Keycloak змусить змінити при першому вході

    // ---- провайдер: Admin REST API Keycloak. BasePath "" — з Keycloak 17+ префікса /auth немає ----
    var provider = new Kc.Provider("keycloak", new Kc.ProviderArgs
    {
        Url = url,
        Realm = "master",
        ClientId = "admin-cli",
        Username = adminUser,
        Password = adminPassword,
        BasePath = "",
        InitialLogin = true,
    });
    var options = new CustomResourceOptions { Provider = provider };

    // ---- 1. Realm ----
    var realm = new Kc.Realm("realm", new Kc.RealmArgs
    {
        RealmName = PlatformModel.RealmName,
        DisplayName = "Dev Realm Drivers",
        Enabled = true,
        SslRequired = "none",                 // локальна розробка по http
        AccessTokenLifespan = "15m",
        PasswordPolicy = "length(8)",
    }, options);

    // ---- 2. Realm roles: admin, manager, user, guest ----
    var roles = PlatformModel.Roles.ToDictionary(
        name => name,
        name => new Kc.Role($"role-{name}", new Kc.RoleArgs { RealmId = realm.Id, Name = name, Description = $"Роль платформи: {name}" }, options));

    // ---- 3. Resource servers: клієнт з Authorization Services + scopes + resource + role policies + scope permissions ----
    var resourceServers = new Dictionary<string, Kc.OpenId.Client>();
    foreach (var spec in PlatformModel.Servers)
    {
        var server = new Kc.OpenId.Client(spec.ClientId, new Kc.OpenId.ClientArgs
        {
            RealmId = realm.Id,
            ClientId = spec.ClientId,
            Name = spec.ClientId,
            Enabled = true,
            AccessType = "CONFIDENTIAL",
            ServiceAccountsEnabled = true,       // вимога Authorization Services
            StandardFlowEnabled = false,
            DirectAccessGrantsEnabled = false,
            Authorization = new Kc.OpenId.Inputs.ClientAuthorizationArgs
            {
                PolicyEnforcementMode = "ENFORCING",   // без явного дозволу — відмова
                DecisionStrategy = "AFFIRMATIVE",
                AllowRemoteResourceManagement = false,
                KeepDefaults = false,                  // прибрати «Default Resource/Policy/Permission» (вони дозволяють усе)
            },
        }, options);
        resourceServers[spec.ClientId] = server;

        // 3a. Authorization scopes
        var scopes = spec.Scopes.ToDictionary(
            scope => scope,
            scope => new Kc.OpenId.ClientAuthorizationScope($"{spec.ClientId}-scope-{scope}", new Kc.OpenId.ClientAuthorizationScopeArgs
            {
                ResourceServerId = server.ResourceServerId,
                RealmId = realm.Id,
                Name = scope,
            }, options));

        // 3b. Resource сервісу з усіма його scopes
        var resource = new Kc.OpenId.ClientAuthorizationResource($"{spec.ClientId}-resource", new Kc.OpenId.ClientAuthorizationResourceArgs
        {
            ResourceServerId = server.ResourceServerId,
            RealmId = realm.Id,
            Name = spec.ResourceName,
            DisplayName = $"{spec.ResourceName} API",
            Uris = { spec.Uri },
            Scopes = ToInputList(scopes.Values.Select(s => s.Name)),
        }, options);

        // 3c. Permission policies: по одній role-політиці на роль
        var policies = PlatformModel.Roles.ToDictionary(
            role => role,
            role => new Kc.OpenId.ClientRolePolicy($"{spec.ClientId}-policy-{role}", new Kc.OpenId.ClientRolePolicyArgs
            {
                ResourceServerId = server.ResourceServerId,
                RealmId = realm.Id,
                Name = $"{role}-policy",
                Type = "role",
                Logic = "POSITIVE",
                DecisionStrategy = "UNANIMOUS",
                Roles = new[] { new Kc.OpenId.Inputs.ClientRolePolicyRoleArgs { Id = roles[role].Id, Required = true } },
            }, options));

        // 3d. Scope permission: для кожного scope — ролі, яким він дозволений (матриця PlatformModel.RolePermissions)
        foreach (var scope in spec.Scopes)
        {
            var allowedRoles = PlatformModel.Roles.Where(r => PlatformModel.RolePermissions[r].Contains(scope)).ToList();
            if (allowedRoles.Count == 0) continue;

            _ = new Kc.OpenId.ClientAuthorizationPermission($"{spec.ClientId}-perm-{scope}", new Kc.OpenId.ClientAuthorizationPermissionArgs
            {
                ResourceServerId = server.ResourceServerId,
                RealmId = realm.Id,
                Name = $"{scope}-permission",
                Type = "scope",
                DecisionStrategy = "AFFIRMATIVE",   // достатньо однієї з ролей
                Policies = ToInputList(allowedRoles.Select(r => policies[r].Id)),
                Resources = ToInputList([resource.Id]),
                Scopes = ToInputList([scopes[scope].Id]),
            }, options);
        }
    }

    // ---- 4. Клієнти (фабрика + extension methods) ----
    var allAudiences = PlatformModel.Servers.Select(s => s.ClientId).ToArray();

    var postman = ClientFactory.CreateAuthorizationCode(
            realm, options, "postman", "Postman (Authorization Code + PKCE)",
            redirectUris: ["https://oauth.pstmn.io/v1/callback", "https://oauth.pstmn.io/v1/browser-callback"],
            webOrigins: ["http://localhost:5000", "https://oauth.pstmn.io"],
            allowDirectAccessGrants: true)
        .WithDefaultScopes("profile", "email", "roles", "web-origins", "acr", "basic");
    foreach (var (key, server) in resourceServers) postman.WithAudience(key, server.ClientId);

    var aggregator = ClientFactory.CreateClientCredentials(realm, options, "aggregator-service", "Platform Aggregator (Client Credentials)")
        .WithDefaultScopes("profile", "email", "roles", "web-origins", "acr", "basic")
        .WithServiceAccountRole("user");
    foreach (var (key, server) in resourceServers) aggregator.WithAudience(key, server.ClientId);

    // ---- 5. Тестові користувачі: по одному на роль, ТИМЧАСОВИЙ пароль ----
    foreach (var role in PlatformModel.Roles)
    {
        var user = new Kc.User($"user-{role}", new Kc.UserArgs
        {
            RealmId = realm.Id,
            Username = $"test-{role}",
            Email = $"test-{role}@platform.local",
            EmailVerified = true,
            Enabled = true,
            FirstName = "Test",
            LastName = char.ToUpper(role[0]) + role[1..],
            InitialPassword = new Kc.Inputs.UserInitialPasswordArgs { Value = testUserPassword, Temporary = true },
        }, options);

        _ = new Kc.UserRoles($"user-roles-{role}", new Kc.UserRolesArgs
        {
            RealmId = realm.Id,
            UserId = user.Id,
            RoleIds = ToInputList([roles[role].Id]),
        }, options);
    }

    // ---- Outputs ----
    var confidential = new[] { postman, aggregator }.Select(c => (c.ClientId, c.Client.ClientSecret)).ToList();
    return new Dictionary<string, object?>
    {
        ["RealmUrl"] = $"{url}/realms/{PlatformModel.RealmName}",
        ["AdminConsoleUrl"] = $"{url}/admin/master/console/#/{PlatformModel.RealmName}",
        ["ClientIds"] = new[] { postman.ClientId, aggregator.ClientId }.Concat(allAudiences).ToArray(),
        ["ClientSecrets"] = Output.All(confidential.Select(c => (Input<string>)c.ClientSecret.Apply(s => $"{c.ClientId}={s}"))),
        ["TestUsers"] = PlatformModel.Roles.Select(r => $"test-{r}").ToArray(),
    };

    static InputList<string> ToInputList(IEnumerable<Output<string>> values)
    {
        var list = new InputList<string>();
        foreach (var v in values) list.Add(v);
        return list;
    }
});
