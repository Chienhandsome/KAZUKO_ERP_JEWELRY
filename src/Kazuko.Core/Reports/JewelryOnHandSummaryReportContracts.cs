using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record JewelryOnHandSummaryFilter(
    string GoldPurity,
    string StatusCode,
    string InventoryScope);

public sealed record JewelryOnHandSummaryRow(
    string InventoryScope,
    string GoldPurity,
    int ItemCount,
    decimal TotalGoldWeight,
    decimal TotalWeight,
    decimal TotalStockValue);

public sealed record JewelryOnHandSummaryResult(
    IReadOnlyList<JewelryOnHandSummaryRow> Rows,
    int GroupCount,
    int ItemCount,
    decimal TotalGoldWeight,
    decimal TotalStockValue);

public interface IJewelryOnHandSummaryReportService
{
    Task<JewelryOnHandSummaryResult> GetAsync(
        JewelryOnHandSummaryFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
