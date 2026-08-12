using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Categories;
using ECommerceOrderManagementApi.Entities;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderManagementApi.Services;

public sealed class CategoryService(AppDbContext dbContext, TimeProvider timeProvider) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.Categories.AsNoTracking().Where(x => x.IsActive)
            .OrderBy(x => x.Name).ThenBy(x => x.Id).Select(ToResponse).ToListAsync(cancellationToken);

    public Task<CategoryResponse?> GetAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Categories.AsNoTracking().Where(x => x.Id == id && x.IsActive)
            .Select(ToResponse).SingleOrDefaultAsync(cancellationToken);

    public async Task<CategoryWriteResult> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var normalizedName = NormalizeName(name);
        if (await dbContext.Categories.AnyAsync(x => x.NormalizedName == normalizedName, cancellationToken))
            return new(CategoryWriteStatus.Duplicate);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var category = new Category { Name = name, NormalizedName = normalizedName,
            Description = TrimOptional(request.Description), IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        dbContext.Categories.Add(category);
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(category).State = EntityState.Detached;
            if (await dbContext.Categories.AnyAsync(x => x.NormalizedName == normalizedName, cancellationToken))
                return new(CategoryWriteStatus.Duplicate);
            throw;
        }
        return new(CategoryWriteStatus.Success, Map(category));
    }

    public async Task<CategoryWriteResult> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.FindAsync([id], cancellationToken);
        if (category is null) return new(CategoryWriteStatus.NotFound);
        var name = request.Name.Trim();
        var normalizedName = NormalizeName(name);
        if (await dbContext.Categories.AnyAsync(x => x.Id != id && x.NormalizedName == normalizedName, cancellationToken))
            return new(CategoryWriteStatus.Duplicate);
        if (!request.IsActive && category.IsActive && await dbContext.Products.AnyAsync(x => x.CategoryId == id && x.IsActive, cancellationToken))
            return new(CategoryWriteStatus.ActiveProductsPreventDeactivation);
        category.Name = name;
        category.NormalizedName = normalizedName;
        category.Description = TrimOptional(request.Description);
        category.IsActive = request.IsActive;
        category.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        try { await dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            dbContext.Entry(category).State = EntityState.Detached;
            if (await dbContext.Categories.AnyAsync(x => x.Id != id && x.NormalizedName == normalizedName, cancellationToken))
                return new(CategoryWriteStatus.Duplicate);
            throw;
        }
        return new(CategoryWriteStatus.Success, Map(category));
    }

    public async Task<CategoryWriteStatus> DeactivateAsync(int id, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.FindAsync([id], cancellationToken);
        if (category is null) return CategoryWriteStatus.NotFound;
        if (!category.IsActive) return CategoryWriteStatus.Success;
        if (await dbContext.Products.AnyAsync(x => x.CategoryId == id && x.IsActive, cancellationToken))
            return CategoryWriteStatus.ActiveProductsPreventDeactivation;
        category.IsActive = false;
        category.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        await dbContext.SaveChangesAsync(cancellationToken);
        return CategoryWriteStatus.Success;
    }

    internal static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
    private static string? TrimOptional(string? value) => value?.Trim();
    private static CategoryResponse Map(Category x) => new(x.Id, x.Name, x.Description, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static readonly System.Linq.Expressions.Expression<Func<Category, CategoryResponse>> ToResponse =
        x => new(x.Id, x.Name, x.Description, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc);
    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is SqlException { Number: 2601 or 2627 };
}
