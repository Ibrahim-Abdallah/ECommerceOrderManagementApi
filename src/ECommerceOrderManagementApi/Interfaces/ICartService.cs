using ECommerceOrderManagementApi.DTOs.Cart;

namespace ECommerceOrderManagementApi.Interfaces;

public enum CartOperationStatus
{
    Success,
    MissingUser,
    NotFound,
    ProductUnavailable,
    DuplicateProduct,
    InsufficientStock
}

public sealed record CartOperationResult(CartOperationStatus Status, CartResponse? Cart = null);

public interface ICartService
{
    Task<CartOperationResult> GetAsync(CancellationToken cancellationToken);
    Task<CartOperationResult> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken);
    Task<CartOperationResult> UpdateItemAsync(int productId, UpdateCartItemRequest request, CancellationToken cancellationToken);
    Task<CartOperationStatus> RemoveItemAsync(int productId, CancellationToken cancellationToken);
    Task<CartOperationStatus> ClearAsync(CancellationToken cancellationToken);
}
