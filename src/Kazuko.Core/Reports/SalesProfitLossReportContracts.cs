using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesProfitLossReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesProfitLossReportRow(
    DateTime OrderDate,
    string OrderCode,
    string ProductName,
    string GoldType,
    decimal CostAmount,
    decimal SaleAmount,
    decimal Profit);

public sealed record SalesProfitLossReportResult(
    IReadOnlyList<SalesProfitLossReportRow> Rows,
    int ItemCount,
    decimal TotalCost,
    decimal TotalSale,
    decimal TotalProfit);

public interface ISalesProfitLossReportService
{
    Task<SalesProfitLossReportResult> GetAsync(
        SalesProfitLossReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
