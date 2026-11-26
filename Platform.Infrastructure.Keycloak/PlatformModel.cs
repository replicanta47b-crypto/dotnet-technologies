namespace DevRealmDrivers.Iac;

/// <summary>Resource server = мікросервіс. Scopes — permissions (таблиці 6–7 завдання, адаптовані до домену платформи).</summary>
public sealed record ResourceServerSpec(string ClientId, string ResourceName, string Uri, string[] Scopes);

/// <summary>
/// Декларативна модель авторизації платформи. Відповідність таблицям завдання:
///   orders-api → payments-api (Лаб. 02), products-api → billing-api (Лаб. 03), payments-api → settlements-api (нова заглушка),
///   disputes-api → disputes-api (Лаб. 04, додатково). Матриця ролей збігається з Platform.Security.PlatformRoles.Matrix
///   (її використовують сервіси для токенів власного Auth Service) — розбіжність виявить перевірка матриці відповідей.
/// </summary>
public static class PlatformModel
{
    public const string RealmName = "DevRealmDrivers";

    public static readonly ResourceServerSpec[] Servers =
    [
        new("payments-api", "payments", "/api/payments/*", ["payments:read", "payments:create", "payments:update", "payments:delete", "payments:approve"]),
        new("billing-api", "billing", "/api/billing/*", ["billing:read", "billing:create", "billing:update", "billing:delete", "billing:publish"]),
        new("disputes-api", "disputes", "/api/disputes/*", ["disputes:read", "disputes:create", "disputes:update", "disputes:delete"]),
        new("settlements-api", "settlements", "/api/settlements/*", ["settlements:read", "settlements:process", "settlements:refund"]),
    ];

    public static readonly string[] Roles = ["admin", "manager", "user", "guest"];

    /// <summary>Роль → дозволені scopes (permission policies: для кожного scope створюється permission, яку надають ці ролі).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolePermissions = new Dictionary<string, string[]>
    {
        ["admin"] = Servers.SelectMany(s => s.Scopes).ToArray(),
        ["manager"] =
        [
            "payments:read", "payments:update", "payments:approve",
            "billing:read", "billing:update", "billing:publish",
            "disputes:read", "disputes:update"
        ],
        ["user"] = ["payments:read", "payments:create", "billing:read", "disputes:read", "disputes:create"],
        ["guest"] = ["billing:read"],
    };
}
