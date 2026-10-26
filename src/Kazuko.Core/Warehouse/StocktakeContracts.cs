using Kazuko.Core.Authentication;

namespace Kazuko.Core.Warehouse;

public sealed record StocktakeSummary(long Id, string Code, DateTime CountDate, string LocationName,
    string Scope, string Status, int Revision, string CreatedByName);

public sealed record StocktakeLine(long Id, string ItemKey, string Kind, long? ItemId, string Code, string Name,
    string Unit, decimal SystemQuantity, decimal? CountedQuantity, string Notes, string Stamp, string ItemState, bool NeedsRecount = false)
{
    public decimal? Difference => CountedQuantity - SystemQuantity;
    public string ItemStateDisplay => ItemState switch {
        "IN_STOCK"=>"Trong kho", "DISPLAYED"=>"Đang trưng bày", "RESERVED"=>"Đã giữ hàng",
        "SOLD"=>"Đã bán", "WAITING_RECEIPT"=>"Chờ nhập kho", "SCRAPPED"=>"Đã loại",
        "UNKNOWN"=>"Chưa xác định", "ABSENT"=>"Không còn hàng", _=>ItemState };
    public string Finding => NeedsRecount ? "Cần kiểm lại" : Kind == "UNKNOWN" ? "Chưa xác định serial"
        : SystemQuantity == 0 && CountedQuantity > 0 ? "Ngoài danh sách dự kiến"
        : CountedQuantity is null ? "Chưa nhập"
        : Difference < 0 ? "Thiếu" : Difference > 0 ? "Thừa" : "Khớp";
}

public sealed record StocktakeDetail(long Id, string Code, DateTime CountDate, long LocationId, string LocationName,
    string Scope, string Status, int Revision, long InventoryRevision, string Notes, string CreatedByName,
    IReadOnlyList<StocktakeLine> Lines);
public sealed record StocktakeCreateRequest(Guid RequestId, DateTime CountDate, long LocationId, string Notes);
public sealed record StocktakeCountEntry(long LineId, decimal? Quantity, string Notes, bool ConfirmRecount = false);
public sealed record StocktakeSaveRequest(long Id, int Revision, string Notes, IReadOnlyList<StocktakeCountEntry> Lines);

public interface IStocktakeService
{
    Task<IReadOnlyList<WarehouseLocationOption>> GetLocationsAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StocktakeSummary>> ListAsync(AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<StocktakeDetail> GetAsync(long id, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<StocktakeDetail> CreateAsync(StocktakeCreateRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<StocktakeDetail> SaveAsync(StocktakeSaveRequest request, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<StocktakeDetail> ScanAsync(long id, int revision, string scan, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<StocktakeDetail> ReviewAsync(long id, int revision, AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<StocktakeDetail> CloseAsync(long id, int revision, AuthenticatedSession session, CancellationToken cancellationToken = default);
}

public static class StocktakeValidation
{
    public static void ValidateQuantity(string kind, decimal? quantity)
    {
        if (quantity is null) return;
        if (quantity < 0 || quantity > 99999999999999.9999m || decimal.Round(quantity.Value, 4) != quantity)
            throw new InvalidOperationException("Số thực tế phải không âm, tối đa 4 chữ số thập phân.");
        if (kind != "MATERIAL" && quantity is not (0m or 1m))
            throw new InvalidOperationException("Mỗi serial chỉ được ghi nhận 0 hoặc 1 món.");
    }
}
