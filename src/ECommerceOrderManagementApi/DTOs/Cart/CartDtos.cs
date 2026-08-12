namespace ECommerceOrderManagementApi.DTOs.Cart;

public sealed record AddCartItemRequest(int ProductId, int Quantity);

public sealed record UpdateCartItemRequest(int Quantity);

public sealed record CartItemResponse(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record CartResponse(
    int Id,
    IReadOnlyList<CartItemResponse> Items,
    int TotalQuantity,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
