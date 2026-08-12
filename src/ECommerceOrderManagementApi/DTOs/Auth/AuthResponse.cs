namespace ECommerceOrderManagementApi.DTOs.Auth;

public sealed record AuthResponse(string AccessToken, string RefreshToken, DateTime ExpiresAtUtc);
