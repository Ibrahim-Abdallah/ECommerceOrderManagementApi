using ECommerceOrderManagementApi.DTOs.Reports;

namespace ECommerceOrderManagementApi.Interfaces;

public interface IReportingService
{
    Task<SalesSummaryResponse> GetSalesSummaryAsync(
        SalesSummaryQueryParameters query,
        CancellationToken cancellationToken);
}
