using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesNewGoldSummaryByTypeReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesNewGoldSummaryByTypeReportRow(
    string GoldType,
    int ItemCount,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public sealed record SalesNewGoldSummaryByTypeReportResult(
    IReadOnlyList<SalesNewGoldSummaryByTypeReportRow> Rows,
    int TotalItems,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public interface ISalesNewGoldSummaryByTypeReportService
{
    Task<SalesNewGoldSummaryByTypeReportResult> GetAsync(
        SalesNewGoldSummaryByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
