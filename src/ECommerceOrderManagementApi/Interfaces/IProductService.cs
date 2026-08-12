using ECommerceOrderManagementApi.DTOs;
using ECommerceOrderManagementApi.DTOs.Products;

namespace ECommerceOrderManagementApi.Interfaces;

public enum ProductWriteStatus { Success, NotFound, InvalidCategory }
public sealed record ProductWriteResult(ProductWriteStatus Status, ProductResponse? Product = null);

public interface IProductService
{
    Task<PagedResponse<ProductResponse>> GetAllAsync(ProductQueryParameters parameters, CancellationToken cancellationToken);
    Task<ProductResponse?> GetAsync(int id, CancellationToken cancellationToken);
    Task<ProductWriteResult> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);
    Task<ProductWriteResult> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken);
    Task<ProductWriteStatus> DeactivateAsync(int id, CancellationToken cancellationToken);
}
