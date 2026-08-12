using ECommerceOrderManagementApi.Enums;

namespace ECommerceOrderManagementApi.DTOs.Orders;

public sealed record OrderItemResponse(
    int ProductId,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal TotalPrice);

public sealed record OrderDetailsResponse(
    int Id,
    OrderStatus Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<OrderItemResponse> Items);
