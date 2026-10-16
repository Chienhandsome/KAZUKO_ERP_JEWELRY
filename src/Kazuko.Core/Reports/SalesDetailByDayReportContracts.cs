using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesDetailByDayReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesDetailByDayReportRow(
    DateTime OrderDate,
    string OrderCode,
    string ProductCode,
    string ProductName,
    string GoldType,
    decimal GoldWeight,
    decimal Amount);

public sealed record SalesDetailByDayReportResult(
    IReadOnlyList<SalesDetailByDayReportRow> Rows,
    int ItemCount,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public interface ISalesDetailByDayReportService
{
    Task<SalesDetailByDayReportResult> GetAsync(
        SalesDetailByDayReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
