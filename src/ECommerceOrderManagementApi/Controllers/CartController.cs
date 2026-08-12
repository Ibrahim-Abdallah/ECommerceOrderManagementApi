using ECommerceOrderManagementApi.DTOs.Cart;
using ECommerceOrderManagementApi.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/cart"), Authorize(Roles = "Customer")]
public sealed class CartController(ICartService service) : ControllerBase
{
    [HttpGet, EndpointSummary("Get the current customer's cart")]
    public async Task<ActionResult<CartResponse>> Get(CancellationToken ct) => MapRead(await service.GetAsync(ct));

    [HttpPost("items"), EndpointSummary("Add a product to the current customer's cart")]
    public async Task<ActionResult<CartResponse>> Add(AddCartItemRequest request, IValidator<AddCartItemRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await service.AddItemAsync(request, ct);
        return result.Status == CartOperationStatus.Success
            ? StatusCode(StatusCodes.Status201Created, result.Cart)
            : MapRead(result);
    }

    [HttpPut("items/{productId:int}"), EndpointSummary("Replace a cart item's quantity")]
    public async Task<ActionResult<CartResponse>> Update(int productId, UpdateCartItemRequest request,
        IValidator<UpdateCartItemRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        return MapRead(await service.UpdateItemAsync(productId, request, ct));
    }

    [HttpDelete("items/{productId:int}"), EndpointSummary("Remove a product from the current customer's cart")]
    public async Task<IActionResult> Remove(int productId, CancellationToken ct) =>
        await service.RemoveItemAsync(productId, ct) switch
        {
            CartOperationStatus.Success => NoContent(),
            CartOperationStatus.MissingUser => Unauthorized(),
            _ => NotFound()
        };

    [HttpDelete, EndpointSummary("Clear the current customer's cart")]
    public async Task<IActionResult> Clear(CancellationToken ct) =>
        await service.ClearAsync(ct) == CartOperationStatus.MissingUser ? Unauthorized() : NoContent();

    private ActionResult<CartResponse> MapRead(CartOperationResult result) => result.Status switch
    {
        CartOperationStatus.Success => Ok(result.Cart),
        CartOperationStatus.MissingUser => Unauthorized(),
        CartOperationStatus.DuplicateProduct => ConflictProblem("Product already in cart",
            "The product is already in the cart. Use the quantity update endpoint to change its quantity."),
        CartOperationStatus.InsufficientStock => ConflictProblem("Insufficient stock",
            "The requested quantity exceeds the currently available stock."),
        _ => NotFound()
    };

    private ConflictObjectResult ConflictProblem(string title, string detail) =>
        Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title, Detail = detail });
}
