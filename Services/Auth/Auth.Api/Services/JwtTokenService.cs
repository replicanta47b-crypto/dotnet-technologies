using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Auth.Api.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Platform.Security;

namespace Auth.Api.Services;

public sealed class JwtTokenService(IOptions<PlatformAuthOptions> options) : IJwtTokenService
{
    private readonly PlatformAuthOptions.AuthServiceOptions _o = options.Value.AuthService;
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };

    private SymmetricSecurityKey Key => new(Encoding.UTF8.GetBytes(_o.SecretKey));

    public AccessToken GenerateAccessToken(ApplicationUser user, IEnumerable<string> roles)
    {
        var expires = DateTime.UtcNow.AddMinutes(_o.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new("userId", user.Id),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName!),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        claims.AddRange(roles.Select(r => new Claim("role", r)));

        var token = new JwtSecurityToken(
            issuer: _o.Issuer, audience: _o.Audience, claims: claims,
            notBefore: DateTime.UtcNow, expires: expires,
            signingCredentials: new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return new AccessToken(_handler.WriteToken(token), expires);
    }

    public string GenerateRefreshToken() => Base64Url(RandomNumberGenerator.GetBytes(64));

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            return _handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = _o.Issuer,
                ValidateAudience = true, ValidAudience = _o.Audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true, IssuerSigningKey = Key,
                ClockSkew = TimeSpan.FromSeconds(30),
                NameClaimType = "unique_name", RoleClaimType = "role"
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }
    }

    public string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] bytes) => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes);
}
