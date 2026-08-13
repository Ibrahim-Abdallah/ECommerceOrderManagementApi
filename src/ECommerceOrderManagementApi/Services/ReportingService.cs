using Dapper;
using ECommerceOrderManagementApi.Data;
using ECommerceOrderManagementApi.DTOs.Reports;
using ECommerceOrderManagementApi.Enums;
using ECommerceOrderManagementApi.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace ECommerceOrderManagementApi.Services;

public sealed class ReportingService(AppDbContext dbContext) : IReportingService
{
    private const string SalesSummarySql = """
        SELECT
            COUNT(*) AS TotalOrders,
            COALESCE(SUM(o.TotalAmount), 0) AS TotalRevenue
        FROM Orders AS o
        WHERE o.Status IN (@ConfirmedStatus, @ShippedStatus, @DeliveredStatus)
          AND (@FromDate IS NULL OR o.CreatedAtUtc >= @FromDate)
          AND (@ToDate IS NULL OR o.CreatedAtUtc <= @ToDate);

        SELECT
            oi.ProductId,
            p.Name AS ProductName,
            SUM(oi.Quantity) AS QuantitySold,
            SUM(oi.TotalPrice) AS Revenue
        FROM OrderItems AS oi
        INNER JOIN Orders AS o ON o.Id = oi.OrderId
        INNER JOIN Products AS p ON p.Id = oi.ProductId
        WHERE o.Status IN (@ConfirmedStatus, @ShippedStatus, @DeliveredStatus)
          AND (@FromDate IS NULL OR o.CreatedAtUtc >= @FromDate)
          AND (@ToDate IS NULL OR o.CreatedAtUtc <= @ToDate)
        GROUP BY oi.ProductId, p.Name
        ORDER BY SUM(oi.Quantity) DESC, SUM(oi.TotalPrice) DESC, oi.ProductId ASC;
        """;

    public async Task<SalesSummaryResponse> GetSalesSummaryAsync(
        SalesSummaryQueryParameters query,
        CancellationToken cancellationToken)
    {
        var parameters = new
        {
            ConfirmedStatus = (int)OrderStatus.Confirmed,
            ShippedStatus = (int)OrderStatus.Shipped,
            DeliveredStatus = (int)OrderStatus.Delivered,
            query.FromDate,
            query.ToDate
        };
        var command = new CommandDefinition(
            SalesSummarySql,
            parameters,
            cancellationToken: cancellationToken);

        var connection = dbContext.Database.GetDbConnection();
        using var results = await connection.QueryMultipleAsync(command);
        var totalsRow = await results.ReadSingleAsync();
        var totalOrders = Convert.ToInt32(totalsRow.TotalOrders);
        var totalRevenue = Convert.ToDecimal(totalsRow.TotalRevenue);
        var productRows = await results.ReadAsync();
        var topProducts = productRows.Take(5).Select(row => new TopSellingProductResponse(
            Convert.ToInt32(row.ProductId),
            Convert.ToString(row.ProductName)!,
            Convert.ToInt64(row.QuantitySold),
            Convert.ToDecimal(row.Revenue))).ToArray();
        var averageOrderValue = totalOrders == 0
            ? 0m
            : Math.Round(totalRevenue / totalOrders, 2, MidpointRounding.AwayFromZero);

        return new SalesSummaryResponse(
            totalOrders,
            totalRevenue,
            averageOrderValue,
            topProducts);
    }
}
