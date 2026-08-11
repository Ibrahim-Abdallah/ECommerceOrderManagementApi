namespace ECommerceOrderManagementApi.Entities;

public sealed class CartItem
{
    public int Id { get; set; }
    public int CartId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public required Cart Cart { get; set; }
    public required Product Product { get; set; }
}
