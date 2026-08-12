using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerceOrderManagementApi.Data;
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

    private HttpClient Client() => factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
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
