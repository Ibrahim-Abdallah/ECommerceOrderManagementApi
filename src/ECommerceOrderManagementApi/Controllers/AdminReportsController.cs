using ECommerceOrderManagementApi.DTOs.Reports;
using ECommerceOrderManagementApi.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ECommerceOrderManagementApi.Controllers;

[ApiController, Route("api/admin/reports"), Authorize(Roles = "Admin")]
public sealed class AdminReportsController(IReportingService reportingService) : ControllerBase
{
    [HttpGet("sales-summary"), EndpointSummary("Summarize recognized sales")]
    public async Task<ActionResult<SalesSummaryResponse>> GetSalesSummary(
        [FromQuery] SalesSummaryQueryParameters query,
        IValidator<SalesSummaryQueryParameters> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
            return BadRequest(new ValidationProblemDetails(validation.ToDictionary()));

        return Ok(await reportingService.GetSalesSummaryAsync(query, cancellationToken));
    }
}
