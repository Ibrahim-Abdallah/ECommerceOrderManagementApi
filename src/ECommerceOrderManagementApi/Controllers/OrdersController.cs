using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.DTOs;
using FluentValidation;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/orders"), Authorize(Roles = "Customer")]
public sealed class OrdersController(IOrderService service) : ControllerBase
{
    [HttpGet, EndpointSummary("List the current customer's orders")]
    public async Task<ActionResult<PagedResponse<OrderSummaryResponse>>> GetAll([FromQuery] CustomerOrderQueryParameters query,
        IValidator<CustomerOrderQueryParameters> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await service.GetCustomerOrdersAsync(query, ct);
        return result is null ? Unauthorized() : Ok(result);
    }

    [HttpGet("{id:int}"), EndpointSummary("Get one of the current customer's orders")]
    public async Task<ActionResult<OrderDetailsResponse>> Get(int id, CancellationToken ct)
    { var order = await service.GetCustomerOrderAsync(id, ct); return order is null ? NotFound() : Ok(order); }

    [HttpPost, EndpointSummary("Checkout the current customer's cart")]
    public async Task<ActionResult<OrderDetailsResponse>> Checkout(CancellationToken cancellationToken)
    {
        var result = await service.CheckoutAsync(cancellationToken);
        return result.Status switch
        {
            CheckoutStatus.Success => StatusCode(StatusCodes.Status201Created, result.Order),
            CheckoutStatus.MissingUser => Unauthorized(),
            CheckoutStatus.EmptyCart => ConflictProblem("Empty cart", "The cart does not contain any items to checkout."),
            CheckoutStatus.ProductUnavailable => ConflictProblem("Cart contains unavailable product", "One or more products in the cart are no longer available."),
            CheckoutStatus.InsufficientStock => ConflictProblem("Insufficient stock", "One or more products no longer have sufficient stock to complete checkout."),
            CheckoutStatus.CartChanged => ConflictProblem("Cart changed", "The cart changed while checkout was being processed. Review the cart and try again."),
            _ => throw new InvalidOperationException("Unknown checkout status.")
        };
    }

    [HttpPost("{id:int}/cancel"), EndpointSummary("Cancel a pending order")]
    public async Task<ActionResult<OrderDetailsResponse>> Cancel(int id, CancellationToken ct)
    {
        var result = await service.CancelAsync(id, ct);
        return result.Status switch
        {
            OrderMutationStatus.Success => Ok(result.Order),
            OrderMutationStatus.MissingUser => Unauthorized(),
            OrderMutationStatus.NotFound => NotFound(),
            OrderMutationStatus.Conflict => ConflictProblem("Order cannot be cancelled", "Only pending orders can be cancelled."),
            _ => throw new InvalidOperationException("Unknown cancellation status.")
        };
    }

    private ConflictObjectResult ConflictProblem(string title, string detail) =>
        Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title, Detail = detail });
}
