using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Platform.Security;

namespace Auth.Api.Authorization;

public static class Policies
{
    public const string Admin = "Admin";
    public const string OwnerOrAdmin = "OwnerOrAdmin";
}

public sealed class OwnerOrAdminRequirement : IAuthorizationRequirement;

/// <summary>
/// Доступ до /users/{id}: власник (sub == {id} з маршруту) або Admin. Admin-роль враховується ЛИШЕ в токені,
/// виданому цим Auth Service (issuer) — «role=Admin» у чужому токені прав не дає.
/// </summary>
public sealed class OwnerOrAdminHandler(IHttpContextAccessor accessor, IOptions<PlatformAuthOptions> options)
    : AuthorizationHandler<OwnerOrAdminRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OwnerOrAdminRequirement requirement)
    {
        var user = context.User;
        if (!IsOwnToken(user, options.Value)) return Task.CompletedTask;

        if (user.IsInRole("Admin")) { context.Succeed(requirement); return Task.CompletedTask; }

        var routeId = accessor.HttpContext?.GetRouteValue("id")?.ToString();
        var sub = user.FindFirstValue("sub");
        if (routeId is not null && sub is not null && string.Equals(routeId, sub, StringComparison.Ordinal))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    public static bool IsOwnToken(ClaimsPrincipal user, PlatformAuthOptions o)
        => user.Identity?.IsAuthenticated == true && PermissionClaims.IsFromAuthService(user, o);
}
