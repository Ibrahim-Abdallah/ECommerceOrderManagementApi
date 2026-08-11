using ECommerceOrderManagementApi.Enums;

namespace ECommerceOrderManagementApi.DTOs.Auth;

public sealed record RegisterResponse(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    UserRole Role,
    DateTime CreatedAtUtc);
