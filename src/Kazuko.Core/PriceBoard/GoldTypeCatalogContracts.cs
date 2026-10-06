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
    decimal IssuePricePerChi,
    long MaterialId);

public sealed record GoldTypeUpsertRequest(
    string Code,
    string Name,
    decimal PurityRate,
    decimal BuyPricePerChi,
    decimal SellPricePerChi,
    decimal IssuePricePerChi,
    int? DisplayOrder = null);

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
        if (request.PurityRate < 0 || request.PurityRate > 1)
            errors.Add("Hàm lượng (tỷ lệ) phải trong khoảng 0–1 (ví dụ 0.75).");
        if (request.BuyPricePerChi <= 0)
            errors.Add("Giá mua bán lẻ / chỉ phải lớn hơn 0.");
        if (request.SellPricePerChi <= 0)
            errors.Add("Giá bán lẻ / chỉ phải lớn hơn 0.");
        if (request.IssuePricePerChi <= 0)
            errors.Add("Giá kho / chỉ phải lớn hơn 0.");
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
