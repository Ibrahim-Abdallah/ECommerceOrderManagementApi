using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderManagementApi.Services;

public sealed class OrderService(
    AppDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<CheckoutResult> CheckoutAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId)
            return new(CheckoutStatus.MissingUser);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var cart = await dbContext.Carts
            .Include(x => x.CartItems)
            .SingleOrDefaultAsync(x => x.UserId == userId, cancellationToken);

        if (cart is null || cart.CartItems.Count == 0)
            return new(CheckoutStatus.EmptyCart);

        var requestedItems = cart.CartItems
            .OrderBy(x => x.ProductId)
            .Select(x => new { x.ProductId, x.Quantity })
            .ToList();
        var productIds = requestedItems.Select(x => x.ProductId).ToList();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(x => productIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Name, x.Price, x.IsActive, CategoryIsActive = x.Category.IsActive })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        if (products.Count != requestedItems.Count ||
            requestedItems.Any(x => !products[x.ProductId].IsActive || !products[x.ProductId].CategoryIsActive))
            return new(CheckoutStatus.ProductUnavailable);

        foreach (var item in requestedItems)
        {
            var affectedRows = await dbContext.Products
                .Where(product =>
                    product.Id == item.ProductId &&
                    product.IsActive &&
                    product.StockQuantity >= item.Quantity &&
                    dbContext.Categories.Any(category =>
                        category.Id == product.CategoryId && category.IsActive))
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    product => product.StockQuantity,
                    product => product.StockQuantity - item.Quantity), cancellationToken);

            if (affectedRows != 1)
            {
                await transaction.RollbackAsync(cancellationToken);
                logger.LogInformation("Checkout stock conflict for customer {UserId} and product {ProductId}.", userId, item.ProductId);
                return new(CheckoutStatus.InsufficientStock);
            }
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var orderItems = requestedItems.Select(item =>
        {
            var product = products[item.ProductId];
            return new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = item.Quantity,
                TotalPrice = product.Price * item.Quantity,
                Order = null!,
                Product = null!
            };
        }).ToList();
        var order = new Order
        {
            UserId = userId,
            Status = OrderStatus.Pending,
            TotalAmount = orderItems.Sum(x => x.TotalPrice),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            User = null!,
            OrderItems = orderItems
        };

        dbContext.Orders.Add(order);
        dbContext.CartItems.RemoveRange(cart.CartItems);
        cart.UpdatedAtUtc = now;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(CheckoutStatus.CartChanged);
        }

        logger.LogInformation("Order {OrderId} created for customer {UserId}.", order.Id, userId);
        return new(CheckoutStatus.Success, new OrderDetailsResponse(
            order.Id,
            order.Status,
            order.TotalAmount,
            order.CreatedAtUtc,
            order.UpdatedAtUtc,
            order.OrderItems.Select(x => new OrderItemResponse(
                x.ProductId, x.ProductName, x.UnitPrice, x.Quantity, x.TotalPrice)).ToList()));
    }
}
