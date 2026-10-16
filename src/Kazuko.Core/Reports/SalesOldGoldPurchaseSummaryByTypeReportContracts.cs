using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesOldGoldPurchaseSummaryByTypeReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesOldGoldPurchaseSummaryByTypeReportRow(
    string GoldType,
    int LineCount,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public sealed record SalesOldGoldPurchaseSummaryByTypeReportResult(
    IReadOnlyList<SalesOldGoldPurchaseSummaryByTypeReportRow> Rows,
    int TotalLines,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public interface ISalesOldGoldPurchaseSummaryByTypeReportService
{
    Task<SalesOldGoldPurchaseSummaryByTypeReportResult> GetAsync(
        SalesOldGoldPurchaseSummaryByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
