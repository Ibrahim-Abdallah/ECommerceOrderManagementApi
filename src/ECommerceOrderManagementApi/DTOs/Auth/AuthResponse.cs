namespace ECommerceOrderManagementApi.DTOs.Auth;

public sealed record AuthResponse(string AccessToken, DateTime ExpiresAtUtc);
