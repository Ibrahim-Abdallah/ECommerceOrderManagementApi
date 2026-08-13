namespace ECommerceOrderManagementApi.DTOs.Reports;

public sealed class SalesSummaryQueryParameters
{
    public DateTime? FromDate { get; init; }
    public DateTime? ToDate { get; init; }
}

public sealed record SalesSummaryResponse(
    int TotalOrders,
    decimal TotalRevenue,
    decimal AverageOrderValue,
    IReadOnlyList<TopSellingProductResponse> TopSellingProducts);

public sealed record TopSellingProductResponse(
    int ProductId,
    string ProductName,
    long QuantitySold,
    decimal Revenue);
