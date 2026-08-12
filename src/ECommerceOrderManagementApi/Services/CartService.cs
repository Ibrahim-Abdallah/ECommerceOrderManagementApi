using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Cart;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderManagementApi.Services;

public sealed class CartService(AppDbContext dbContext, ICurrentUserService currentUser, TimeProvider timeProvider) : ICartService
{
    public async Task<CartOperationResult> GetAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return new(CartOperationStatus.MissingUser);
        var cart = await GetOrCreateCartAsync(userId, cancellationToken);
        return new(CartOperationStatus.Success, await ProjectCartAsync(cart.Id, userId, cancellationToken));
    }

    public async Task<CartOperationResult> AddItemAsync(AddCartItemRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return new(CartOperationStatus.MissingUser);
        var product = await GetAvailableProductAsync(request.ProductId, cancellationToken);
        if (product is null) return new(CartOperationStatus.ProductUnavailable);
        if (request.Quantity > product.StockQuantity) return new(CartOperationStatus.InsufficientStock);

        var cart = await GetOrCreateCartAsync(userId, cancellationToken);
        if (await dbContext.CartItems.AnyAsync(x => x.CartId == cart.Id && x.ProductId == request.ProductId, cancellationToken))
            return new(CartOperationStatus.DuplicateProduct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.CartItems.Add(new CartItem
        {
            CartId = cart.Id, ProductId = request.ProductId, Quantity = request.Quantity,
            Cart = cart, Product = null!, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        cart.UpdatedAtUtc = now;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            dbContext.ChangeTracker.Clear();
            if (await dbContext.CartItems.AnyAsync(x => x.CartId == cart.Id && x.ProductId == request.ProductId, cancellationToken))
                return new(CartOperationStatus.DuplicateProduct);
            throw;
        }
        return new(CartOperationStatus.Success, await ProjectCartAsync(cart.Id, userId, cancellationToken));
    }

    public async Task<CartOperationResult> UpdateItemAsync(int productId, UpdateCartItemRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return new(CartOperationStatus.MissingUser);
        var item = await dbContext.CartItems.Include(x => x.Cart)
            .SingleOrDefaultAsync(x => x.ProductId == productId && x.Cart.UserId == userId, cancellationToken);
        if (item is null) return new(CartOperationStatus.NotFound);
        var product = await GetAvailableProductAsync(productId, cancellationToken);
        if (product is null) return new(CartOperationStatus.ProductUnavailable);
        if (request.Quantity > product.StockQuantity) return new(CartOperationStatus.InsufficientStock);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        item.Quantity = request.Quantity;
        item.UpdatedAtUtc = now;
        item.Cart.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(CartOperationStatus.Success, await ProjectCartAsync(item.CartId, userId, cancellationToken));
    }

    public async Task<CartOperationStatus> RemoveItemAsync(int productId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return CartOperationStatus.MissingUser;
        var item = await dbContext.CartItems.Include(x => x.Cart)
            .SingleOrDefaultAsync(x => x.ProductId == productId && x.Cart.UserId == userId, cancellationToken);
        if (item is null) return CartOperationStatus.NotFound;
        item.Cart.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.CartItems.Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartOperationStatus.Success;
    }

    public async Task<CartOperationStatus> ClearAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return CartOperationStatus.MissingUser;
        var cart = await dbContext.Carts.Include(x => x.CartItems)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (cart is null || cart.CartItems.Count == 0) return CartOperationStatus.Success;
        dbContext.CartItems.RemoveRange(cart.CartItems);
        cart.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CartOperationStatus.Success;
    }

    private async Task<Cart> GetOrCreateCartAsync(int userId, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Carts.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (existing is not null) return existing;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var cart = new Cart { UserId = userId, User = null!, CreatedAtUtc = now, UpdatedAtUtc = now };
        dbContext.Carts.Add(cart);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return cart;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            dbContext.ChangeTracker.Clear();
            var winner = await dbContext.Carts.SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);
            if (winner is not null) return winner;
            throw;
        }
    }

    private Task<Product?> GetAvailableProductAsync(int productId, CancellationToken cancellationToken) =>
        dbContext.Products.Include(x => x.Category)
            .SingleOrDefaultAsync(x => x.Id == productId && x.IsActive && x.Category.IsActive, cancellationToken);

    private async Task<CartResponse> ProjectCartAsync(int cartId, int userId, CancellationToken cancellationToken)
    {
        var cart = await dbContext.Carts.AsNoTracking().Where(x => x.Id == cartId && x.UserId == userId)
            .Select(x => new
            {
                x.Id, x.CreatedAtUtc, x.UpdatedAtUtc,
                Items = x.CartItems.OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id)
                    .Select(i => new CartItemResponse(i.ProductId, i.Product.Name, i.Product.Price, i.Quantity,
                        i.Product.Price * i.Quantity)).ToList()
            }).SingleAsync(cancellationToken);
        return new(cart.Id, cart.Items, cart.Items.Sum(x => x.Quantity), cart.Items.Sum(x => x.LineTotal),
            cart.CreatedAtUtc, cart.UpdatedAtUtc);
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };
}
