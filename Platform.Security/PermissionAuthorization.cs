using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Platform.Security;

/// <summary>Вимога «у токені є permission X».</summary>
public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = PlatformPermissions.Normalize(permission);
}

/// <summary>
/// Рішення «пропустити чи відхилити» приймає ЦЕЙ сервіс локально, на рівні конкретного endpoint: IdP лише видав токен.
/// Автентифікований користувач без потрібного permission отримує 403 (а не 500 і не 401).
/// </summary>
public sealed class PermissionAuthorizationHandler(IOptions<PlatformAuthOptions> options)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (PermissionClaims.GetPermissions(context.User, options.Value).Contains(requirement.Permission))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

/// <summary>
/// [RequirePermission("payments:update")] — атрибут для КОЖНОГО endpoint. Політика з таким ім'ям створюється в AddPlatformAuthorization.
/// Те саме працює для gRPC-сервісів (клас або метод).
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission) : base(permission) => Permission = permission;

    public string Permission { get; }
}
