using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs;
using ECommerceOrderManagementApi.DTOs.Auth;
using ECommerceOrderManagementApi.DTOs.Categories;
using ECommerceOrderManagementApi.DTOs.Products;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Tests.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ECommerceOrderManagementApi.Tests.Catalog;

public sealed class CatalogApiTests : IClassFixture<AuthenticationApiFactory>
{
    private readonly AuthenticationApiFactory _factory;
    public CatalogApiTests(AuthenticationApiFactory factory) => _factory = factory;

    [Fact]
    public async Task CatalogWritesEnforceAdminRole()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("A", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/products", new CreateProductRequest("P", null, 1, 0, 1))).StatusCode);
        using var customer = await AuthenticatedClient(UserRole.Customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("A", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync("/api/products", new CreateProductRequest("P", null, 1, 0, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsJsonAsync("/api/categories/1", new UpdateCategoryRequest("A", null, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.DeleteAsync("/api/categories/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PutAsJsonAsync("/api/products/1", new UpdateProductRequest("P", null, 1, 0, 1, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.DeleteAsync("/api/products/1")).StatusCode);
    }

    [Fact]
    public async Task AdminCreatesTrimmedNormalizedCategory_AndDuplicatesConflict()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var name = $"  Electronics {Guid.NewGuid():N}  ";
        var response = await admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name, "  Devices  "));
        var category = await response.Content.ReadFromJsonAsync<CategoryResponse>();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(category!.IsActive);
        Assert.Equal(name.Trim(), category.Name);
        Assert.Equal("Devices", category.Description);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name.ToUpperInvariant(), null))).StatusCode);
        await WithDb(async db => Assert.Equal(name.Trim().ToUpperInvariant(), (await db.Categories.FindAsync(category.Id))!.NormalizedName));
    }

    [Fact]
    public async Task CategoryVisibilityUpdateAndSafeDeactivationWork()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var category = await CreateCategory(admin);
        var product = await CreateProduct(admin, category.Id, "Active product", 10, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/categories/{category.Id}")).StatusCode);
        using var publicClient = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/categories/{category.Id}")).StatusCode);
        var updated = await admin.PutAsJsonAsync($"/api/categories/{category.Id}", new UpdateCategoryRequest(category.Name, category.Description, true));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/categories/{category.Id}")).StatusCode);
    }

    [Fact]
    public async Task ProductsRequireActiveCategory_ValidateValues_AndDeactivateWithoutDeleting()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/products", new CreateProductRequest("P", null, 1, 0, int.MaxValue))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/products", new CreateProductRequest("P", null, 0, -1, 1))).StatusCode);
        var category = await CreateCategory(admin);
        var product = await CreateProduct(admin, category.Id, "  Keyboard  ", 25, 4);
        var createdAt = product.CreatedAtUtc;
        var update = await admin.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductRequest("Keyboard Pro", "  Better  ", 30, 5, category.Id, true));
        var updated = await update.Content.ReadFromJsonAsync<ProductResponse>();
        Assert.Equal(createdAt, updated!.CreatedAtUtc);
        Assert.True(updated.UpdatedAtUtc >= product.UpdatedAtUtc);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        using var publicClient = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/products/{product.Id}")).StatusCode);
        await WithDb(async db => Assert.NotNull(await db.Products.FindAsync(product.Id)));
    }

    [Fact]
    public async Task InactiveCategoryCannotReceiveOrExposeProducts_ButProductCanReactivateAfterCategoryDoes()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var category = await CreateCategory(admin);
        var product = await CreateProduct(admin, category.Id, "Hidden product", 12, 2);
        await WithDb(async db => { var stored = await db.Categories.FindAsync(category.Id); stored!.IsActive = false; await db.SaveChangesAsync(); });
        using var publicClient = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/products", new CreateProductRequest("Rejected", null, 2, 1, category.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductRequest(product.Name, null, product.Price, product.StockQuantity, category.Id, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/categories/{category.Id}", new UpdateCategoryRequest(category.Name, null, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/products/{product.Id}", new UpdateProductRequest(product.Name, null, product.Price, product.StockQuantity, category.Id, true))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await publicClient.GetAsync($"/api/products/{product.Id}")).StatusCode);
    }

    [Fact]
    public async Task ProductQuerySupportsPaginationSearchFiltersAndDeterministicSorting()
    {
        using var admin = await AuthenticatedClient(UserRole.Admin);
        var category = await CreateCategory(admin);
        await CreateProduct(admin, category.Id, "Alpha", 20, 5, "special description");
        await CreateProduct(admin, category.Id, "Beta", 10, 5);
        await CreateProduct(admin, category.Id, "Gamma", 30, 1);
        using var publicClient = Client();
        var page = await publicClient.GetFromJsonAsync<PagedResponse<ProductResponse>>($"/api/products?categoryId={category.Id}&minPrice=10&maxPrice=30&pageSize=2&sortBy=stock&sortDirection=desc");
        Assert.Equal(3, page!.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
        Assert.True(page.Items[0].Id > page.Items[1].Id);
        var search = await publicClient.GetFromJsonAsync<PagedResponse<ProductResponse>>("/api/products?search=special");
        Assert.Contains(search!.Items, x => x.Name == "Alpha");
        Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/api/products?pageNumber=0&pageSize=101&sortBy=hacker&sortDirection=sideways")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/api/products?sortDirection=")).StatusCode);
    }

    private HttpClient Client() => _factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
    private async Task<HttpClient> AuthenticatedClient(UserRole role)
    {
        var client = Client();
        var email = $"{role}-{Guid.NewGuid():N}@example.com";
        await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest("Test", "User", email, "Strong1!", "Strong1!"));
        if (role == UserRole.Admin) await WithDb(async db => { var user = await db.Users.SingleAsync(x => x.NormalizedEmail == email.ToUpperInvariant()); user.Role = role; await db.SaveChangesAsync(); });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "Strong1!"));
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);
        return client;
    }
    private static async Task<CategoryResponse> CreateCategory(HttpClient client)
    { var response = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest($"Category {Guid.NewGuid():N}", " Description ")); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<CategoryResponse>())!; }
    private static async Task<ProductResponse> CreateProduct(HttpClient client, int categoryId, string name, decimal price, int stock, string? description = null)
    { var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest(name, description, price, stock, categoryId)); response.EnsureSuccessStatusCode(); return (await response.Content.ReadFromJsonAsync<ProductResponse>())!; }
    private async Task WithDb(Func<AppDbContext, Task> action)
    { using var scope = _factory.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<AppDbContext>()); }
}
