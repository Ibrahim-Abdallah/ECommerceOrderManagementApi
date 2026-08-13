using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs;
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
    public async Task<PagedResponse<OrderSummaryResponse>?> GetCustomerOrdersAsync(CustomerOrderQueryParameters query, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return null;
        var orders = dbContext.Orders.AsNoTracking().Where(x => x.UserId == userId);
        orders = ApplyFilters(orders, query.Status, query.FromDate, query.ToDate);
        var totalCount = await orders.CountAsync(cancellationToken);
        var items = await orders.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new OrderSummaryResponse(x.Id, x.Status, x.TotalAmount, x.CreatedAtUtc,
                x.UpdatedAtUtc, x.OrderItems.Sum(item => item.Quantity))).ToListAsync(cancellationToken);
        return new(items, query.PageNumber, query.PageSize, totalCount,
            (int)Math.Ceiling(totalCount / (double)query.PageSize));
    }

    public async Task<OrderDetailsResponse?> GetCustomerOrderAsync(int id, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return null;
        return await CustomerDetailsQuery(userId, id).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<CustomerOrderMutationResult> CancelAsync(int id, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not int userId) return new(OrderMutationStatus.MissingUser);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var exists = await dbContext.Orders.AsNoTracking().AnyAsync(x => x.Id == id && x.UserId == userId, cancellationToken);
        if (!exists) return new(OrderMutationStatus.NotFound);

        var quantities = await dbContext.OrderItems.AsNoTracking().Where(x => x.OrderId == id)
            .GroupBy(x => x.ProductId).Select(x => new { ProductId = x.Key, Quantity = x.Sum(i => i.Quantity) })
            .OrderBy(x => x.ProductId).ToListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var affected = await dbContext.Orders
            .Where(x => x.Id == id && x.UserId == userId && x.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OrderStatus.Cancelled)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        if (affected != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(OrderMutationStatus.Conflict);
        }

        foreach (var item in quantities)
        {
            var restored = await dbContext.Products.Where(x => x.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, x => x.StockQuantity + item.Quantity), cancellationToken);
            if (restored != 1) throw new InvalidOperationException($"Order {id} references missing product {item.ProductId}.");
        }
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Order {OrderId} cancelled by customer {UserId}.", id, userId);
        return new(OrderMutationStatus.Success, await CustomerDetailsQuery(userId, id).SingleAsync(cancellationToken));
    }

    public async Task<PagedResponse<AdminOrderSummaryResponse>> GetAdminOrdersAsync(AdminOrderQueryParameters query, CancellationToken cancellationToken)
    {
        var orders = ApplyFilters(dbContext.Orders.AsNoTracking(), query.Status, query.FromDate, query.ToDate);
        var email = query.CustomerEmail?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(email)) orders = orders.Where(x => x.User.NormalizedEmail.Contains(email));
        var totalCount = await orders.CountAsync(cancellationToken);
        var items = await orders.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new AdminOrderSummaryResponse(x.Id, x.UserId, x.User.Email, x.Status, x.TotalAmount,
                x.OrderItems.Sum(item => item.Quantity), x.CreatedAtUtc, x.UpdatedAtUtc)).ToListAsync(cancellationToken);
        return new(items, query.PageNumber, query.PageSize, totalCount,
            (int)Math.Ceiling(totalCount / (double)query.PageSize));
    }

    public Task<AdminOrderDetailsResponse?> GetAdminOrderAsync(int id, CancellationToken cancellationToken) =>
        AdminDetailsQuery(id).SingleOrDefaultAsync(cancellationToken);

    public async Task<AdminOrderMutationResult> UpdateStatusAsync(int id, OrderStatus requestedStatus, CancellationToken cancellationToken)
    {
        var current = await dbContext.Orders.AsNoTracking().Where(x => x.Id == id)
            .Select(x => (OrderStatus?)x.Status).SingleOrDefaultAsync(cancellationToken);
        if (!current.HasValue) return new(OrderMutationStatus.NotFound);
        if (!OrderStatusTransitionRules.CanAdminTransition(current.Value, requestedStatus))
            return new(OrderMutationStatus.Conflict);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var affected = await dbContext.Orders.Where(x => x.Id == id && x.Status == current.Value)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, requestedStatus)
                .SetProperty(x => x.UpdatedAtUtc, now), cancellationToken);
        if (affected != 1) return new(OrderMutationStatus.ConcurrentChange);
        logger.LogInformation("Order {OrderId} transitioned from {CurrentStatus} to {RequestedStatus}.", id, current, requestedStatus);
        return new(OrderMutationStatus.Success, await AdminDetailsQuery(id).SingleAsync(cancellationToken));
    }

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

    private static IQueryable<Order> ApplyFilters(IQueryable<Order> orders, OrderStatus? status, DateTime? from, DateTime? to)
    {
        if (status.HasValue) orders = orders.Where(x => x.Status == status);
        if (from.HasValue) orders = orders.Where(x => x.CreatedAtUtc >= from);
        if (to.HasValue) orders = orders.Where(x => x.CreatedAtUtc <= to);
        return orders;
    }

    private IQueryable<OrderDetailsResponse> CustomerDetailsQuery(int userId, int id) => dbContext.Orders.AsNoTracking()
        .Where(x => x.Id == id && x.UserId == userId)
        .Select(x => new OrderDetailsResponse(x.Id, x.Status, x.TotalAmount, x.CreatedAtUtc, x.UpdatedAtUtc,
            x.OrderItems.OrderBy(item => item.Id).Select(item => new OrderItemResponse(item.ProductId,
                item.ProductName, item.UnitPrice, item.Quantity, item.TotalPrice)).ToList()));

    private IQueryable<AdminOrderDetailsResponse> AdminDetailsQuery(int id) => dbContext.Orders.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => new AdminOrderDetailsResponse(x.Id, x.UserId, x.User.Email, x.Status, x.TotalAmount,
            x.CreatedAtUtc, x.UpdatedAtUtc, x.OrderItems.OrderBy(item => item.Id).Select(item =>
                new OrderItemResponse(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.TotalPrice)).ToList()));
}
