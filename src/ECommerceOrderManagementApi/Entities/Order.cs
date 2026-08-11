using ECommerceOrderManagementApi.Enums;

namespace ECommerceOrderManagementApi.Entities;

public sealed class Order
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public required User User { get; set; }
    public ICollection<OrderItem> OrderItems { get; set; } = [];
}
