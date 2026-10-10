using Kazuko.Core.Authentication;

namespace Kazuko.Core.Warehouse;

public static class InventoryScopes
{
    public const string RawMaterial = "RAW_MATERIAL";
    public const string FinishedGoods = "FINISHED_GOODS";
    public const string SalesFloor = "SALES_FLOOR";
}

public sealed record WarehouseLocationOption(
    long Id,
    long BranchId,
    string Code,
    string Name,
    string LocationType,
    string InventoryScope = InventoryScopes.RawMaterial)
{
    public string DisplayName => $"{Code} - {Name}";
    public override string ToString() => DisplayName;
}

public sealed record MaterialOption(
    long Id,
    string Code,
    string Name,
    string MaterialType,
    string PurityCode,
    string UnitCode,
    bool IsActive,
    decimal ReferenceBuyPricePerChi = 0,
    string? StoneKind = null,
    string? StoneShape = null,
    decimal? CaratPerPiece = null,
    decimal? SizeMm = null)
{
    public string DisplayName => $"{Code} - {Name}";
    public override string ToString() => DisplayName;
}

public sealed record MaterialBalanceItem(
    long MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialType,
    string PurityCode,
    string UnitCode,
    long LocationId,
    string LocationCode,
    string LocationName,
    decimal Quantity,
    decimal AverageCost,
    decimal StockValue,
    decimal MinimumStock,
    bool IsLowStock);

public sealed record FinishedProductStockItem(
    long Id,
    string Code,
    string ProductName,
    string ProductGroupCode,
    string ProductGroupName,
    string GoldPurity,
    string UnitCode,
    decimal Quantity,
    decimal StockValue,
    long? LocationId,
    string LocationName,
    string StatusCode);

public sealed record SaleItemStockItem(
    long Id,
    string Code,
    string ProductName,
    string ProductGroupCode,
    string ProductGroupName,
    string GoldPurity,
    string UnitCode,
    decimal Quantity,
    decimal StockValue,
    long? LocationId,
    string LocationName,
    string StatusCode);

public sealed record WarehouseSalesOrderOption(
    long Id,
    string Code,
    string CustomerName,
    string StatusCode,
    string OrderType = "")
{
    public string DisplayName =>
        string.IsNullOrWhiteSpace(OrderType)
            ? $"{Code} - {CustomerName} ({StatusCode})"
            : $"{Code} - {CustomerName} ({OrderType}/{StatusCode})";
    public override string ToString() => DisplayName;
}

public sealed record StockDocumentSummary(
    long Id,
    string Code,
    DateTime DocumentDate,
    string DocumentType,
    string StatusCode,
    string SourceLocationName,
    string DestinationLocationName,
    string ReferenceNo,
    string CounterpartyName,
    long? SupplierId,
    int LineCount,
    decimal TotalQuantity,
    decimal TotalAmount,
    string CreatedByName,
    DateTime CreatedAt,
    string PostedByName,
    DateTime? PostedAt);

public sealed record StockDocumentDetail(
    long Id,
    string Code,
    DateTime DocumentDate,
    string DocumentType,
    string StatusCode,
    long? SourceLocationId,
    long? DestinationLocationId,
    long? ProductionOrderId,
    long? SupplierId,
    string PurposeCode,
    string ReferenceNo,
    string CounterpartyName,
    string Notes,
    string CreatedByName,
    DateTime CreatedAt,
    string PostedByName,
    DateTime? PostedAt,
    string CancellationReason,
    string SourceType = "");

public sealed record StockDocumentLine(
    long Id,
    int LineNumber,
    long MaterialId,
    long? ProductionMaterialPlanId,
    string MaterialCode,
    string MaterialName,
    string MaterialType,
    string PurityCode,
    string UnitCode,
    decimal Quantity,
    decimal UnitCost,
    decimal LineAmount,
    string LotNo,
    string Notes);

public sealed record StockMovementItem(
    long Id,
    DateTime OccurredAt,
    string DocumentCode,
    string DocumentType,
    string MaterialCode,
    string MaterialName,
    string LocationName,
    string MovementType,
    decimal QuantityChange,
    decimal UnitCost,
    decimal ValueChange,
    string CreatedByName);

public sealed record MaterialBalanceSearchFilter(
    string SearchText,
    string MaterialType,
    long LocationId,
    bool LowStockOnly);

public sealed record FinishedProductSearchFilter(
    string SearchText,
    string ProductGroupCode,
    string GoldPurity,
    string StatusCode);

public sealed record SaleItemSearchFilter(
    string SearchText,
    string ProductGroupCode,
    string GoldPurity,
    string StatusCode);

public sealed record FinishedProductGroupOption(long Id, string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record StockDocumentSearchFilter(
    string SearchText,
    string DocumentType,
    string StatusCode,
    DateTime? FromDate,
    DateTime? ToDate);

public sealed record StockMovementSearchFilter(
    long MaterialId,
    long LocationId,
    string SearchText);

public interface IWarehouseRepository
{
    Task<IReadOnlyList<FinishedProductGroupOption>> GetFinishedProductGroupsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WarehouseLocationOption>> GetLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialOption>> SearchMaterialsAsync(
        string searchText,
        string materialType,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MaterialBalanceItem>> SearchMaterialBalancesAsync(
        MaterialBalanceSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinishedProductStockItem>> SearchFinishedProductStockAsync(
        FinishedProductSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SaleItemStockItem>> SearchSaleItemStockAsync(
        SaleItemSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WarehouseSalesOrderOption>> SearchIssueSalesOrdersAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinishedProductStockItem>> SearchIssueCandidatesAsync(
        long salesOrderId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockDocumentSummary>> SearchDocumentsAsync(
        StockDocumentSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<StockDocumentDetail?> GetDocumentAsync(
        long documentId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockDocumentLine>> GetDocumentLinesAsync(
        long documentId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovementItem>> SearchMovementsAsync(
        StockMovementSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
