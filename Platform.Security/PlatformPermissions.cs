namespace Platform.Security;

/// <summary>
/// Єдине джерело правди про permissions платформи. З нього генеруються: політики авторизації в сервісах,
/// API scopes IdentityServer (payments.read ↔ payments:read) і — незалежно, у Pulumi-проєкті — scopes Keycloak.
///
/// Відповідність завданню (таблиці 6–10 Лаби 9) для нашої платіжної платформи:
///   orders-api   (Замовлення, Лаб. 02) → payments-api   (платежі):  read, create, update, delete, approve
///   products-api (Каталог, Лаб. 03)    → billing-api    (рахунки):  read, create, update, delete, publish
///   payments-api (нова заглушка)       → settlements-api (виплати): read, process, refund
///   disputes-api (Відгуки, Лаб. 04)    → disputes-api   (спори):    read, create, update, delete  (додатково)
/// </summary>
public static class PlatformPermissions
{
    public static class Payments
    {
        public const string Read = "payments:read";
        public const string Create = "payments:create";
        public const string Update = "payments:update";
        public const string Delete = "payments:delete";
        public const string Approve = "payments:approve";
    }

    public static class Billing
    {
        public const string Read = "billing:read";
        public const string Create = "billing:create";
        public const string Update = "billing:update";
        public const string Delete = "billing:delete";
        public const string Publish = "billing:publish";
    }

    public static class Disputes
    {
        public const string Read = "disputes:read";
        public const string Create = "disputes:create";
        public const string Update = "disputes:update";
        public const string Delete = "disputes:delete";
    }

    public static class Settlements
    {
        public const string Read = "settlements:read";
        public const string Process = "settlements:process";
        public const string Refund = "settlements:refund";
    }

    /// <summary>Resource server (сервіс) → усі його permissions.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ByService = new Dictionary<string, string[]>
    {
        ["payments"] = [Payments.Read, Payments.Create, Payments.Update, Payments.Delete, Payments.Approve],
        ["billing"] = [Billing.Read, Billing.Create, Billing.Update, Billing.Delete, Billing.Publish],
        ["disputes"] = [Disputes.Read, Disputes.Create, Disputes.Update, Disputes.Delete],
        ["settlements"] = [Settlements.Read, Settlements.Process, Settlements.Refund],
    };

    public static IEnumerable<string> All => ByService.Values.SelectMany(x => x);

    /// <summary>IdentityServer-форма: payments:read → payments.read (scope і політика [Authorize(Policy = "payments.read")]).</summary>
    public static string ToScope(string permission) => permission.Replace(':', '.');

    /// <summary>Нормалізація: і «payments.read», і «payments:read» → «payments:read».</summary>
    public static string Normalize(string value) => value.Trim().Replace('.', ':').ToLowerInvariant();
}

/// <summary>Ролі платформи (Keycloak: admin, manager, user, guest; Auth Service: User і Admin — без урахування регістру).</summary>
public static class PlatformRoles
{
    public const string Admin = "admin";
    public const string Manager = "manager";
    public const string User = "user";
    public const string Guest = "guest";

    /// <summary>
    /// Матриця «роль → permissions» (таблиця 7 завдання). У Keycloak вона описана декларативно в Pulumi (Permission Policies),
    /// тут — для токенів власного Auth Service, які містять лише ролі й не мають claim authorization.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> Matrix = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [Admin] = PlatformPermissions.All.ToArray(),
        [Manager] =
        [
            PlatformPermissions.Payments.Read, PlatformPermissions.Payments.Update, PlatformPermissions.Payments.Approve,
            PlatformPermissions.Billing.Read, PlatformPermissions.Billing.Update, PlatformPermissions.Billing.Publish,
            PlatformPermissions.Disputes.Read, PlatformPermissions.Disputes.Update,
        ],
        [User] =
        [
            PlatformPermissions.Payments.Read, PlatformPermissions.Payments.Create,
            PlatformPermissions.Billing.Read,
            PlatformPermissions.Disputes.Read, PlatformPermissions.Disputes.Create,
        ],
        [Guest] = [PlatformPermissions.Billing.Read],
    };
}
