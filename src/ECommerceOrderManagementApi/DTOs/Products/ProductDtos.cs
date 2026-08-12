namespace ECommerceOrderManagementApi.DTOs.Products;

public sealed record CreateProductRequest(string Name, string? Description, decimal Price,
    int StockQuantity, int CategoryId);
public sealed record UpdateProductRequest(string Name, string? Description, decimal Price,
    int StockQuantity, int CategoryId, bool IsActive);
public sealed record ProductResponse(int Id, string Name, string? Description, decimal Price,
    int StockQuantity, int CategoryId, string CategoryName, bool IsActive,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

public sealed class ProductQueryParameters
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Search { get; init; }
    public int? CategoryId { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public string SortBy { get; init; } = "createdAt";
    public string SortDirection { get; init; } = "desc";
}
