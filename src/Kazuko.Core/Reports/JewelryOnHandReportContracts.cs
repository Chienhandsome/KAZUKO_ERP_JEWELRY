using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record JewelryOnHandReportFilter(
    string SearchText,
    string GoldPurity,
    string StatusCode,
    string InventoryScope);

public sealed record JewelryOnHandReportRow(
    long Id,
    string Code,
    string ProductName,
    string GoldPurity,
    decimal GoldWeight,
    decimal TotalWeight,
    decimal StockValue,
    long? LocationId,
    string LocationName,
    string InventoryScope,
    string StatusCode);

public sealed record JewelryOnHandReportResult(
    IReadOnlyList<JewelryOnHandReportRow> Rows,
    int ItemCount,
    decimal TotalGoldWeight,
    decimal TotalStockValue);

public interface IJewelryOnHandReportService
{
    Task<JewelryOnHandReportResult> GetAsync(
        JewelryOnHandReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
