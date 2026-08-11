using ECommerceOrderManagementApi.Enums;

namespace ECommerceOrderManagementApi.Entities;

public sealed class User
{
    public int Id { get; set; }
    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; } = UserRole.Customer;
    public DateTime CreatedAtUtc { get; set; }
    public Cart? Cart { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<Order> Orders { get; set; } = [];
}
