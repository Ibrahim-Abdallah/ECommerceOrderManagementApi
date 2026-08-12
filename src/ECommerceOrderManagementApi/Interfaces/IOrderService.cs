using ECommerceOrderManagementApi.DTOs.Orders;

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

public interface IOrderService
{
    Task<CheckoutResult> CheckoutAsync(CancellationToken cancellationToken);
}
