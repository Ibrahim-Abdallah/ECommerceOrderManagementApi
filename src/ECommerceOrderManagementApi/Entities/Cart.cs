namespace ECommerceOrderManagementApi.Entities;

public sealed class Cart
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public required User User { get; set; }
    public ICollection<CartItem> CartItems { get; set; } = [];
}
