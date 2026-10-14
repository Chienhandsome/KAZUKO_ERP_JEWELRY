using Kazuko.Core.Authentication;

namespace Kazuko.Core.Production;

public static class ProductionPermissions
{
    public const string OrderRead = "production.order.read";
    public const string OrderCreate = "production.order.create";
    public const string OrderEdit = "production.order.edit";
    public const string MaterialIssue = "production.material.issue";
    public const string ReturnPost = "production.return.post";
    public const string QcWrite = "production.qc.write";
    public const string OrderComplete = "production.order.complete";
    public const string OrderCancel = "production.order.cancel";
    public const string DocumentPrint = "production.document.print";
}

public static class ProductionOrderSources
{
    public const string Stock = "STOCK";
    public const string SalesOrder = "SALES_ORDER";
}

public static class ProductionExecutionTypes
{
    public const string InHouse = "IN_HOUSE";
    public const string Outsourced = "OUTSOURCED";
}

public static class ProductionOrderStatuses
{
    public const string Draft = "DRAFT";
    public const string InProgress = "IN_PROGRESS";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";

    public static string ToDisplay(string? statusCode) =>
        (statusCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            Draft => "Bản nháp",
            InProgress => "Đang làm",
            Completed => "Hoàn thành",
            Cancelled => "Đã hủy",
            _ => statusCode ?? string.Empty
        };

    public static string ToListDisplay(string? statusCode) =>
        (statusCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            Draft => "Bản nháp",
            InProgress => "Đang làm",
            Completed => "Hoàn thành",
            Cancelled => "Đã hủy",
            _ => statusCode ?? string.Empty
        };
}

public static class ProductionAttachmentKinds
{
    public const string Design = "DESIGN";
    public const string CustomerRequirement = "CUSTOMER_REQUIREMENT";
}

public static class ProductionQcUnitStatuses
{
    public const string Pending = "PENDING";
    public const string ReworkRequired = "REWORK_REQUIRED";
    public const string Passed = "PASSED";

    public static string ToDisplay(string? statusCode) =>
        (statusCode ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            Pending => "Chưa QC",
            ReworkRequired => "Không đạt - cần làm lại",
            Passed => "Đạt",
            _ => statusCode ?? string.Empty
        };
}

public static class ProductionQcResultCodes
{
    public const string Pass = "PASS";
    public const string Fail = "FAIL";
}

public static class ProductionComponentKinds
{
    public const string Gold = "GOLD";
    public const string Stone = "STONE";
    public const string Accessory = "ACCESSORY";
    public const string Other = "OTHER";
}

public static class ProductionStockDocumentTypes
{
    public const string MaterialIssue = "PRODUCTION_MATERIAL_ISSUE";
    public const string ReturnReceipt = "PRODUCTION_RETURN_RECEIPT";
    public const string FinishedReceipt = "PRODUCTION_FINISHED_RECEIPT";
}

public static class ProductionStockPurposes
{
    public const string MaterialDelivery = "PRODUCTION_MATERIAL_DELIVERY";
    public const string Return = "PRODUCTION_RETURN";
    public const string FinishedReceipt = "PRODUCTION_FINISHED_RECEIPT";
}

public sealed record ProductionOrderListFilter(
    string? SearchText,
    string? StatusCode,
    string? OrderSource,
    string? ExecutionType,
    DateTime? FromDate,
    DateTime? ToDate,
    bool IncludeDrafts = true,
    // Redesign 14/10: filter riêng theo target UI
    string? OrderCode = null,
    string? CustomerName = null,
    string? SalesOrderCode = null,
    string? WorkerName = null,
    string? ModelName = null);

// M1 redesign (Phase 2 decisions): KPI count trên TOÀN BỘ (B2), không theo kỳ;
// thẻ "Hoàn thành" đổi sang đếm toàn bộ; thêm "Đã hủy"; bỏ 2 thẻ thợ ngoài/nội bộ (A1).
public sealed record ProductionOrderSummary(
    int InProgressCount,
    int CompletedCount,
    int CancelledCount);

