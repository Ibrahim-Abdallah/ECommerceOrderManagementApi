using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/orders"), Authorize(Roles = "Customer")]
public sealed class OrdersController(IOrderService service) : ControllerBase
{
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

    private ConflictObjectResult ConflictProblem(string title, string detail) =>
        Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title, Detail = detail });
}
