using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesTopItemsReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesTopItemsReportRow(
    string ProductCode,
    string ProductName,
    string GoldType,
    int QuantitySold,
    decimal Revenue);

public sealed record SalesTopItemsReportResult(
    IReadOnlyList<SalesTopItemsReportRow> Rows,
    int TotalQuantity,
    decimal TotalRevenue);

public interface ISalesTopItemsReportService
{
    Task<SalesTopItemsReportResult> GetAsync(
        SalesTopItemsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