// M1 redesign: thêm Ngày giao (= STOCK_DOCUMENTS.DOCUMENT_DATE qua
// MATERIAL_ISSUE_DOCUMENT_ID), Hạn hoàn thành (PRODUCTION_ORDERS.DUE_DATE, migration 62),
// Khách hàng (qua SALES_ORDER_ID → CUSTOMERS).
public sealed record ProductionOrderListItem(
    long Id,
    string Code,
    string OrderSource,
    string ExecutionType,
    string StatusCode,
    DateTime OrderDate,
    DateTime? MaterialIssueDate,
    DateTime? DueDate,
    string? SalesOrderCode,
    string? CustomerName,
    string? WorkerDisplayName,
    string ModelSummary,
    int PlannedQuantityTotal,
    long RowVersion);

public sealed record ProductionFinishedReceiptOrderOption(long Id, string Code);

public sealed record ProductionOrderLineDto(
    long Id,
    int LineNumber,
    string? ModelCode,
    string ModelName,
    string GoldTypeCode,
    string GoldTypeName,
    int PlannedQuantity,
    decimal LaborCostPerUnit,
    decimal EstimatedWeightChi,
    decimal GoldUnitPricePerChi,
    decimal StoneAmountPerUnitVnd,
    string? Description,
    string? Notes);

public sealed record ProductionOrderWorkerDto(
    long EmployeeId,
    string EmployeeCode,
    string FullName);

public sealed record ProductionAttachmentDto(
    long Id,
    string AttachmentKind,
    string StoredPath,
    string OriginalFileName,
    string? MimeType,
    string? Caption,
    int DisplayOrder,
    int? LineNumber);

public sealed record ProductionMaterialBalanceDto(
    long Id,
    long IssueDocumentId,
    long IssueLineId,
    long MaterialId,
    string MaterialCode,
    string MaterialName,
    string MaterialType,
    string UnitCode,
    decimal QuantityIssued,
    decimal QuantityInProduct,
    decimal QuantityReturned,
    decimal QuantityScrap,
    decimal QuantityLoss,
    decimal UnitCostSnapshot,
    long? WarehouseGoldPriceId,
    decimal? WarehouseGoldPricePerChi);

public sealed record ProductionQcUnitDto(
    long Id,
    long OrderLineId,
    int UnitNumber,
    string StatusCode,
    string ModelName,
    decimal? ActualGoldWeight,
    string? ActualWeightUnitCode,
    string? GoldPurityCode,
    decimal LaborCostSnapshot,
    string? DefectNotes,
    DateTime? LastQcAt,
    long? FinishedProductId,
    long RowVersion);

public sealed record ProductionQcAttemptDto(
    long Id,
    long QcUnitId,
    int AttemptNumber,
    string ResultCode,
    decimal? ActualGoldWeight,
    string? ActualWeightUnitCode,
    string? GoldPurityCode,
    string? DefectNotes,
    DateTime InspectedAt,
    string? InspectedByName,
    DateTime? ReturnedForReworkAt,
    DateTime? ReworkReceivedAt,
    string? ReworkNotes);

public sealed record ProductionUnitComponentDto(
    long Id,
    long QcUnitId,
    long MaterialBalanceId,
    long MaterialId,
    string ComponentKind,
    decimal QuantityUsed,
    string UnitCode,
    decimal UnitCostSnapshot,
    decimal AmountVnd,
    long? WarehouseGoldPriceId,
    decimal? GoldPricePerChiSnapshot);

/// <summary>
/// Phân bổ cấu thành theo balance: lượng vào TP, đã dùng bởi món Đạt, còn lại cho món đang QC.
/// </summary>
public sealed record ProductionMaterialBalanceAllocationDto(
    long MaterialBalanceId,
    decimal QuantityInProduct,
    decimal QuantityUsedByPassed,
    decimal QuantityRemaining);

