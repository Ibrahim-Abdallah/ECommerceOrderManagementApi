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

public sealed record OrderSummaryResponse(int Id, OrderStatus Status, decimal TotalAmount,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc, int TotalItems);

public sealed record AdminOrderSummaryResponse(int Id, int UserId, string CustomerEmail,
    OrderStatus Status, decimal TotalAmount, int TotalItems, DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed record AdminOrderDetailsResponse(int Id, int UserId, string CustomerEmail,
    OrderStatus Status, decimal TotalAmount, DateTime CreatedAtUtc, DateTime UpdatedAtUtc,
    IReadOnlyList<OrderItemResponse> Items);

public sealed record UpdateOrderStatusRequest(OrderStatus Status);

public sealed class CustomerOrderQueryParameters
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public OrderStatus? Status { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}

public sealed class AdminOrderQueryParameters
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public OrderStatus? Status { get; init; }
    public string? CustomerEmail { get; init; }
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}
