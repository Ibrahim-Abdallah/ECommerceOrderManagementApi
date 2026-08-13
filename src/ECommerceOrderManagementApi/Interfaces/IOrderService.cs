using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.DTOs;

namespace ECommerceOrderManagementApi.Interfaces;

public enum CheckoutStatus
{
    Success,
    MissingUser,
    EmptyCart,
    ProductUnavailable,
    InsufficientStock,
    CartChanged
}

public sealed record CheckoutResult(CheckoutStatus Status, OrderDetailsResponse? Order = null);

public enum OrderMutationStatus { Success, MissingUser, NotFound, Conflict, ConcurrentChange }
public sealed record CustomerOrderMutationResult(OrderMutationStatus Status, OrderDetailsResponse? Order = null);
public sealed record AdminOrderMutationResult(OrderMutationStatus Status, AdminOrderDetailsResponse? Order = null);

public interface IOrderService
{
    Task<CheckoutResult> CheckoutAsync(CancellationToken cancellationToken);
    Task<PagedResponse<OrderSummaryResponse>?> GetCustomerOrdersAsync(CustomerOrderQueryParameters query, CancellationToken cancellationToken);
    Task<OrderDetailsResponse?> GetCustomerOrderAsync(int id, CancellationToken cancellationToken);
    Task<CustomerOrderMutationResult> CancelAsync(int id, CancellationToken cancellationToken);
    Task<PagedResponse<AdminOrderSummaryResponse>> GetAdminOrdersAsync(AdminOrderQueryParameters query, CancellationToken cancellationToken);
    Task<AdminOrderDetailsResponse?> GetAdminOrderAsync(int id, CancellationToken cancellationToken);
    Task<AdminOrderMutationResult> UpdateStatusAsync(int id, ECommerceOrderManagementApi.Enums.OrderStatus requestedStatus, CancellationToken cancellationToken);
}