public sealed record ProductionOrderDetail(
    long Id,
    string Code,
    string OrderSource,
    long? SalesOrderId,
    string? SalesOrderCode,
    string ExecutionType,
    long? ExternalWorkerId,
    string? ExternalWorkerName,
    string StatusCode,
    DateTime OrderDate,
    DateTime? DueDate,
    long SourceLocationId,
    string SourceLocationName,
    long FinishedLocationId,
    string FinishedLocationName,
    long? MaterialIssueDocumentId,
    string? MaterialIssueDocumentCode,
    DateTime? MaterialIssueDocumentDate,
    long? ReturnDocumentId,
    string? ReturnDocumentCode,
    long? FinishedReceiptDocumentId,
    string? FinishedReceiptDocumentCode,
    string? Notes,
    long RowVersion,
    IReadOnlyList<ProductionOrderLineDto> Lines,
    IReadOnlyList<ProductionOrderWorkerDto> Workers,
    IReadOnlyList<ProductionAttachmentDto> Attachments,
    IReadOnlyList<ProductionMaterialBalanceDto> MaterialBalances,
    IReadOnlyList<ProductionQcUnitDto> QcUnits);

public sealed record ProductionSalesOrderOption(
    long Id,
    string Code,
    string CustomerName,
    string StatusCode,
    DateTime OrderDate);

public sealed record ProductionEmployeeOption(
    long Id,
    string Code,
    string FullName);

public sealed record ProductionExternalWorkerOption(
    long Id,
    string Code,
    string Name);

public sealed record ProductionLocationOption(
    long Id,
    string Code,
    string Name,
    string InventoryScope);

public sealed record ProductionMaterialStockOption(
    long MaterialId,
    string Code,
    string Name,
    string MaterialType,
    string UnitCode,
    string? PurityCode,
    decimal QuantityOnHand,
    decimal AverageCost,
    long LocationId);

public sealed record ProductionOrderLineDraft(
    long? Id,
    int LineNumber,
    string? ModelCode,
    string ModelName,
    string GoldTypeCode,
    string GoldTypeName,
    int PlannedQuantity,
    decimal LaborCostPerUnit,
    decimal EstimatedWeightChi,
    decimal GoldUnitPricePerChi,
    decimal StoneAmountPerUnitVnd,
    string? Description,
    string? Notes);

public sealed record ProductionAttachmentDraft(
    long? Id,
    string AttachmentKind,
    string? StagingPath,
    string? StoredPath,
    string OriginalFileName,
    string? MimeType,
    string? Caption,
    int DisplayOrder,
    int? LineNumber = null,
    bool MarkDeleted = false);

public sealed record ProductionOrderDraft(
    long? Id,
    string? Code,
    string OrderSource,
    long? SalesOrderId,
    string ExecutionType,
    long? ExternalWorkerId,
    DateTime OrderDate,
    DateTime? DueDate,
    long SourceLocationId,
    long FinishedLocationId,
    string? Notes,
    long? RowVersion,
    IReadOnlyList<long> EmployeeIds,
    IReadOnlyList<ProductionOrderLineDraft> Lines,
    IReadOnlyList<ProductionAttachmentDraft> Attachments);

public sealed record ProductionOrderSaveResult(
    long Id,
    string Code,
    long RowVersion);

public sealed record ProductionMaterialIssueLineDraft(
    long MaterialId,
    decimal Quantity,
    string? Notes);

public sealed record ProductionMaterialIssueDraft(
    long ProductionOrderId,
    long ExpectedRowVersion,
    DateTime DocumentDate,
    string? DeliveredByName,
    string? ReceivedByName,
    string? Notes,
    IReadOnlyList<ProductionMaterialIssueLineDraft> Lines);

public sealed record ProductionMaterialIssueResult(
    long DocumentId,
    string DocumentCode,
    long ProductionOrderId,
    long RowVersion);

public sealed record ProductionReturnBalanceLineDraft(
    long MaterialBalanceId,
    decimal QuantityInProduct,
    decimal QuantityReturned,
    decimal QuantityScrap,
    decimal QuantityLoss);

public sealed record ProductionWorkerReturnDraft(
    long ProductionOrderId,
    long ExpectedRowVersion,
    DateTime DocumentDate,
    string? DeliveredByName,
    string? ReceivedByName,
    string? Notes,
    IReadOnlyList<ProductionReturnBalanceLineDraft> BalanceLines);

public sealed record ProductionWorkerReturnResult(
    long DocumentId,
    string DocumentCode,
    int QcUnitCount,
    long RowVersion);

