using System.ComponentModel.DataAnnotations;

namespace Auth.Api.Models;

public sealed record RegisterRequest(
    [property: Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$")] string UserName,
    [property: Required, EmailAddress] string Email,
    [property: Required] string Password,
    string? FullName);

public sealed record LoginRequest([property: Required] string UserNameOrEmail, [property: Required] string Password);

public sealed record RevokeTokenRequest(string? Token);
public sealed record EmailRequest([property: Required, EmailAddress] string Email);
public sealed record ResetPasswordRequest(
    [property: Required, EmailAddress] string Email, [property: Required] string Token, [property: Required] string NewPassword);
public sealed record ChangePasswordRequest([property: Required] string CurrentPassword, [property: Required] string NewPassword);

public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, DateTime ExpiresAtUtc, UserDto User);

public sealed record UserDto(string Id, string UserName, string Email, bool EmailConfirmed, string? FullName, DateTime CreatedAtUtc, IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(
    [property: StringLength(50, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$")] string? UserName,
    [property: EmailAddress] string? Email,
    [property: StringLength(200)] string? FullName);

public sealed record RoleRequest([property: Required] string Role);

public sealed record RefreshTokenDto(Guid Id, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, string? CreatedByIp, bool IsActive, DateTime? RevokedAtUtc, string? ReasonRevoked);
