using ECommerceOrderManagementApi.DTOs.Categories;

namespace ECommerceOrderManagementApi.Interfaces;

public enum CategoryWriteStatus { Success, NotFound, Duplicate, ActiveProductsPreventDeactivation }
public sealed record CategoryWriteResult(CategoryWriteStatus Status, CategoryResponse? Category = null);

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryResponse>> GetAllAsync(CancellationToken cancellationToken);
    Task<CategoryResponse?> GetAsync(int id, CancellationToken cancellationToken);
    Task<CategoryWriteResult> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken);
    Task<CategoryWriteResult> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken);
    Task<CategoryWriteStatus> DeactivateAsync(int id, CancellationToken cancellationToken);
}
