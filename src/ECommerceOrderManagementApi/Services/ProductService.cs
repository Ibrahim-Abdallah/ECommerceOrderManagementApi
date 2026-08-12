using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs;
using ECommerceOrderManagementApi.DTOs.Products;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderManagementApi.Services;

public sealed class ProductService(AppDbContext dbContext, TimeProvider timeProvider) : IProductService
{
    public async Task<PagedResponse<ProductResponse>> GetAllAsync(ProductQueryParameters p, CancellationToken cancellationToken)
    {
        var query = dbContext.Products.AsNoTracking().Where(x => x.IsActive && x.Category.IsActive);
        var search = p.Search?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(x => x.Name.Contains(search) || (x.Description != null && x.Description.Contains(search)));
        if (p.CategoryId.HasValue) query = query.Where(x => x.CategoryId == p.CategoryId);
        if (p.MinPrice.HasValue) query = query.Where(x => x.Price >= p.MinPrice);
        if (p.MaxPrice.HasValue) query = query.Where(x => x.Price <= p.MaxPrice);
        var totalCount = await query.CountAsync(cancellationToken);
        query = ApplySort(query, p.SortBy, string.Equals(p.SortDirection, "desc", StringComparison.OrdinalIgnoreCase));
        var items = await query.Skip((p.PageNumber - 1) * p.PageSize).Take(p.PageSize)
            .Select(ToResponse).ToListAsync(cancellationToken);
        return new(items, p.PageNumber, p.PageSize, totalCount,
            (int)Math.Ceiling(totalCount / (double)p.PageSize));
    }

    public Task<ProductResponse?> GetAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Products.AsNoTracking().Where(x => x.Id == id && x.IsActive && x.Category.IsActive)
            .Select(ToResponse).SingleOrDefaultAsync(cancellationToken);

    public async Task<ProductWriteResult> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        if (!await IsActiveCategory(request.CategoryId, cancellationToken)) return new(ProductWriteStatus.InvalidCategory);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var product = new Product { Name = request.Name.Trim(), Description = request.Description?.Trim(), Price = request.Price,
            StockQuantity = request.StockQuantity, CategoryId = request.CategoryId, Category = null!, IsActive = true,
            CreatedAtUtc = now, UpdatedAtUtc = now };
        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ProductWriteStatus.Success, await GetForWrite(product.Id, cancellationToken));
    }

    public async Task<ProductWriteResult> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FindAsync([id], cancellationToken);
        if (product is null) return new(ProductWriteStatus.NotFound);
        if (!await IsActiveCategory(request.CategoryId, cancellationToken)) return new(ProductWriteStatus.InvalidCategory);
        product.Name = request.Name.Trim();
        product.Description = request.Description?.Trim();
        product.Price = request.Price;
        product.StockQuantity = request.StockQuantity;
        product.CategoryId = request.CategoryId;
        product.IsActive = request.IsActive;
        product.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(ProductWriteStatus.Success, await GetForWrite(id, cancellationToken));
    }

    public async Task<ProductWriteStatus> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FindAsync([id], cancellationToken);
        if (product is null) return ProductWriteStatus.NotFound;
        if (product.IsActive)
        {
            product.IsActive = false;
            product.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return ProductWriteStatus.Success;
    }

    private Task<bool> IsActiveCategory(int id, CancellationToken ct) =>
        dbContext.Categories.AnyAsync(x => x.Id == id && x.IsActive, ct);
    private Task<ProductResponse?> GetForWrite(int id, CancellationToken ct) => dbContext.Products.AsNoTracking()
        .Where(x => x.Id == id).Select(ToResponse).SingleOrDefaultAsync(ct);
    private static readonly System.Linq.Expressions.Expression<Func<Product, ProductResponse>> ToResponse = x =>
        new(x.Id, x.Name, x.Description, x.Price, x.StockQuantity, x.CategoryId, x.Category.Name,
            x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static IQueryable<Product> ApplySort(IQueryable<Product> query, string sortBy, bool desc) =>
        sortBy.ToLowerInvariant() switch
        {
            "name" => desc ? query.OrderByDescending(x => x.Name).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Name).ThenBy(x => x.Id),
            "price" => desc ? query.OrderByDescending(x => x.Price).ThenByDescending(x => x.Id) : query.OrderBy(x => x.Price).ThenBy(x => x.Id),
            "stock" => desc ? query.OrderByDescending(x => x.StockQuantity).ThenByDescending(x => x.Id) : query.OrderBy(x => x.StockQuantity).ThenBy(x => x.Id),
            _ => desc ? query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id) : query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id)
        };
}
