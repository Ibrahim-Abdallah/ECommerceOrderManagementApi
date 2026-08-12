using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.DTOs.Cart;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Tests.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.ShoppingCart;

public sealed class CartApiTests(AuthenticationApiFactory factory) : IClassFixture<AuthenticationApiFactory>
{
    [Fact]
    public async Task CartEndpointsRequireCustomerRole()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(1, 1))).StatusCode);
        using var admin = await AuthenticatedClient(UserRole.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/cart")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(1, 1))).StatusCode);
    }

    [Fact]
    public async Task GetLazilyCreatesOneStableEmptyCart()
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var first = await customer.GetFromJsonAsync<CartResponse>("/api/cart");
            var second = await customer.GetFromJsonAsync<CartResponse>("/api/cart");
            Assert.Equal(first!.Id, second!.Id);
            Assert.Empty(first.Items);
            Assert.Equal(0, first.TotalQuantity);
            Assert.Equal(0m, first.TotalAmount);
            await WithDb(async db => Assert.Equal(1, await db.Carts.CountAsync(x => x.UserId == userId)));
        }
    }

    [Fact]
    public async Task AddValidatesAvailabilityStockDuplicatesAndUsesCurrentPrices()
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var available = await Product(10m, 2);
            var other = await Product(15.50m, 3);
            var inactive = await Product(5m, 2, productActive: false);
            var inactiveCategory = await Product(5m, 2, categoryActive: false);
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(0, 0))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(int.MaxValue, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(inactive, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(inactiveCategory, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(available, 3))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(available, 2))).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(other, 3))).StatusCode);
            var duplicate = await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(available, 1));
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

            var cart = await customer.GetFromJsonAsync<CartResponse>("/api/cart");
            Assert.Equal(5, cart!.TotalQuantity);
            Assert.Equal(66.50m, cart.TotalAmount);
            Assert.Equal([available, other], cart.Items.Select(x => x.ProductId));
            await WithDb(async db =>
            {
                Assert.Equal(2, await db.CartItems.CountAsync(x => x.Cart.UserId == userId));
                Assert.Equal(2, (await db.Products.FindAsync(available))!.StockQuantity);
                var item = await db.CartItems.SingleAsync(x => x.Cart.UserId == userId && x.ProductId == available);
                Assert.NotEqual(default, item.CreatedAtUtc);
                Assert.NotEqual(default, item.UpdatedAtUtc);
                var product = await db.Products.FindAsync(available);
                product!.Price = 12m;
                await db.SaveChangesAsync();
            });
            cart = await customer.GetFromJsonAsync<CartResponse>("/api/cart");
            Assert.Equal(24m, cart!.Items.Single(x => x.ProductId == available).LineTotal);
            Assert.Equal(70.50m, cart.TotalAmount);
        }
    }

    [Fact]
    public async Task UpdateRemoveAndClearPreserveRequiredLifecycleRules()
    {
        var (customer, userId) = await Customer();
        using (customer)
        {
            var productId = await Product(9m, 5);
            await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(productId, 2));
            CartItem before = null!;
            int cartId = 0;
            await WithDb(async db => { before = await db.CartItems.AsNoTracking().SingleAsync(x => x.Cart.UserId == userId); cartId = before.CartId; });
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.PutAsJsonAsync($"/api/cart/items/{productId}", new UpdateCartItemRequest(0))).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await customer.PutAsJsonAsync($"/api/cart/items/{productId}", new UpdateCartItemRequest(6))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await customer.PutAsJsonAsync($"/api/cart/items/{productId}", new UpdateCartItemRequest(5))).StatusCode);
            await WithDb(async db =>
            {
                var item = await db.CartItems.AsNoTracking().SingleAsync(x => x.Cart.UserId == userId);
                Assert.Equal(5, item.Quantity);
                Assert.Equal(before.CreatedAtUtc, item.CreatedAtUtc);
                Assert.True(item.UpdatedAtUtc >= before.UpdatedAtUtc);
                Assert.Equal(5, (await db.Products.FindAsync(productId))!.StockQuantity);
                (await db.Products.FindAsync(productId))!.IsActive = false;
                await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.NotFound, (await customer.PutAsJsonAsync($"/api/cart/items/{productId}", new UpdateCartItemRequest(1))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await customer.DeleteAsync($"/api/cart/items/{productId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await customer.DeleteAsync($"/api/cart/items/{productId}")).StatusCode);

            var second = await Product(3m, 2);
            await customer.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(second, 1));
            Assert.Equal(HttpStatusCode.NoContent, (await customer.DeleteAsync("/api/cart")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await customer.DeleteAsync("/api/cart")).StatusCode);
            var cart = await customer.GetFromJsonAsync<CartResponse>("/api/cart");
            Assert.Equal(cartId, cart!.Id);
            Assert.Empty(cart.Items);
        }
    }

    [Fact]
    public async Task CustomersCannotSeeUpdateOrRemoveAnotherCustomersItems()
    {
        var (a, _) = await Customer();
        var (b, _) = await Customer();
        using (a) using (b)
        {
            var productId = await Product(10m, 5);
            await a.PostAsJsonAsync("/api/cart/items", new AddCartItemRequest(productId, 2));
            var aCart = await a.GetFromJsonAsync<CartResponse>("/api/cart");
            var bCart = await b.GetFromJsonAsync<CartResponse>("/api/cart");
            Assert.NotEqual(aCart!.Id, bCart!.Id);
            Assert.Empty(bCart.Items);
            Assert.Equal(HttpStatusCode.NotFound, (await b.PutAsJsonAsync($"/api/cart/items/{productId}", new UpdateCartItemRequest(1))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/cart/items/{productId}")).StatusCode);
            Assert.Equal(2, (await a.GetFromJsonAsync<CartResponse>("/api/cart"))!.Items.Single().Quantity);
        }
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
        var email = $"cart-{role}-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Cart", "User", email, "Strong1!", "Strong1!"));
        if (role == UserRole.Admin) await WithDb(async db => { var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant()); user.Role = role; await db.SaveChangesAsync(); });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Strong1!"));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        return client;
    }

    private async Task<int> Product(decimal price, int stock, bool productActive = true, bool categoryActive = true)
    {
        var id = 0;
        await WithDb(async db =>
        {
            var now = DateTime.UtcNow;
            var category = new Category { Name = $"Cart {Guid.NewGuid():N}", NormalizedName = Guid.NewGuid().ToString("N").ToUpperInvariant(), IsActive = categoryActive, CreatedAtUtc = now, UpdatedAtUtc = now };
            var product = new Product { Name = $"Product {Guid.NewGuid():N}", Price = price, StockQuantity = stock, Category = category, IsActive = productActive, CreatedAtUtc = now, UpdatedAtUtc = now };
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
