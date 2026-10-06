using Kazuko.Core.Authentication;

namespace Kazuko.Core.PriceBoard;

public static class PriceBoardPermissions
{
    public const string Read = "price-board.read";
    public const string Update = "price-board.update";
    public const string GoldTypeWrite = GoldTypeCatalogPermissions.Write;
}

public sealed record GoldUnitDefinition(
    string Code,
    string Name,
    decimal RatioToGram,
    decimal RatioToChi)
{
    public override string ToString() => Name;
}

public static class GoldUnitConverter
{
    public const decimal GramsPerChi = 3.75m;
    public const string DefaultUnitCode = "CHI";

    public static readonly IReadOnlyList<GoldUnitDefinition> Units =
    [
        new("CHI", "Chỉ", 3.75m, 1m),
        new("LUONG", "Lượng / Cây", 37.5m, 10m),
        new("PHAN", "Phân", 0.375m, 0.1m),
        new("LY", "Ly", 0.0375m, 0.01m),
        new("ZEM", "Zem", 0.00375m, 0.001m),
        new("GRAM", "Gram (g)", 1.0m, 1m / 3.75m)
    ];

    public static GoldUnitDefinition GetUnit(string code)
    {
        var normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
        return Units.FirstOrDefault(item => item.Code == normalized) ?? Units[0];
    }

    public static decimal ConvertFromChi(decimal pricePerChi, string targetUnitCode)
    {
        var unit = GetUnit(targetUnitCode);
        return Math.Round(pricePerChi * unit.RatioToChi, 2);
    }

    public static decimal ConvertToChi(decimal priceInUnit, string fromUnitCode)
    {
        var unit = GetUnit(fromUnitCode);
        if (unit.RatioToChi == 0)
            return 0;
        return Math.Round(priceInUnit / unit.RatioToChi, 2);
    }
}

public sealed record GoldPriceRecord(
    long Id,
    long CompanyId,
    string Code,
    string Name,
    decimal PurityRate,
    decimal BuyPricePerChi,
    decimal SellPricePerChi,
    decimal BuyPricePerGram,
    decimal SellPricePerGram,
    string CurrencyCode,
    int DisplayOrder,
    bool IsActive,
    string UpdatedBy,
    DateTime UpdatedAt)
{
    public decimal DisplayBuyPrice(string unitCode) =>
        GoldUnitConverter.ConvertFromChi(BuyPricePerChi, unitCode);

    public decimal DisplaySellPrice(string unitCode) =>
        GoldUnitConverter.ConvertFromChi(SellPricePerChi, unitCode);
}

public sealed record GoldPriceUpdateEntry(
    long Id,
    string Code,
    string Name,
    decimal BuyPriceInSelectedUnit,
    decimal SellPriceInSelectedUnit,
    string UnitCode);

public sealed record GoldPriceHistoryRecord(
    long Id,
    long GoldPriceId,
    string PriceCode,
    string GoldName,
    string UnitName,
    decimal OldBuyPrice,
    decimal NewBuyPrice,
    decimal OldSellPrice,
    decimal NewSellPrice,
    string ChangedBy,
    DateTime ChangedAt,
    string Notes);

public sealed record CurrencyRateRecord(
    long Id,
    string CurrencyCode,
    string CurrencyName,
    decimal BuyRate,
    decimal SellRate,
    bool IsActive,
    string UpdatedBy,
    DateTime UpdatedAt);

public interface IPriceBoardRepository
{
    Task<IReadOnlyList<GoldPriceRecord>> GetGoldPricesAsync(CancellationToken cancellationToken = default);

    Task SaveGoldPricesAsync(
        IReadOnlyList<GoldPriceUpdateEntry> updates,
        string note,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<long> AddCustomGoldTypeAsync(
        string code,
        string name,
        decimal purityRate,
        decimal buyPriceInUnit,
        decimal sellPriceInUnit,
        string unitCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task DeleteGoldPriceAsync(long id, AuthenticatedSession session, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GoldPriceHistoryRecord>> GetPriceHistoryAsync(
        long? goldPriceId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CurrencyRateRecord>> GetCurrencyRatesAsync(CancellationToken cancellationToken = default);

    Task SaveCurrencyRatesAsync(
        IReadOnlyList<CurrencyRateRecord> rates,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<long> AddCurrencyRateAsync(
        string code,
        string name,
        decimal buyRate,
        decimal sellRate,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task DeleteCurrencyRateAsync(long id, AuthenticatedSession session, CancellationToken cancellationToken = default);
}

public interface IPriceBoardDialogService
{
    void ShowPriceBoard();
}
