using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ECommerceOrderManagementApi.Configuration;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ECommerceOrderManagementApi.Services;

public sealed class TokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public GeneratedAccessToken CreateAccessToken(User user)
    {
        var issuedAt = timeProvider.GetUtcNow().UtcDateTime;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenExpirationMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            notBefore: issuedAt,
            expires: expiresAt,
            signingCredentials: credentials);

        return new GeneratedAccessToken(new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public GeneratedRefreshToken CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(64));
        var expiresAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenExpirationDays);
        return new GeneratedRefreshToken(token, HashRefreshToken(token), expiresAt);
    }

    public string HashRefreshToken(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
}