public sealed record ProductionQcComponentDraft(
    long MaterialBalanceId,
    string ComponentKind,
    decimal QuantityUsed);

public sealed record ProductionQcImageDraft(
    string StagingPath,
    string OriginalFileName,
    string? MimeType,
    string? Caption,
    int DisplayOrder);

public sealed record ProductionQcDraft(
    long QcUnitId,
    long ExpectedRowVersion,
    string ResultCode,
    decimal? ActualGoldWeight,
    string? ActualWeightUnitCode,
    string? GoldPurityCode,
    string? DefectNotes,
    IReadOnlyList<ProductionQcComponentDraft> Components,
    IReadOnlyList<ProductionQcImageDraft> Images,
    bool IsAdjustment = false);

public sealed record ProductionReworkReturnDraft(
    long QcUnitId,
    long ExpectedRowVersion,
    string? ReworkNotes);

public sealed record ProductionCompleteResult(
    long ProductionOrderId,
    string Code,
    long? FinishedReceiptDocumentId,
    string? FinishedReceiptDocumentCode,
    int FinishedProductCount,
    long? PayableDebtId,
    decimal TotalLaborCostVnd,
    long RowVersion);

public sealed record ProductionCancelResult(
    long ProductionOrderId,
    string StatusCode,
    long RowVersion);

