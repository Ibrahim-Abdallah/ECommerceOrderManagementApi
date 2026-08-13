using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs;
using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.DTOs.Cart;
using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Orders;

public sealed class OrderCheckoutApiTests(RelationalOrderApiFactory factory) : IClassFixture<RelationalOrderApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        await WithDb(async db =>
        {
            await db.OrderItems.ExecuteDeleteAsync();
            await db.Orders.ExecuteDeleteAsync();
            await db.CartItems.ExecuteDeleteAsync();
            await db.Carts.ExecuteDeleteAsync();
            await db.RefreshTokens.ExecuteDeleteAsync();
            await db.Products.ExecuteDeleteAsync();
            await db.Categories.ExecuteDeleteAsync();
            await db.Users.ExecuteDeleteAsync();
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task CheckoutRequiresCustomerAndRejectsEmptyCartWithoutCreatingOne()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/orders", null)).StatusCode);
        using var admin = await AuthenticatedClient(UserRole.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsync("/api/orders", null)).StatusCode);
        using var customer = await AuthenticatedClient(UserRole.Customer);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsync("/api/orders", null)).StatusCode);
        await WithDb(async db => Assert.Empty(await db.Carts.ToListAsync()));
    }

    [Fact]
    public async Task SuccessfulCheckoutCreatesSnapshotsCalculatesTotalsDecrementsStockAndPreservesCart()
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var a = await Product("Keyboard", 10m, 5);
            var b = await Product("Mouse", 15.50m, 10);
            await Add(customer, a, 2);
            await Add(customer, b, 3);
            Cart before = null!;
            await WithDb(async db => before = await db.Carts.AsNoTracking().SingleAsync(x => x.UserId == userId));

            var response = await customer.PostAsync("/api/orders", null);
            var body = await response.Content.ReadFromJsonAsync<OrderDetailsResponse>();

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(OrderStatus.Pending, body!.Status);
            Assert.Equal(66.50m, body.TotalAmount);
            Assert.Equal([20m, 46.50m], body.Items.OrderBy(x => x.ProductId).Select(x => x.TotalPrice));
            await WithDb(async db =>
            {
                var order = await db.Orders.Include(x => x.OrderItems).SingleAsync(x => x.UserId == userId);
                Assert.Equal(2, order.OrderItems.Count);
                Assert.Equal("Keyboard", order.OrderItems.Single(x => x.ProductId == a).ProductName);
                Assert.Equal(10m, order.OrderItems.Single(x => x.ProductId == a).UnitPrice);
                Assert.Equal(2, order.OrderItems.Single(x => x.ProductId == a).Quantity);
                Assert.Equal(3, (await db.Products.FindAsync(a))!.StockQuantity);
                Assert.Equal(7, (await db.Products.FindAsync(b))!.StockQuantity);
                var cart = await db.Carts.Include(x => x.CartItems).SingleAsync(x => x.UserId == userId);
                Assert.Equal(before.Id, cart.Id);
                Assert.Equal(before.CreatedAtUtc, cart.CreatedAtUtc);
                Assert.True(cart.UpdatedAtUtc >= before.UpdatedAtUtc);
                Assert.Empty(cart.CartItems);
                var product = (await db.Products.FindAsync(a))!;
                product.Name = "Renamed";
                product.Price = 99m;
                await db.SaveChangesAsync();
            });
            await WithDb(async db =>
            {
                var snapshot = await db.OrderItems.SingleAsync(x => x.ProductId == a);
                Assert.Equal("Keyboard", snapshot.ProductName);
                Assert.Equal(10m, snapshot.UnitPrice);
            });
        }
    }

    [Fact]
    public async Task CheckoutUsesCurrentDatabasePriceAfterProductPriceChangesInCart()
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var productId = await Product("Price Change Product", 100m, 5);
            await Add(customer, productId, 2);
            await WithDb(async db =>
            {
                (await db.Products.FindAsync(productId))!.Price = 120m;
                await db.SaveChangesAsync();
            });

            var response = await customer.PostAsync("/api/orders", null);
            var body = await response.Content.ReadFromJsonAsync<OrderDetailsResponse>();

            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(240m, body!.TotalAmount);
            var responseItem = Assert.Single(body.Items);
            Assert.Equal(120m, responseItem.UnitPrice);
            Assert.Equal(2, responseItem.Quantity);
            Assert.Equal(240m, responseItem.TotalPrice);

            await WithDb(async db =>
            {
                var order = await db.Orders.Include(x => x.OrderItems).SingleAsync(x => x.UserId == userId);
                Assert.Equal(240m, order.TotalAmount);
                var orderItem = Assert.Single(order.OrderItems);
                Assert.Equal(120m, orderItem.UnitPrice);
                Assert.Equal(2, orderItem.Quantity);
                Assert.Equal(240m, orderItem.TotalPrice);
                Assert.Equal(3, (await db.Products.FindAsync(productId))!.StockQuantity);
                Assert.Empty((await db.Carts.Include(x => x.CartItems).SingleAsync(x => x.UserId == userId)).CartItems);
            });

            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsync("/api/orders", null)).StatusCode);
            await WithDb(async db => Assert.Equal(1, await db.Orders.CountAsync(x => x.UserId == userId)));
        }
    }

    [Fact]
    public async Task StockExactlyEqualToQuantitySucceedsAndReachesZero()
    {
        var (customer, _) = await Customer();
        using (customer)
        {
            var productId = await Product("Exact", 4m, 3);
            await Add(customer, productId, 3);
            Assert.Equal(HttpStatusCode.Created, (await customer.PostAsync("/api/orders", null)).StatusCode);
            await WithDb(async db => Assert.Equal(0, (await db.Products.FindAsync(productId))!.StockQuantity));
        }
    }

    [Fact]
    public async Task FailedSecondReservationRollsBackFirstAndPreservesEntireCart()
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var first = await Product("First", 10m, 10);
            var second = await Product("Second", 20m, 1);
            await Add(customer, first, 2);
            await Add(customer, second, 1);
            await WithDb(async db => { (await db.CartItems.SingleAsync(x => x.ProductId == second)).Quantity = 2; await db.SaveChangesAsync(); });

            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsync("/api/orders", null)).StatusCode);
            await WithDb(async db =>
            {
                Assert.Equal(10, (await db.Products.FindAsync(first))!.StockQuantity);
                Assert.Equal(1, (await db.Products.FindAsync(second))!.StockQuantity);
                Assert.Equal(2, await db.CartItems.CountAsync(x => x.Cart.UserId == userId));
                Assert.False(await db.Orders.AnyAsync());
                Assert.False(await db.OrderItems.AnyAsync());
            });
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UnavailableProductOrCategoryRejectsCheckoutAndPreservesState(bool disableProduct, bool disableCategory)
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var productId = await Product("Unavailable", 8m, 4);
            await Add(customer, productId, 2);
            await WithDb(async db =>
            {
                var product = await db.Products.Include(x => x.Category).SingleAsync(x => x.Id == productId);
                if (disableProduct) product.IsActive = false;
                if (disableCategory) product.Category.IsActive = false;
                await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsync("/api/orders", null)).StatusCode);
            await WithDb(async db =>
            {
                Assert.Equal(4, (await db.Products.FindAsync(productId))!.StockQuantity);
                Assert.True(await db.CartItems.AnyAsync(x => x.Cart.UserId == userId));
                Assert.False(await db.Orders.AnyAsync());
            });
        }
    }

    [Fact]
    public async Task AuthenticatedIdentityAndServerValuesCannotBeOverriddenAndCustomersAreIsolated()
    {
        var (a, aId) = await Customer();
        var (b, _) = await Customer();
        using (a) using (b)
        {
            var productId = await Product("Owned", 12m, 5);
            await Add(a, productId, 2);
            var attack = await b.PostAsJsonAsync("/api/orders", new { userId = aId, totalAmount = 0.01m, status = "Delivered" });
            Assert.Equal(HttpStatusCode.Conflict, attack.StatusCode);
            await WithDb(async db =>
            {
                Assert.Equal(5, (await db.Products.FindAsync(productId))!.StockQuantity);
                Assert.True(await db.CartItems.AnyAsync(x => x.Cart.UserId == aId));
                Assert.False(await db.Orders.AnyAsync());
            });
            var success = await a.PostAsJsonAsync("/api/orders", new { userId = int.MaxValue, totalAmount = 0.01m, status = "Delivered" });
            Assert.Equal(HttpStatusCode.Created, success.StatusCode);
            await WithDb(async db =>
            {
                var order = await db.Orders.SingleAsync();
                Assert.Equal(aId, order.UserId);
                Assert.Equal(24m, order.TotalAmount);
                Assert.Equal(OrderStatus.Pending, order.Status);
            });
        }
    }

    [Fact]
    public async Task ConditionalRelationalUpdatesAllowOnlyAvailableQuantity()
    {
        var productId = await Product("Atomic", 5m, 1);
        var first = 0;
        var second = 0;
        await WithDb(async db => first = await db.Products.Where(x => x.Id == productId && x.StockQuantity >= 1)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, x => x.StockQuantity - 1)));
        await WithDb(async db => second = await db.Products.Where(x => x.Id == productId && x.StockQuantity >= 1)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StockQuantity, x => x.StockQuantity - 1)));
        Assert.Equal(1, first);
        Assert.Equal(0, second);
        await WithDb(async db => Assert.Equal(0, (await db.Products.FindAsync(productId))!.StockQuantity));
    }

    [Fact]
    public async Task CustomerOrderHistoryIsPagedFilteredOwnedAndUsesHistoricalSnapshots()
    {
        var (a, aId) = await Customer();
        var (b, _) = await Customer();
        using (a) using (b)
        {
            var productId = await Product("Historical", 12m, 20);
            for (var i = 0; i < 3; i++) { await Add(a, productId, 1); Assert.Equal(HttpStatusCode.Created, (await a.PostAsync("/api/orders", null)).StatusCode); }
            await Add(b, productId, 1); var foreign = await b.PostAsync("/api/orders", null);
            var foreignOrder = await foreign.Content.ReadFromJsonAsync<OrderDetailsResponse>();
            int newestId = 0;
            await WithDb(async db =>
            {
                newestId = await db.Orders.Where(x => x.UserId == aId).MaxAsync(x => x.Id);
                var product = (await db.Products.FindAsync(productId))!; product.Name = "Changed"; product.Price = 99m;
                await db.SaveChangesAsync();
            });

            var page1 = await (await a.GetAsync("/api/orders?pageNumber=1&pageSize=2&status=Pending")).Content.ReadFromJsonAsync<PagedResponse<OrderSummaryResponse>>();
            Assert.Equal(3, page1!.TotalCount); Assert.Equal(2, page1.TotalPages); Assert.Equal(2, page1.Items.Count);
            Assert.Equal(page1.Items.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Select(x => x.Id), page1.Items.Select(x => x.Id));
            var page2 = await (await a.GetAsync("/api/orders?pageNumber=2&pageSize=2")).Content.ReadFromJsonAsync<PagedResponse<OrderSummaryResponse>>();
            Assert.Single(page2!.Items); Assert.DoesNotContain(page1.Items, x => x.Id == foreignOrder!.Id);
            var detail = await (await a.GetAsync($"/api/orders/{newestId}")).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            Assert.Equal("Historical", Assert.Single(detail!.Items).ProductName); Assert.Equal(12m, detail.Items[0].UnitPrice);
            Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/orders/{newestId}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/orders?pageNumber=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/orders?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await a.GetAsync("/api/orders?fromDate=2026-02-01&toDate=2026-01-01")).StatusCode);
        }
    }

    [Fact]
    public async Task CustomerOrderListFiltersStatusAndInclusiveDateRangeWithoutLeakingForeignOrders()
    {
        var (customer, userId) = await Customer(); var (foreign, foreignUserId) = await Customer();
        using (customer) using (foreign)
        {
            var early = new DateTime(2026, 1, 10, 8, 0, 0, DateTimeKind.Utc);
            var middle = new DateTime(2026, 1, 20, 8, 0, 0, DateTimeKind.Utc);
            var late = new DateTime(2026, 1, 30, 8, 0, 0, DateTimeKind.Utc);
            var earlyPending = await SeedOrder(userId, OrderStatus.Pending, early);
            var middleCancelled = await SeedOrder(userId, OrderStatus.Cancelled, middle);
            var latePending = await SeedOrder(userId, OrderStatus.Pending, late);
            var foreignPending = await SeedOrder(foreignUserId, OrderStatus.Pending, middle);

            var pending = await GetCustomerOrders(customer, "/api/orders?status=Pending");
            Assert.Equal([latePending, earlyPending], pending.Items.Select(x => x.Id));
            Assert.DoesNotContain(pending.Items, x => x.Id == foreignPending);
            var cancelled = await GetCustomerOrders(customer, "/api/orders?status=Cancelled");
            Assert.Equal([middleCancelled], cancelled.Items.Select(x => x.Id));

            var from = Uri.EscapeDataString(middle.ToString("O"));
            var fromResult = await GetCustomerOrders(customer, $"/api/orders?fromDate={from}");
            Assert.Equal([latePending, middleCancelled], fromResult.Items.Select(x => x.Id));
            var to = Uri.EscapeDataString(middle.ToString("O"));
            var toResult = await GetCustomerOrders(customer, $"/api/orders?toDate={to}");
            Assert.Equal([middleCancelled, earlyPending], toResult.Items.Select(x => x.Id));
        }
    }

    [Fact]
    public async Task CustomerOrderDetailEnforcesMissingAnonymousAndRoleContracts()
    {
        var (customer, userId) = await Customer(); using (customer)
        using (var anonymous = Client()) using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            var orderId = await SeedOrder(userId, OrderStatus.Pending, DateTime.UtcNow);
            Assert.Equal(HttpStatusCode.NotFound, (await customer.GetAsync($"/api/orders/{int.MaxValue}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/orders/{orderId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync($"/api/orders/{orderId}")).StatusCode);
        }
    }

    [Fact]
    public async Task CancellationRestoresAllInventoryExactlyOnceAndPreservesSnapshots()
    {
        var (customer, _) = await Customer();
        using (customer)
        {
            var a = await Product("Cancel A", 10m, 10); var b = await Product("Cancel B", 5m, 8);
            await Add(customer, a, 3); await Add(customer, b, 2);
            var created = await (await customer.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            var response = await customer.PostAsync($"/api/orders/{created!.Id}/cancel", null);
            var cancelled = await response.Content.ReadFromJsonAsync<OrderDetailsResponse>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(OrderStatus.Cancelled, cancelled!.Status);
            Assert.Equal(created.CreatedAtUtc, cancelled.CreatedAtUtc); Assert.Equal(created.TotalAmount, cancelled.TotalAmount);
            Assert.Equal(created.Items, cancelled.Items); Assert.True(cancelled.UpdatedAtUtc >= created.UpdatedAtUtc);
            await WithDb(async db => { Assert.Equal(10, (await db.Products.FindAsync(a))!.StockQuantity); Assert.Equal(8, (await db.Products.FindAsync(b))!.StockQuantity); });
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsync($"/api/orders/{created.Id}/cancel", null)).StatusCode);
            await WithDb(async db => { Assert.Equal(10, (await db.Products.FindAsync(a))!.StockQuantity); Assert.Equal(8, (await db.Products.FindAsync(b))!.StockQuantity); });
        }
    }

    [Fact]
    public async Task AdminAuthorizationListingDetailsAndForwardTransitionsWorkWithoutChangingStock()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/orders")).StatusCode);
        var (customer, userId) = await Customer(); using (customer)
        using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/admin/orders")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/orders")).StatusCode);
            var productId = await Product("Admin Snapshot", 7m, 5); await Add(customer, productId, 2);
            var order = await (await customer.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            var list = await (await admin.GetAsync("/api/admin/orders?customerEmail=ORDER-CUSTOMER")).Content.ReadFromJsonAsync<PagedResponse<AdminOrderSummaryResponse>>();
            Assert.Contains(list!.Items, x => x.Id == order!.Id && x.UserId == userId && x.TotalItems == 2);
            var details = await (await admin.GetAsync($"/api/admin/orders/{order!.Id}")).Content.ReadFromJsonAsync<AdminOrderDetailsResponse>();
            Assert.Equal(userId, details!.UserId); Assert.Equal("Admin Snapshot", Assert.Single(details.Items).ProductName);
            var createdAt = details.CreatedAtUtc;
            var previousUpdatedAt = details.UpdatedAtUtc;
            await WithDb(async db =>
            {
                await db.Orders.Where(x => x.Id == order.Id).ExecuteUpdateAsync(s =>
                    s.SetProperty(x => x.UpdatedAtUtc, createdAt.AddMinutes(-1)));
                previousUpdatedAt = createdAt.AddMinutes(-1);
            });
            foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Shipped, OrderStatus.Delivered })
            {
                var updated = await admin.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequest(status));
                Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
                var body = await updated.Content.ReadFromJsonAsync<AdminOrderDetailsResponse>();
                Assert.Equal(createdAt, body!.CreatedAtUtc);
                Assert.True(body.UpdatedAtUtc > previousUpdatedAt);
                previousUpdatedAt = body.UpdatedAtUtc;
                await WithDb(async db => Assert.Equal(3, (await db.Products.FindAsync(productId))!.StockQuantity));
            }
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Pending))).StatusCode);
            await WithDb(async db => Assert.Equal(3, (await db.Products.FindAsync(productId))!.StockQuantity));
        }
    }

    [Fact]
    public async Task AdminOrderListFiltersPagesAndOrdersAcrossCustomersDeterministically()
    {
        var (firstCustomer, firstUserId) = await Customer(); var (secondCustomer, secondUserId) = await Customer();
        using (firstCustomer) using (secondCustomer) using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            string firstEmail = ""; string secondEmail = "";
            await WithDb(async db =>
            {
                firstEmail = (await db.Users.FindAsync(firstUserId))!.Email;
                secondEmail = (await db.Users.FindAsync(secondUserId))!.Email;
            });
            var day1 = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
            var day2 = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);
            var day3 = new DateTime(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);
            var oldest = await SeedOrder(firstUserId, OrderStatus.Pending, day1);
            var tiedLowerId = await SeedOrder(secondUserId, OrderStatus.Cancelled, day2);
            var tiedHigherId = await SeedOrder(firstUserId, OrderStatus.Pending, day2);
            var newest = await SeedOrder(secondUserId, OrderStatus.Delivered, day3);

            var page1 = await GetAdminOrders(admin, "/api/admin/orders?pageNumber=1&pageSize=2");
            Assert.Equal(4, page1.TotalCount); Assert.Equal(2, page1.TotalPages);
            Assert.Equal([newest, tiedHigherId], page1.Items.Select(x => x.Id));
            var page2 = await GetAdminOrders(admin, "/api/admin/orders?pageNumber=2&pageSize=2");
            Assert.Equal([tiedLowerId, oldest], page2.Items.Select(x => x.Id));
            Assert.Contains(page1.Items.Concat(page2.Items), x => x.CustomerEmail == firstEmail);
            Assert.Contains(page1.Items.Concat(page2.Items), x => x.CustomerEmail == secondEmail);

            var pending = await GetAdminOrders(admin, "/api/admin/orders?status=Pending");
            Assert.Equal([tiedHigherId, oldest], pending.Items.Select(x => x.Id));
            var emailSearch = Uri.EscapeDataString(firstEmail[3..^3].ToUpperInvariant());
            var byEmail = await GetAdminOrders(admin, $"/api/admin/orders?customerEmail={emailSearch}");
            Assert.Equal([tiedHigherId, oldest], byEmail.Items.Select(x => x.Id));
            var from = Uri.EscapeDataString(day2.ToString("O")); var to = Uri.EscapeDataString(day2.ToString("O"));
            var dated = await GetAdminOrders(admin, $"/api/admin/orders?fromDate={from}&toDate={to}");
            Assert.Equal([tiedHigherId, tiedLowerId], dated.Items.Select(x => x.Id));

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/orders?pageNumber=0")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/orders?pageSize=101")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/orders?fromDate=2026-02-02&toDate=2026-02-01")).StatusCode);
        }
    }

    [Fact]
    public async Task AdminDetailUsesHistoricalOrderItemSnapshotAfterProductChanges()
    {
        var (customer, _) = await Customer(); using (customer) using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            var productId = await Product("Original Admin Product", 11m, 5); await Add(customer, productId, 2);
            var order = await (await customer.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            await WithDb(async db => { var product = (await db.Products.FindAsync(productId))!; product.Name = "Current Name"; product.Price = 99m; await db.SaveChangesAsync(); });
            var detail = await (await admin.GetAsync($"/api/admin/orders/{order!.Id}")).Content.ReadFromJsonAsync<AdminOrderDetailsResponse>();
            var item = Assert.Single(detail!.Items);
            Assert.Equal("Original Admin Product", item.ProductName); Assert.Equal(11m, item.UnitPrice); Assert.Equal(22m, item.TotalPrice);
        }
    }

    [Fact]
    public async Task InvalidAdminTransitionAndCancelAfterConfirmationCannotRestoreStock()
    {
        var (customer, _) = await Customer(); using (customer)
        using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            var productId = await Product("Race", 4m, 5); await Add(customer, productId, 2);
            var order = await (await customer.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/orders/{order!.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Shipped))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Cancelled))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Confirmed))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsync($"/api/orders/{order.Id}/cancel", null)).StatusCode);
            await WithDb(async db => { Assert.Equal(OrderStatus.Confirmed, (await db.Orders.FindAsync(order.Id))!.Status); Assert.Equal(3, (await db.Products.FindAsync(productId))!.StockQuantity); });
        }
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Shipped)]
    [InlineData(OrderStatus.Pending, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Confirmed, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Pending)]
    [InlineData(OrderStatus.Shipped, OrderStatus.Confirmed)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Pending)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Confirmed)]
    public async Task AdminRejectsInvalidTransitionAndPreservesCurrentStatus(OrderStatus current, OrderStatus requested)
    {
        var (customer, userId) = await Customer(); using (customer) using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            var orderId = await SeedOrder(userId, current, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc));
            var response = await admin.PatchAsJsonAsync($"/api/admin/orders/{orderId}/status", new UpdateOrderStatusRequest(requested));
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            await WithDb(async db => Assert.Equal(current, (await db.Orders.FindAsync(orderId))!.Status));
        }
    }

    [Fact]
    public async Task AdminStatusPatchRequiresAdminRole()
    {
        var (customer, userId) = await Customer(); using (customer) using (var anonymous = Client())
        {
            var orderId = await SeedOrder(userId, OrderStatus.Pending, DateTime.UtcNow);
            var request = new UpdateOrderStatusRequest(OrderStatus.Confirmed);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PatchAsJsonAsync($"/api/admin/orders/{orderId}/status", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await customer.PatchAsJsonAsync($"/api/admin/orders/{orderId}/status", request)).StatusCode);
            await WithDb(async db => Assert.Equal(OrderStatus.Pending, (await db.Orders.FindAsync(orderId))!.Status));
        }
    }

    [Fact]
    public async Task CancellationRestoresStockWhenProductAndCategoryAreInactive()
    {
        var (customer, _) = await Customer(); using (customer)
        {
            var productId = await Product("Inactive After Sale", 6m, 5); await Add(customer, productId, 2);
            var order = await (await customer.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            await WithDb(async db =>
            {
                var product = await db.Products.Include(x => x.Category).SingleAsync(x => x.Id == productId);
                Assert.Equal(3, product.StockQuantity); product.IsActive = false; product.Category.IsActive = false; await db.SaveChangesAsync();
            });
            var response = await customer.PostAsync($"/api/orders/{order!.Id}/cancel", null);
            var cancelled = await response.Content.ReadFromJsonAsync<OrderDetailsResponse>();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(OrderStatus.Cancelled, cancelled!.Status);
            await WithDb(async db => Assert.Equal(5, (await db.Products.FindAsync(productId))!.StockQuantity));
        }
    }

    [Fact]
    public async Task ForeignCancellationIsHiddenAndAdminCannotAdvanceCancelledOrder()
    {
        var (owner, _) = await Customer(); var (other, _) = await Customer();
        using (owner) using (other) using (var admin = await AuthenticatedClient(UserRole.Admin))
        {
            var productId = await Product("Owned Cancellation", 9m, 5); await Add(owner, productId, 2);
            var order = await (await owner.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/orders/{order!.Id}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/orders/{order.Id}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/orders/{order.Id}/status", new UpdateOrderStatusRequest(OrderStatus.Confirmed))).StatusCode);
            await WithDb(async db => { Assert.Equal(OrderStatus.Cancelled, (await db.Orders.FindAsync(order.Id))!.Status); Assert.Equal(5, (await db.Products.FindAsync(productId))!.StockQuantity); });
        }
    }

    [Fact]
    public async Task RelationalConditionalCancellationClaimSucceedsOnlyOnce()
    {
        var (customer, _) = await Customer(); using (customer)
        {
            var productId = await Product("Claim", 3m, 2); await Add(customer, productId, 1);
            var order = await (await customer.PostAsync("/api/orders", null)).Content.ReadFromJsonAsync<OrderDetailsResponse>();
            var first = 0; var second = 0;
            await WithDb(async db => first = await db.Orders.Where(x => x.Id == order!.Id && x.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OrderStatus.Cancelled)));
            await WithDb(async db => second = await db.Orders.Where(x => x.Id == order!.Id && x.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, OrderStatus.Cancelled)));
            Assert.Equal(1, first); Assert.Equal(0, second);
        }
    }

    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    private static async Task<PagedResponse<OrderSummaryResponse>> GetCustomerOrders(HttpClient client, string uri) =>
        (await (await client.GetAsync(uri)).Content.ReadFromJsonAsync<PagedResponse<OrderSummaryResponse>>())!;
    private static async Task<PagedResponse<AdminOrderSummaryResponse>> GetAdminOrders(HttpClient client, string uri) =>
        (await (await client.GetAsync(uri)).Content.ReadFromJsonAsync<PagedResponse<AdminOrderSummaryResponse>>())!;
    private async Task<(HttpClient Client, int UserId)> Customer()
    {
        var client = await AuthenticatedClient(UserRole.Customer);
        var email = client.DefaultRequestHeaders.GetValues("X-Test-Email").Single();
        client.DefaultRequestHeaders.Remove("X-Test-Email");
        var id = 0;
        await WithDb(async db => id = (await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant())).Id);
        return (client, id);
    }
    private async Task<HttpClient> AuthenticatedClient(UserRole role)
    {
        var client = Client();
        var email = $"order-{role}-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Order", "User", email, "Strong1!", "Strong1!"));
        if (role == UserRole.Admin) await WithDb(async db => { var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant()); user.Role = role; await db.SaveChangesAsync(); });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Strong1!"));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        return client;
    }
    private static async Task Add(HttpClient client, int productId, int quantity) =>
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(productId, quantity))).StatusCode);
    private async Task<int> Product(string name, decimal price, int stock)
    {
        var id = 0;
        await WithDb(async db =>
        {
            var now = DateTime.UtcNow;
            var category = new Category { Name = $"Orders {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), CreatedAtUtc = now, UpdatedAtUtc = now };
            var product = new Product { Name = name, Price = price, StockQuantity = stock, Category = category, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        });
        return id;
    }
    private async Task<int> SeedOrder(int userId, OrderStatus status, DateTime createdAtUtc)
    {
        var id = 0;
        await WithDb(async db =>
        {
            var order = new Order
            {
                UserId = userId, Status = status, TotalAmount = 0m,
                CreatedAtUtc = createdAtUtc, UpdatedAtUtc = createdAtUtc, User = null!
            };
            db.Orders.Add(order); await db.SaveChangesAsync(); id = order.Id;
        });
        return id;
    }
    private async Task WithDb(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

public sealed class RelationalOrderApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"orders-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "ECommerceOrderManagementApi",
            ["Jwt:Audience"] = "ECommerceOrderManagementApi.Client",
            ["Jwt:Key"] = "integration-test-signing-key-at-least-32-bytes-long",
            ["Jwt:AccessTokenExpirationMinutes"] = "15",
            ["Jwt:RefreshTokenExpirationDays"] = "7"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={_databasePath};Pooling=False"));
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        });
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && File.Exists(_databasePath)) File.Delete(_databasePath);
    }
}
