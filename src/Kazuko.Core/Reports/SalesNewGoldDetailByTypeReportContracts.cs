using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesNewGoldDetailByTypeReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesNewGoldDetailByTypeReportRow(
    string GoldType,
    DateTime OrderDate,
    string OrderCode,
    string ProductCode,
    decimal GoldWeight,
    decimal Amount);

public sealed record SalesNewGoldDetailByTypeReportResult(
    IReadOnlyList<SalesNewGoldDetailByTypeReportRow> Rows,
    int ItemCount,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public interface ISalesNewGoldDetailByTypeReportService
{
    Task<SalesNewGoldDetailByTypeReportResult> GetAsync(
        SalesNewGoldDetailByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
