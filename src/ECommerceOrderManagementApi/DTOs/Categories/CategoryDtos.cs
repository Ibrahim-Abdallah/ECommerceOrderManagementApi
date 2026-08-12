namespace ECommerceOrderManagementApi.DTOs.Categories;

public sealed record CreateCategoryRequest(string Name, string? Description);
public sealed record UpdateCategoryRequest(string Name, string? Description, bool IsActive);
public sealed record CategoryResponse(int Id, string Name, string? Description, bool IsActive,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);
