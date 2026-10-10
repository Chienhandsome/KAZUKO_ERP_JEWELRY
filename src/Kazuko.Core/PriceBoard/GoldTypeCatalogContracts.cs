using Kazuko.Core.Authentication;

namespace Kazuko.Core.PriceBoard;

public static class GoldTypeCatalogPermissions
{
    public const string Write = "priceboard.goldtype.write";
}

public sealed record GoldTypeOption(
    long Id,
    string Code,
    string Name,
    decimal PurityRate,
    bool IsActive)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Code : $"{Code} - {Name}";
    public override string ToString() => DisplayName;
}

public sealed record GoldTypeDetail(
    long Id,
    string Code,
    string Name,
    decimal PurityRate,
    int DisplayOrder,
    bool IsActive,
    long RetailGoldPriceId,
    decimal BuyPricePerChi,
    decimal SellPricePerChi,
    long WarehouseGoldPriceId,
    decimal WarehouseBuyPricePerChi,
    decimal WarehouseSellPricePerChi,
    long MaterialId);

public sealed record GoldTypeUpsertRequest(
    string Code,
    string Name,
    decimal PurityRate,
    decimal BuyPricePerChi,
    decimal SellPricePerChi,
    int? DisplayOrder = null);

/// <summary>Suy ra tỷ lệ tuổi từ mã loại vàng (750 → 0.750, 9999 → 0.9999).</summary>
public static class GoldTypePurity
{
    public static decimal InferFromCode(string? code)
    {
        var digits = new string((code ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 0 || !decimal.TryParse(digits, out var value) || value <= 0)
            return 1m;

        return digits.Length switch
        {
            >= 4 => Math.Round(value / 10_000m, 4, MidpointRounding.AwayFromZero),
            3 => Math.Round(value / 1_000m, 4, MidpointRounding.AwayFromZero),
            2 => Math.Round(value / 100m, 4, MidpointRounding.AwayFromZero),
            _ => Math.Round(value / 10m, 4, MidpointRounding.AwayFromZero)
        };
    }

    public static decimal Resolve(GoldTypeUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.PurityRate > 0
            ? request.PurityRate
            : InferFromCode(request.Code);
    }
}

public static class GoldTypeCatalogValidator
{
    public static IReadOnlyList<string> Validate(GoldTypeUpsertRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Code))
            errors.Add("Mã loại vàng bắt buộc.");
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("Tên loại vàng bắt buộc.");
        var purity = GoldTypePurity.Resolve(request);
        if (purity <= 0 || purity > 1)
            errors.Add("Không suy ra được tuổi vàng hợp lệ từ mã (ví dụ 750, 9999).");
        if (request.BuyPricePerChi <= 0)
            errors.Add("Giá mua bán lẻ / chỉ phải lớn hơn 0.");
        if (request.SellPricePerChi <= 0)
            errors.Add("Giá bán lẻ / chỉ phải lớn hơn 0.");
        return errors;
    }
}

public interface IGoldTypeCatalogService
{
    Task<IReadOnlyList<GoldTypeOption>> ListAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<GoldTypeDetail?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default);

    Task<long> CreateAsync(
        GoldTypeUpsertRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        long id,
        GoldTypeUpsertRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task DeactivateAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

/// <summary>UI tạo loại vàng (dialog). Implement ở module PriceBoard.</summary>
public interface IGoldTypeEditorService
{
    Task<GoldTypeOption?> ShowCreateAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
