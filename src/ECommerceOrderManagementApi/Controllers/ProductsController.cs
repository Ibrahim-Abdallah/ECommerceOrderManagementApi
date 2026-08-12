using ECommerceOrderManagementApi.DTOs;
using ECommerceOrderManagementApi.DTOs.Products;
using ECommerceOrderManagementApi.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/products")]
public sealed class ProductsController(IProductService service) : ControllerBase
{
    [HttpGet, AllowAnonymous, EndpointSummary("Browse active products")]
    public async Task<ActionResult<PagedResponse<ProductResponse>>> GetAll([FromQuery] ProductQueryParameters query,
        IValidator<ProductQueryParameters> validator, CancellationToken ct)
    { var validation = await validator.ValidateAsync(query, ct); return validation.IsValid ? Ok(await service.GetAllAsync(query, ct)) : BadRequest(new ValidationProblemDetails(validation.ToDictionary())); }

    [HttpGet("{id:int}"), AllowAnonymous, EndpointSummary("Get an active product")]
    public async Task<ActionResult<ProductResponse>> Get(int id, CancellationToken ct)
    { var item = await service.GetAsync(id, ct); return item is null ? NotFound() : Ok(item); }

    [HttpPost, Authorize(Roles = "Admin"), EndpointSummary("Create a product")]
    public async Task<ActionResult<ProductResponse>> Create(CreateProductRequest request, IValidator<CreateProductRequest> validator, CancellationToken ct)
    { var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
      var result = await service.CreateAsync(request, ct); return result.Status == ProductWriteStatus.InvalidCategory ? InvalidCategory() : CreatedAtAction(nameof(Get), new { id = result.Product!.Id }, result.Product); }

    [HttpPut("{id:int}"), Authorize(Roles = "Admin"), EndpointSummary("Update or reactivate a product")]
    public async Task<ActionResult<ProductResponse>> Update(int id, UpdateProductRequest request, IValidator<UpdateProductRequest> validator, CancellationToken ct)
    { var validation = await validator.ValidateAsync(request, ct); if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
      var result = await service.UpdateAsync(id, request, ct); return result.Status switch { ProductWriteStatus.NotFound => NotFound(), ProductWriteStatus.InvalidCategory => InvalidCategory(), _ => Ok(result.Product) }; }

    [HttpDelete("{id:int}"), Authorize(Roles = "Admin"), EndpointSummary("Safely deactivate a product")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct) => await service.DeactivateAsync(id, ct) == ProductWriteStatus.NotFound ? NotFound() : NoContent();

    private BadRequestObjectResult InvalidCategory() => BadRequest(new ProblemDetails { Status = 400, Title = "Invalid category", Detail = "The selected category is invalid or inactive." });
}
