using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesOldGoldPurchaseDetailByTypeReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesOldGoldPurchaseDetailByTypeReportRow(
    string GoldType,
    DateTime PurchaseDate,
    string DocumentCode,
    decimal GoldWeight,
    decimal UnitPrice,
    decimal Amount);

public sealed record SalesOldGoldPurchaseDetailByTypeReportResult(
    IReadOnlyList<SalesOldGoldPurchaseDetailByTypeReportRow> Rows,
    int LineCount,
    decimal TotalGoldWeight,
    decimal TotalAmount);

public interface ISalesOldGoldPurchaseDetailByTypeReportService
{
    Task<SalesOldGoldPurchaseDetailByTypeReportResult> GetAsync(
        SalesOldGoldPurchaseDetailByTypeReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