public interface IProductionQueryRepository
{
    Task<ProductionOrderSummary> GetSummaryAsync(
        long branchId,
        DateTime? periodFrom,
        DateTime? periodTo,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionOrderListItem>> SearchOrdersAsync(
        long branchId,
        ProductionOrderListFilter filter,
        CancellationToken cancellationToken = default);

    Task<ProductionOrderDetail?> GetOrderDetailAsync(
        long orderId,
        long branchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionFinishedReceiptOrderOption>> GetCompletedUnreceivedOrdersAsync(
        long branchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionSalesOrderOption>> SearchEligibleSalesOrdersAsync(
        long branchId,
        string? searchText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionEmployeeOption>> SearchWorkingEmployeesAsync(
        string? searchText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionExternalWorkerOption>> SearchActiveExternalWorkersAsync(
        string? searchText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionLocationOption>> GetLocationsAsync(
        long branchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionMaterialStockOption>> SearchMaterialStockAsync(
        long branchId,
        long locationId,
        string? searchText,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionUnitComponentDto>> GetQcUnitComponentsAsync(
        long qcUnitId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductionMaterialBalanceAllocationDto>> GetMaterialBalanceAllocationsAsync(
        long productionOrderId,
        long? excludeQcUnitId = null,
        CancellationToken cancellationToken = default);
}

public interface IProductionApplicationService
{
    Task<ProductionOrderSaveResult> SaveDraftAsync(
        ProductionOrderDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ProductionMaterialIssueResult> PostMaterialIssueAsync(
        ProductionMaterialIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ProductionWorkerReturnResult> PostWorkerReturnAsync(
        ProductionWorkerReturnDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task RecordQcAsync(
        ProductionQcDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task RecordReworkReturnAsync(
        ProductionReworkReturnDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ProductionCompleteResult> CompleteAsync(
        long productionOrderId,
        long expectedRowVersion,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ProductionCompleteResult> PostFinishedReceiptAsync(
        long productionOrderId,
        long expectedRowVersion,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ProductionCancelResult> CancelAsync(
        long productionOrderId,
        long expectedRowVersion,
        string? reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IProductionAttachmentStore
{
    string ManagedRoot { get; }

    /// <summary>
    /// Import file vào staging. <paramref name="attachmentKind"/> quyết định allow-list:
    /// CUSTOMER_REQUIREMENT = ảnh mẫu; DESIGN = ảnh hoặc file thiết kế.
    /// </summary>
    Task<string> ImportToStagingAsync(
        string sourceFilePath,
        string attachmentKind = ProductionAttachmentKinds.CustomerRequirement,
        CancellationToken cancellationToken = default);

    Task<string> FinalizeAsync(
        string stagingPath,
        string orderCode,
        CancellationToken cancellationToken = default);

    void TryDeleteManagedFile(string? storedPath);

    void TryDeleteStagingFile(string? stagingPath);
}

/// <summary>
/// Mở dialog lệnh sản xuất hoặc đơn đặt từ module khác.
/// </summary>
public interface IProductionOrderEditorService
{
    /// <param name="preferredOrderSource">SALES_ORDER mở form tạo đơn đặt; null/STOCK mở form lệnh sản xuất.</param>
    ProductionOrderEditResult? ShowCreate(string? preferredOrderSource = null);
}

public sealed record ProductionOrderEditResult(
    long? ProductionOrderId,
    string? Code,
    bool DialogConfirmed);

public interface IProductionPrintDataService
{
    Task<ProductionMaterialIssuePrintDto?> GetMaterialIssuePrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default);

    Task<ProductionReturnPrintDto?> GetReturnPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default);

    Task<ProductionQcPrintDto?> GetQcPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default);

    Task<ProductionFinishedReceiptPrintDto?> GetFinishedReceiptPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default);

    Task<ProductionGoldSettlementPrintDto?> GetGoldSettlementPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default);

    Task<ProductionOrderPrintDto?> GetOrderPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default);
}

public sealed record ProductionMaterialIssuePrintDto(
    string OrderCode,
    DateTime DocumentDate,
    string BranchName,
    string? DeliveredByName,
    string? ReceivedByName,
    string DocumentCode,
    IReadOnlyList<ProductionMaterialIssuePrintLine> Lines);

public sealed record ProductionOrderPrintDto(
    string OrderCode,
    DateTime OrderDate,
    DateTime? MaterialIssueDate,
    DateTime? DueDate,
    string StatusDisplay,
    string BranchName,
    string? CustomerName,
    string? SalesOrderCode,
    string? WorkerDisplayName,
    string? Notes,
    IReadOnlyList<ProductionOrderPrintLine> Lines);

public sealed record ProductionOrderPrintLine(
    int LineNumber,
    string ModelName,
    int PlannedQuantity,
    decimal? GoldWeight,
    string? GoldPurityCode,
    decimal LaborCost);

public sealed record ProductionMaterialIssuePrintLine(
    int LineNumber,
    string MaterialCode,
    string MaterialName,
    decimal Quantity,
    string UnitCode,
    decimal UnitCost,
    string? Notes);

public sealed record ProductionReturnPrintDto(
    string OrderCode,
    DateTime DocumentDate,
    string BranchName,
    string? DeliveredByName,
    string? ReceivedByName,
    string DocumentCode,
    IReadOnlyList<ProductionReturnPrintLine> Lines);

public sealed record ProductionReturnPrintLine(
    string MaterialCode,
    string MaterialName,
    decimal QuantityIssued,
    decimal QuantityInProduct,
    decimal QuantityReturned,
    decimal QuantityScrap,
    decimal QuantityLoss,
    string UnitCode);

public sealed record ProductionQcPrintDto(
    string OrderCode,
    DateTime PrintedAt,
    string BranchName,
    IReadOnlyList<ProductionQcPrintUnit> Units);

public sealed record ProductionQcPrintUnit(
    string ModelName,
    int UnitNumber,
    string StatusDisplay,
    decimal? ActualGoldWeight,
    string? WeightUnit,
    string? GoldPurityCode,
    decimal LaborCost,
    string? DefectNotes);

public sealed record ProductionFinishedReceiptPrintDto(
    string OrderCode,
    DateTime DocumentDate,
    string BranchName,
    string DocumentCode,
    string FinishedLocationName,
    IReadOnlyList<ProductionFinishedReceiptPrintLine> Lines);

public sealed record ProductionFinishedReceiptPrintLine(
    string SerialCode,
    string ModelName,
    decimal GoldWeight,
    decimal MaterialCost,
    decimal LaborCost,
    decimal NonGoldCost,
    decimal TotalCost);

public sealed record ProductionGoldSettlementPrintDto(
    string OrderCode,
    DateTime PrintedAt,
    string BranchName,
    IReadOnlyList<ProductionReturnPrintLine> BalanceLines,
    decimal TotalLaborCostVnd,
    bool CreatesPayableDebt);
