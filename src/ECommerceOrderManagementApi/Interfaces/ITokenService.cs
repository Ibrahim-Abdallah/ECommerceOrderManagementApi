using ECommerceOrderManagementApi.Entities;

namespace ECommerceOrderManagementApi.Interfaces;

public interface ITokenService
{
    GeneratedAccessToken CreateAccessToken(User user);
    GeneratedRefreshToken CreateRefreshToken();
    string HashRefreshToken(string rawToken);
}

public sealed record GeneratedAccessToken(string Token, DateTime ExpiresAtUtc);
public sealed record GeneratedRefreshToken(string Token, string TokenHash, DateTime ExpiresAtUtc);
