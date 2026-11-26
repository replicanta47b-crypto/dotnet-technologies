using System.Security.Claims;
using System.Text;
using Auth.Api.Data;
using Auth.Api.Models;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Auth.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController(
    UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, ApplicationDbContext db,
    TokenIssuer issuer, IJwtTokenService jwt, IEmailService email, IWebHostEnvironment env) : ControllerBase
{
    /// <summary>Реєстрація: створює користувача (роль User, email НЕ підтверджений) і «надсилає» лист із посиланням підтвердження.</summary>
    [HttpPost("register"), AllowAnonymous]
    [ProducesResponseType<UserDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser { UserName = request.UserName, Email = request.Email, FullName = request.FullName };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityProblem(result);

        await users.AddToRoleAsync(user, "User");
        await SendVerificationAsync(user);
        return Created($"/users/{user.Id}", await issuer.ToDtoAsync(user));
    }

    /// <summary>Вхід. Пароль невірний → 401; email не підтверджений → 403; 5 невдалих спроб → lockout (423).</summary>
    [HttpPost("login"), AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByNameAsync(request.UserNameOrEmail) ?? await users.FindByEmailAsync(request.UserNameOrEmail);
        if (user is null) return Problem("Невірний логін або пароль.", statusCode: StatusCodes.Status401Unauthorized);

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return Problem("Обліковий запис заблоковано після 5 невдалих спроб. Спробуйте пізніше.", statusCode: StatusCodes.Status423Locked);
        if (!result.Succeeded) return Problem("Невірний логін або пароль.", statusCode: StatusCodes.Status401Unauthorized);
        if (!user.EmailConfirmed)
            return Problem("Email не підтверджено. Перейдіть за посиланням з листа або запросіть новий (POST /auth/resend-verification).",
                statusCode: StatusCodes.Status403Forbidden);

        return Ok(await issuer.IssueAsync(user, HttpContext));
    }

    /// <summary>
    /// Оновлення пари токенів за refresh-токеном із HttpOnly cookie. Rotation: старий відкликається.
    /// Повторне використання вже відкликаного токена = ознака крадіжки → відкликаються ВСІ токени користувача.
    /// </summary>
    [HttpPost("refresh-token"), AllowAnonymous]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Refresh()
    {
        var raw = Request.Cookies[TokenIssuer.CookieName];
        if (string.IsNullOrEmpty(raw)) return Problem("Refresh-токен відсутній (cookie).", statusCode: StatusCodes.Status401Unauthorized);

        var token = await db.RefreshTokens.Include(t => t.User).FirstOrDefaultAsync(t => t.TokenHash == jwt.Hash(raw));
        if (token is null) return Unauthorized401("Невідомий refresh-токен.");

        if (token.IsRevoked)
        {
            await issuer.RevokeAllAsync(token.UserId, HttpContext, "Reuse of revoked refresh token detected");
            TokenIssuer.DeleteCookie(HttpContext);
            return Unauthorized401("Refresh-токен уже використано або відкликано. Усі сесії користувача завершено.");
        }
        if (token.IsExpired) return Unauthorized401("Refresh-токен прострочено.");
        if (await users.IsLockedOutAsync(token.User)) return Problem("Обліковий запис заблоковано.", statusCode: StatusCodes.Status423Locked);

        return Ok(await issuer.IssueAsync(token.User, HttpContext, replacing: token));
    }

    /// <summary>Відкликання refresh-токена (з тіла або cookie). Чужий токен може відкликати лише Admin.</summary>
    [HttpPost("revoke-token"), Authorize]
    public async Task<IActionResult> RevokeToken(RevokeTokenRequest? request)
    {
        var raw = request?.Token ?? Request.Cookies[TokenIssuer.CookieName];
        if (string.IsNullOrEmpty(raw)) return BadRequest(new ProblemDetails { Title = "Токен не вказано (тіло або cookie)." });

        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == jwt.Hash(raw));
        if (token is null) return NotFound(new ProblemDetails { Title = "Токен не знайдено." });
        if (token.UserId != User.FindFirstValue("sub") && !User.IsInRole("Admin")) return Forbid();

        if (!token.IsRevoked)
        {
            token.RevokedAtUtc = DateTime.UtcNow;
            token.RevokedByIp = TokenIssuer.Ip(HttpContext);
            token.ReasonRevoked = "Revoked by request";
            await db.SaveChangesAsync();
        }
        return NoContent();
    }

    /// <summary>Вихід: відкликає refresh-токен із cookie і видаляє cookie. Access-токен діє до закінчення (15 хв).</summary>
    [HttpPost("logout"), AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        var raw = Request.Cookies[TokenIssuer.CookieName];
        if (!string.IsNullOrEmpty(raw))
        {
            var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == jwt.Hash(raw));
            if (token is { IsRevoked: false })
            {
                token.RevokedAtUtc = DateTime.UtcNow;
                token.RevokedByIp = TokenIssuer.Ip(HttpContext);
                token.ReasonRevoked = "Logout";
                await db.SaveChangesAsync();
            }
        }
        TokenIssuer.DeleteCookie(HttpContext);
        return NoContent();
    }

    /// <summary>Підтвердження email: посилання з листа (userId + token у base64url).</summary>
    [HttpGet("verify-email"), AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromQuery] string userId, [FromQuery] string token)
    {
        var user = await users.FindByIdAsync(userId);
        if (user is null) return BadRequest(new ProblemDetails { Title = "Недійсне посилання підтвердження." });

        var result = await users.ConfirmEmailAsync(user, Decode(token));
        return result.Succeeded ? Ok(new { message = "Email підтверджено. Тепер можна увійти." }) : IdentityProblem(result);
    }

    /// <summary>Повторний лист підтвердження. Завжди 200 — не розкриваємо, чи існує такий email.</summary>
    [HttpPost("resend-verification"), AllowAnonymous]
    public async Task<IActionResult> ResendVerification(EmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is { EmailConfirmed: false }) await SendVerificationAsync(user);
        return Ok(new { message = "Якщо обліковий запис існує і не підтверджений, лист надіслано." });
    }

    /// <summary>Запит на скидання пароля. Завжди 200 (без розкриття існування облікового запису).</summary>
    [HttpPost("forgot-password"), AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(EmailRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var token = Encode(await users.GeneratePasswordResetTokenAsync(user));
            await email.SendAsync(user.Email!, "Скидання пароля",
                $"Для скидання пароля надішліть POST /auth/reset-password: {{ \"email\": \"{user.Email}\", \"token\": \"{token}\", \"newPassword\": \"...\" }}");
        }
        return Ok(new { message = "Якщо обліковий запис існує, інструкцію зі скидання надіслано." });
    }

    /// <summary>Скидання пароля за токеном з листа. Усі refresh-токени користувача відкликаються (усі сесії завершено).</summary>
    [HttpPost("reset-password"), AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email);
        if (user is null) return BadRequest(new ProblemDetails { Title = "Недійсний токен скидання пароля." });

        IdentityResult result;
        try { result = await users.ResetPasswordAsync(user, Decode(request.Token), request.NewPassword); }
        catch (FormatException) { return BadRequest(new ProblemDetails { Title = "Недійсний токен скидання пароля." }); }
        if (!result.Succeeded) return IdentityProblem(result);

        await users.ResetAccessFailedCountAsync(user);
        await issuer.RevokeAllAsync(user.Id, HttpContext, "Password reset");
        return Ok(new { message = "Пароль змінено. Усі сесії завершено — увійдіть знову." });
    }

    /// <summary>Зміна пароля автентифікованим користувачем; усі refresh-токени відкликаються.</summary>
    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        var user = await users.FindByIdAsync(User.FindFirstValue("sub")!);
        if (user is null) return Unauthorized();

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded) return IdentityProblem(result);

        await issuer.RevokeAllAsync(user.Id, HttpContext, "Password changed");
        TokenIssuer.DeleteCookie(HttpContext);
        return Ok(new { message = "Пароль змінено. Усі сесії завершено — увійдіть знову." });
    }

    /// <summary>Лише Development: «вхідні» mock-листи (userId і token для verify-email / reset-password).</summary>
    [HttpGet("dev/outbox"), AllowAnonymous]
    public IActionResult Outbox() => env.IsDevelopment() ? Ok(MockEmailService.Sent) : NotFound();

    // ---- допоміжне ----
    private async Task SendVerificationAsync(ApplicationUser user)
    {
        var token = Encode(await users.GenerateEmailConfirmationTokenAsync(user));
        var link = $"{Request.Scheme}://{Request.Host}/auth/verify-email?userId={user.Id}&token={token}";
        await email.SendAsync(user.Email!, "Підтвердіть email", $"Вітаємо, {user.UserName}! Підтвердіть email: {link}");
    }

    private static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    private static string Decode(string token) => Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(token));

    private ObjectResult Unauthorized401(string detail) => Problem(detail, statusCode: StatusCodes.Status401Unauthorized);

    private IActionResult IdentityProblem(IdentityResult result)
    {
        foreach (var e in result.Errors) ModelState.AddModelError(e.Code, e.Description);
        return ValidationProblem(ModelState);
    }
}
