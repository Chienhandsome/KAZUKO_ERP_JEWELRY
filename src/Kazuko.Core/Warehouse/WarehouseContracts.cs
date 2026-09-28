using Kazuko.Core.Authentication;

namespace Kazuko.Core.Warehouse;

public sealed record WarehouseLocationOption(
    long Id,
    long BranchId,
    string Code,
    string Name,
    string LocationType)
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
    bool IsActive)
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
    string CancellationReason);

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
