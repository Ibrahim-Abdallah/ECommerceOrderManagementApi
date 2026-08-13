using ECommerceOrderManagementApi.DTOs;
using ECommerceOrderManagementApi.DTOs.Orders;
using ECommerceOrderManagementApi.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/admin/orders"), Authorize(Roles = "Admin")]
public sealed class AdminOrdersController(IOrderService service) : ControllerBase
{
    [HttpGet, EndpointSummary("List orders across customers")]
    public async Task<ActionResult<PagedResponse<AdminOrderSummaryResponse>>> GetAll([FromQuery] AdminOrderQueryParameters query,
        IValidator<AdminOrderQueryParameters> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(query, ct);
        return validation.IsValid ? Ok(await service.GetAdminOrdersAsync(query, ct))
            : BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
    }

    [HttpGet("{id:int}"), EndpointSummary("Get an order with customer context")]
    public async Task<ActionResult<AdminOrderDetailsResponse>> Get(int id, CancellationToken ct)
    { var order = await service.GetAdminOrderAsync(id, ct); return order is null ? NotFound() : Ok(order); }

    [HttpPatch("{id:int}/status"), EndpointSummary("Advance an order status")]
    public async Task<ActionResult<AdminOrderDetailsResponse>> UpdateStatus(int id, UpdateOrderStatusRequest request,
        IValidator<UpdateOrderStatusRequest> validator, CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid) return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));
        var result = await service.UpdateStatusAsync(id, request.Status, ct);
        return result.Status switch
        {
            OrderMutationStatus.Success => Ok(result.Order),
            OrderMutationStatus.NotFound => NotFound(),
            OrderMutationStatus.Conflict => ConflictProblem("Invalid order status transition", "The requested order status transition is not allowed."),
            OrderMutationStatus.ConcurrentChange => ConflictProblem("Order status changed", "The order status changed while the request was being processed. Review the order and try again."),
            _ => throw new InvalidOperationException("Unknown order update status.")
        };
    }

    private ConflictObjectResult ConflictProblem(string title, string detail) =>
        Conflict(new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = title, Detail = detail });
}
