using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.DTOs.Cart;
using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.DTOs.Reports;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Tests.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Reports;

public sealed class SalesSummaryApiTests(RelationalOrderApiFactory factory)
    : IClassFixture<RelationalOrderApiFactory>, IAsyncLifetime
{
    public async Task InitializeAsync() => await WithDb(async db =>
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

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SalesSummaryRequiresAdminAndValidatesDateRange()
    {
        using var anonymous = Client();
        using var customer = await AuthenticatedClient(UserRole.Customer);
        using var admin = await AuthenticatedClient(UserRole.Admin);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/admin/reports/sales-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await customer.GetAsync("/api/admin/reports/sales-summary")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.GetAsync("/api/admin/reports/sales-summary?fromDate=2026-01-02&toDate=2026-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/admin/reports/sales-summary?fromDate=2026-01-01&toDate=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task EmptySalesSummaryReturnsZerosAndEmptyProducts()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var response = await admin.GetAsync("/api/admin/reports/sales-summary");
        var summary = await response.Content.ReadFromJsonAsync<SalesSummaryResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, summary!.TotalOrders);
        Assert.Equal(0m, summary.TotalRevenue);
        Assert.Equal(0m, summary.AverageOrderValue);
        Assert.Empty(summary.TopSellingProducts);
    }

    [Fact]
    public async Task RecognizedStatusesInclusiveDatesAndHistoricalValuesAreReported()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var userId = await UserId(admin);
        var productId = await Product("Original", 100m);
        var jan1 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var jan5 = new DateTime(2026, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var jan10 = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        await Order(userId, productId, OrderStatus.Pending, 999m, 9, jan5);
        await Order(userId, productId, OrderStatus.Cancelled, 888m, 8, jan5);
        await Order(userId, productId, OrderStatus.Confirmed, 100m, 1, jan1);
        await Order(userId, productId, OrderStatus.Shipped, 50m, 1, jan5);
        await Order(userId, productId, OrderStatus.Delivered, 150m, 2, jan10);
        await WithDb(async db =>
        {
            var product = (await db.Products.FindAsync(productId))!;
            product.Name = "Current Name";
            product.Price = 999m;
            product.IsActive = false;
            await db.SaveChangesAsync();
        });

        var all = await Summary(admin, "/api/admin/reports/sales-summary");
        Assert.Equal(3, all.TotalOrders);
        Assert.Equal(300m, all.TotalRevenue);
        Assert.Equal(100m, all.AverageOrderValue);
        var allProduct = Assert.Single(all.TopSellingProducts);
        Assert.Equal("Current Name", allProduct.ProductName);
        Assert.Equal(4, allProduct.QuantitySold);
        Assert.Equal(300m, allProduct.Revenue);

        var from = Uri.EscapeDataString(jan5.ToString("O"));
        var to = Uri.EscapeDataString(jan10.ToString("O"));
        var dated = await Summary(admin, $"/api/admin/reports/sales-summary?fromDate={from}&toDate={to}");
        Assert.Equal(2, dated.TotalOrders);
        Assert.Equal(200m, dated.TotalRevenue);
        Assert.Equal(3, Assert.Single(dated.TopSellingProducts).QuantitySold);
    }

    [Fact]
    public async Task TopProductsAggregateAndUseAllDeterministicTieBreakersBeforeTakingFive()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var userId = await UserId(admin);
        var products = new List<int>();
        for (var i = 1; i <= 6; i++) products.Add(await Product($"Product {i}", i));
        var date = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        await Order(userId, products[0], OrderStatus.Confirmed, 10m, 5, date);
        await Order(userId, products[0], OrderStatus.Shipped, 5m, 1, date);
        await Order(userId, products[1], OrderStatus.Confirmed, 20m, 5, date);
        await Order(userId, products[2], OrderStatus.Confirmed, 10m, 5, date);
        await Order(userId, products[3], OrderStatus.Confirmed, 10m, 5, date);
        await Order(userId, products[4], OrderStatus.Confirmed, 40m, 4, date);
        await Order(userId, products[5], OrderStatus.Confirmed, 1m, 1, date);

        var summary = await Summary(admin, "/api/admin/reports/sales-summary");
        Assert.Equal(5, summary.TopSellingProducts.Count);
        Assert.Equal(
            [products[0], products[1], products[2], products[3], products[4]],
            summary.TopSellingProducts.Select(x => x.ProductId));
        Assert.Equal(6, summary.TopSellingProducts[0].QuantitySold);
        Assert.Equal(15m, summary.TopSellingProducts[0].Revenue);
    }

    [Fact]
    public async Task RealCheckoutConfirmedOrderReportsHistoricalMoneyAndCurrentInactiveProductName()
    {
        using var customer = await AuthenticatedClient(UserRole.Customer);
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var productId = await Product("Checkout Reporting Product", 100m);

        var addResponse = await customer.PostAsJsonAsync(
            "/api/cart/items",
            new AddCartItemRequest(productId, 2));
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);

        var checkoutResponse = await customer.PostAsync("/api/orders", null);
        var order = await checkoutResponse.Content.ReadFromJsonAsync<OrderDetailsResponse>();
        Assert.Equal(HttpStatusCode.Created, checkoutResponse.StatusCode);
        Assert.Equal(200m, order!.TotalAmount);

        var confirmResponse = await admin.PatchAsJsonAsync(
            $"/api/admin/orders/{order.Id}/status",
            new UpdateOrderStatusRequest(OrderStatus.Confirmed));
        Assert.Equal(HttpStatusCode.OK, confirmResponse.StatusCode);

        await WithDb(async db =>
        {
            var product = (await db.Products.FindAsync(productId))!;
            product.Name = "Current Reporting Name";
            product.Price = 999m;
            product.IsActive = false;
            await db.SaveChangesAsync();
        });

        var summary = await Summary(admin, "/api/admin/reports/sales-summary");
        Assert.Equal(1, summary.TotalOrders);
        Assert.Equal(200m, summary.TotalRevenue);
        Assert.Equal(200m, summary.AverageOrderValue);
        var topProduct = Assert.Single(summary.TopSellingProducts);
        Assert.Equal(productId, topProduct.ProductId);
        Assert.Equal("Current Reporting Name", topProduct.ProductName);
        Assert.Equal(2, topProduct.QuantitySold);
        Assert.Equal(200m, topProduct.Revenue);
    }

    [Fact]
    public async Task AverageOrderValueRoundsMidpointAwayFromZero()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var userId = await UserId(admin);
        var productId = await Product("Rounding Product", 10m);
        var date = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        await Order(userId, productId, OrderStatus.Confirmed, 10m, 1, date);
        await Order(userId, productId, OrderStatus.Confirmed, 10.01m, 1, date.AddMinutes(1));

        var summary = await Summary(admin, "/api/admin/reports/sales-summary");
        Assert.Equal(2, summary.TotalOrders);
        Assert.Equal(20.01m, summary.TotalRevenue);
        Assert.Equal(10.01m, summary.AverageOrderValue);
    }

    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

    private async Task<HttpClient> AuthenticatedClient(UserRole role)
    {
        var client = Client();
        var email = $"report-{role}-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Report", "User", email, "Strong1!", "Strong1!"));
        if (role == UserRole.Admin)
            await WithDb(async db =>
            {
                (await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant())).Role = role;
                await db.SaveChangesAsync();
            });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Strong1!"));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        return client;
    }

    private async Task<int> UserId(HttpClient client)
    {
        var email = client.DefaultRequestHeaders.GetValues("X-Test-Email").Single();
        var id = 0;
        await WithDb(async db => id = (await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant())).Id);
        return id;
    }

    private async Task<int> Product(string name, decimal price)
    {
        var id = 0;
        await WithDb(async db =>
        {
            var now = DateTime.UtcNow;
            var category = new Category { Name = $"Reports {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), CreatedAtUtc = now, UpdatedAtUtc = now };
            var product = new Product { Name = name, Price = price, StockQuantity = 100, Category = category, CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            id = product.Id;
        });
        return id;
    }

    private async Task Order(int userId, int productId, OrderStatus status, decimal total, int quantity, DateTime date)
    {
        await WithDb(async db =>
        {
            var order = new Order { UserId = userId, Status = status, TotalAmount = total, CreatedAtUtc = date, UpdatedAtUtc = date, User = null! };
            order.OrderItems.Add(new OrderItem { ProductId = productId, ProductName = "Historical Name", UnitPrice = total / quantity, Quantity = quantity, TotalPrice = total, Order = order, Product = null! });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });
    }

    private static async Task<SalesSummaryResponse> Summary(HttpClient client, string uri) =>
        (await (await client.GetAsync(uri)).Content.ReadFromJsonAsync<SalesSummaryResponse>())!;

    private async Task WithDb(Func<AppDbContext, Task> action)
    {
        using var scope = factory.Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
