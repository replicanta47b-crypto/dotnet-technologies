using Auth.Api.Authorization;
using Auth.Api.Data;
using Auth.Api.Models;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Controllers;

[ApiController]
[Route("users")]
public class UsersController(
    UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, ApplicationDbContext db, TokenIssuer issuer) : ControllerBase
{
    /// <summary>Список користувачів (Admin).</summary>
    [HttpGet, Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<IEnumerable<UserDto>>> GetAll([FromQuery] int page = 1, [FromQuery] int pageSize = 50)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var list = await users.Users.OrderBy(u => u.UserName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var result = new List<UserDto>();
        foreach (var u in list) result.Add(await issuer.ToDtoAsync(u));
        return result;
    }

    /// <summary>Користувач за id — власник або Admin.</summary>
    [HttpGet("{id}"), Authorize(Policy = Policies.OwnerOrAdmin)]
    public async Task<ActionResult<UserDto>> Get(string id)
        => await users.FindByIdAsync(id) is { } u ? await issuer.ToDtoAsync(u) : NotFound();

    /// <summary>Оновлення профілю (ім'я, email, повне ім'я) — власник або Admin. Зміна email скидає підтвердження.</summary>
    [HttpPut("{id}"), Authorize(Policy = Policies.OwnerOrAdmin)]
    public async Task<IActionResult> Update(string id, UpdateUserRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();

        if (request.FullName is not null) user.FullName = request.FullName;
        if (request.UserName is not null && request.UserName != user.UserName)
        {
            var r = await users.SetUserNameAsync(user, request.UserName);
            if (!r.Succeeded) return IdentityProblem(r);
        }
        if (request.Email is not null && !string.Equals(request.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var r = await users.SetEmailAsync(user, request.Email);   // EmailConfirmed → false
            if (!r.Succeeded) return IdentityProblem(r);
        }
        var update = await users.UpdateAsync(user);
        return update.Succeeded ? Ok(await issuer.ToDtoAsync(user)) : IdentityProblem(update);
    }

    /// <summary>Видалення користувача (Admin). Його refresh-токени видаляються каскадно.</summary>
    [HttpDelete("{id}"), Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var result = await users.DeleteAsync(user);
        return result.Succeeded ? NoContent() : IdentityProblem(result);
    }

    /// <summary>Refresh-токени користувача (метадані, без значень) — власник або Admin.</summary>
    [HttpGet("{id}/refresh-tokens"), Authorize(Policy = Policies.OwnerOrAdmin)]
    public async Task<ActionResult<IEnumerable<RefreshTokenDto>>> RefreshTokens(string id)
    {
        if (await users.FindByIdAsync(id) is null) return NotFound();
        var list = await db.RefreshTokens.Where(t => t.UserId == id).OrderByDescending(t => t.CreatedAtUtc).ToListAsync();
        return list.Select(t => new RefreshTokenDto(t.Id, t.CreatedAtUtc, t.ExpiresAtUtc, t.CreatedByIp, t.IsActive, t.RevokedAtUtc, t.ReasonRevoked)).ToList();
    }

    /// <summary>Ролі користувача — будь-який автентифікований.</summary>
    [HttpGet("{id}/roles"), Authorize]
    public async Task<ActionResult<IEnumerable<string>>> GetRoles(string id)
        => await users.FindByIdAsync(id) is { } u ? Ok(await users.GetRolesAsync(u)) : NotFound();

    /// <summary>Призначити роль (Admin).</summary>
    [HttpPost("{id}/roles"), Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> AddRole(string id, RoleRequest request)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (!await roles.RoleExistsAsync(request.Role))
            return BadRequest(new ProblemDetails { Title = $"Роль '{request.Role}' не існує. Доступні: {string.Join(", ", DbSeeder.Roles)}." });

        var result = await users.AddToRoleAsync(user, request.Role);
        return result.Succeeded ? Ok(await users.GetRolesAsync(user)) : IdentityProblem(result);
    }

    /// <summary>Забрати роль (Admin).</summary>
    [HttpDelete("{id}/roles/{role}"), Authorize(Policy = Policies.Admin)]
    public async Task<IActionResult> RemoveRole(string id, string role)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var result = await users.RemoveFromRoleAsync(user, role);
        return result.Succeeded ? NoContent() : IdentityProblem(result);
    }

    private IActionResult IdentityProblem(IdentityResult result)
    {
        foreach (var e in result.Errors) ModelState.AddModelError(e.Code, e.Description);
        return ValidationProblem(ModelState);
    }
}
