using System.ComponentModel.DataAnnotations;

namespace Auth.Api.Models;

public sealed record RegisterRequest(
    [Required, StringLength(50, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$")] string UserName,
    [Required, EmailAddress] string Email,
    [Required] string Password,
    string? FullName);

public sealed record LoginRequest([Required] string UserNameOrEmail, [Required] string Password);

public sealed record RevokeTokenRequest(string? Token);
public sealed record EmailRequest([Required, EmailAddress] string Email);
public sealed record ResetPasswordRequest(
    [Required, EmailAddress] string Email, [Required] string Token, [Required] string NewPassword);
public sealed record ChangePasswordRequest([Required] string CurrentPassword, [Required] string NewPassword);

public sealed record AuthResponse(string AccessToken, string TokenType, int ExpiresIn, DateTime ExpiresAtUtc, UserDto User);

public sealed record UserDto(string Id, string UserName, string Email, bool EmailConfirmed, string? FullName, DateTime CreatedAtUtc, IReadOnlyList<string> Roles);

public sealed record UpdateUserRequest(
    [StringLength(50, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$")] string? UserName,
    [EmailAddress] string? Email,
    [StringLength(200)] string? FullName);

public sealed record RoleRequest([Required] string Role);

public sealed record RefreshTokenDto(Guid Id, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, string? CreatedByIp, bool IsActive, DateTime? RevokedAtUtc, string? ReasonRevoked);
