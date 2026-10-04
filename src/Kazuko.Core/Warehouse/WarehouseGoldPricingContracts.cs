using Kazuko.Core.Authentication;

namespace Kazuko.Core.Warehouse;

public sealed record WarehouseGoldPriceItem(
    long Id,
    long GoldPriceId,
    string GoldCode,
    string GoldName,
    decimal PurityRate,
    decimal IssuePricePerChi,
    decimal RetailBuyPricePerChi,
    decimal RetailSellPricePerChi,
    long RowVersion,
    string UpdatedByName,
    DateTime UpdatedAt);

public sealed record WarehouseGoldPriceUpdate(
    long Id,
    decimal IssuePricePerChi,
    long RowVersion);

public interface IWarehouseGoldPriceRepository
{
    Task<IReadOnlyList<WarehouseGoldPriceItem>> GetAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        IReadOnlyList<WarehouseGoldPriceUpdate> updates,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public static class WarehouseIssuePricing
{
    public static decimal CalculateFinishedProductIssueValue(
        decimal goldWeightChi,
        decimal goldPricePerChi,
        decimal laborCost,
        decimal nonGoldMaterialCost)
    {
        if (goldWeightChi < 0 || goldPricePerChi < 0 || laborCost < 0 || nonGoldMaterialCost < 0)
            throw new ArgumentOutOfRangeException(nameof(goldWeightChi), "Các thành phần giá xuất kho không được âm.");

        return decimal.Round(
            goldWeightChi * goldPricePerChi + laborCost + nonGoldMaterialCost,
            2,
            MidpointRounding.AwayFromZero);
    }
}
