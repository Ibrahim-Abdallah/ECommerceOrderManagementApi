using ECommerceOrderManagementApi.DTOs.Categories;
using ECommerceOrderManagementApi.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/categories")]
public sealed class CategoriesController(ICategoryService service) : ControllerBase
{
    [HttpGet, AllowAnonymous, EndpointSummary("List active categories")]
    public async Task<ActionResult<IReadOnlyList<CategoryResponse>>> GetAll(CancellationToken ct) => Ok(await service.GetAllAsync(ct));

    [HttpGet("{id:int}"), AllowAnonymous, EndpointSummary("Get an active category")]
    public async Task<ActionResult<CategoryResponse>> Get(int id, CancellationToken ct)
    { var item = await service.GetAsync(id, ct); return item is null ? NotFound() : Ok(item); }

    [HttpPost, Authorize(Roles = "Admin"), EndpointSummary("Create a category")]
    public async Task<ActionResult<CategoryResponse>> Create(CreateCategoryRequest request, IValidator<CreateCategoryRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await service.CreateAsync(request, ct);
        return result.Status == CategoryWriteStatus.Duplicate ? Duplicate() : CreatedAtAction(nameof(Get), new { id = result.Category!.Id }, result.Category);
    }

    [HttpPut("{id:int}"), Authorize(Roles = "Admin"), EndpointSummary("Update or reactivate a category")]
    public async Task<ActionResult<CategoryResponse>> Update(int id, UpdateCategoryRequest request, IValidator<UpdateCategoryRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await service.UpdateAsync(id, request, ct);
        return result.Status switch { CategoryWriteStatus.NotFound => NotFound(), CategoryWriteStatus.Duplicate => Duplicate(),
            CategoryWriteStatus.ActiveProductsPreventDeactivation => ActiveProductsConflict(), _ => Ok(result.Category) };
    }

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin"), EndpointSummary("Safely deactivate a category")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => (await service.DeactivateAsync(id, ct)) switch
    { CategoryWriteStatus.NotFound => NotFound(), CategoryWriteStatus.ActiveProductsPreventDeactivation => ActiveProductsConflict(), _ => NoContent() };

    private ConflictObjectResult Duplicate() => Conflict(new ProblemDetails { Status = 409, Title = "Duplicate category", Detail = "A category with this name already exists." });
    private ConflictObjectResult ActiveProductsConflict() => Conflict(new ProblemDetails { Status = 409, Title = "Category has active products", Detail = "Deactivate or move active products before deactivating this category." });
}
