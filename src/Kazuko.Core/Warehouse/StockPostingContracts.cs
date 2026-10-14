using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Core.Warehouse;

public static class WarehouseWritePermissions
{
    public const string ReceiptWrite = "warehouse.receipt.write";
    public const string ReceiptPost = "warehouse.receipt.post";
    public const string IssueWrite = "warehouse.issue.write";
    public const string IssuePost = "warehouse.issue.post";
    public const string TransferWrite = "warehouse.transfer.write";
    public const string TransferPost = "warehouse.transfer.post";
    public const string CraftOutsource = "warehouse.craft.outsource";
    public const string CraftSettleOverride = "warehouse.craft.settle.override";
    public const string FinishedIssue = "warehouse.finished.issue";
    public const string DocumentReverse = "warehouse.document.reverse";
    public const string WorkerManage = "warehouse.worker.manage";
    public const string SalesIntakeApprove = "warehouse.sales_intake.approve";
}

public static class StockDocumentTypes
{
    public const string Receipt = "RECEIPT";
    public const string Issue = "ISSUE";
    public const string Transfer = "TRANSFER";
    public const string OutsourcedCraftIssue = "OUTSOURCED_CRAFT_ISSUE";
    public const string CraftReturnReceipt = "CRAFT_RETURN_RECEIPT";
    public const string FinishedGoodsIssue = "FINISHED_GOODS_ISSUE";
    public const string StockAdjustment = "STOCK_ADJUSTMENT";
}

public static class StockSourceTypes
{
    public const string Supplier = "SUPPLIER";
    public const string OldGoldCustomer = "OLD_GOLD_CUSTOMER";
}

public static class StockDocumentStatuses
{
    public const string Draft = "DRAFT";
    public const string Posted = "POSTED";
    public const string Cancelled = "CANCELLED";
}

public static class HoldingStatuses
{
    public const string InProgress = "IN_PROGRESS";
    public const string PartialReturn = "PARTIAL_RETURN";
    public const string Settled = "SETTLED";
    public const string Variance = "VARIANCE";
}

public sealed record MaterialReceiptLineDraft(
    long MaterialId,
    decimal Quantity,
    decimal UnitPrice,
    string CurrencyCode,
    decimal ExchangeRate,
    string? Notes);

public sealed record MaterialReceiptDraft(
    DateTime DocumentDate,
    long DestinationLocationId,
    string SourceType,
    long? SupplierId,
    long? CustomerId,
    string? Notes,
    IReadOnlyList<MaterialReceiptLineDraft> Lines,
    decimal PaidAmountVnd,
    IReadOnlyList<PaymentAllocationDraft> Payments);

public sealed record MaterialReceiptPostResult(
    long DocumentId,
    string DocumentCode,
    decimal TotalAmountVnd,
    decimal PaidAmountVnd,
    decimal DebtAmountVnd,
    long? PayableDebtId);

public sealed record MaterialIssueLineDraft(
    long MaterialId,
    decimal Quantity,
    string? Notes);

public sealed record MaterialIssueDraft(
    DateTime DocumentDate,
    long SourceLocationId,
    string ReceivedByName,
    string? Notes,
    IReadOnlyList<MaterialIssueLineDraft> Lines);

public sealed record ExternalWorkerListItem(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Address,
    string IdTaxNumber,
    string PaymentInfo,
    string Notes,
    bool IsActive);

public sealed record ExternalWorkerSaveRequest(
    long? Id,
    string Code,
    string Name,
    string Phone,
    string Address,
    string IdTaxNumber,
    string PaymentInfo,
    string Notes,
    bool IsActive);

public sealed record OutsourcedCraftIssueLineDraft(
    long MaterialId,
    decimal Quantity,
    string? Notes);

public sealed record OutsourcedCraftIssueDraft(
    DateTime DocumentDate,
    long SourceLocationId,
    long ExternalWorkerId,
    DateTime? ExpectedReturnDate,
    string DeliveredByName,
    string ReceivedByName,
    long? ProductionOrderId,
    string? CraftInstructions,
    string? Notes,
    IReadOnlyList<OutsourcedCraftIssueLineDraft> Lines);

public sealed record CraftSettlementLineDraft(
    long HoldingId,
    decimal QuantityInProduct,
    decimal QuantityReturned,
    decimal QuantityScrap,
    decimal AllowedLoss,
    string? Notes,
    string? VarianceResponsibility = null,
    decimal CompensationQty = 0,
    decimal CompensationUnitPriceVnd = 0,
    decimal CompensationAmountVnd = 0,
    string? CompensationReason = null);

public sealed record CraftOutputLineDraft(
    long? ProductionOrderId,
    long? SalesOrderId,
    long? FinishedProductId,
    string? ProductCode,
    string? ProductName,
    decimal Quantity,
    decimal Weight,
    string? GoldPurity,
    long DestinationLocationId,
    decimal MaterialValueVnd,
    decimal AllocatedLaborCostVnd,
    decimal TotalCostVnd,
    string? Notes);

public sealed record CraftReturnReceiptDraft(
    long SourceIssueDocumentId,
    DateTime DocumentDate,
    long DestinationLocationId,
    string DeliveredByName,
    string ReceivedByName,
    decimal LaborAmountVnd,
    decimal PaidLaborAmountVnd,
    IReadOnlyList<PaymentAllocationDraft> LaborPayments,
    string? Notes,
    IReadOnlyList<CraftSettlementLineDraft> SettlementLines,
    IReadOnlyList<CraftOutputLineDraft> OutputLines,
    bool AllowOverReturn);

public sealed record FinishedGoodsIssueLineDraft(
    long FinishedProductId);

public sealed record FinishedGoodsIssueDraft(
    long SalesOrderId,
    DateTime DocumentDate,
    long SourceLocationId,
    string DeliveredByName,
    string ReceivedByName,
    string? Notes,
    IReadOnlyList<FinishedGoodsIssueLineDraft> Lines);

public sealed record OutsourcedHoldingRow(
    long HoldingId,
    long IssueDocumentId,
    string IssueDocumentCode,
    long ExternalWorkerId,
    string WorkerCode,
    string WorkerName,
    long MaterialId,
    string MaterialCode,
    string MaterialName,
    string UnitCode,
    decimal QuantityIssued,
    decimal QuantityReturned,
    decimal QuantityInProduct,
    decimal QuantityScrap,
    decimal QuantityLoss,
    decimal QuantityOutstanding,
    decimal LaborAmountVnd,
    decimal LaborDebtRemainingVnd,
    string StatusCode);

public sealed record ExternalWorkerEditResult(
    long Id,
    string Code,
    string Name,
    bool IsActive);

public interface IExternalWorkerEditorService
{
    /// <summary>Mở dialog danh mục thợ ngoài (thêm/sửa). Trả về thợ vừa lưu hoặc đang chọn khi đóng.</summary>
    ExternalWorkerEditResult? Show();
}

public interface IExternalWorkerRepository
{
    Task<IReadOnlyList<ExternalWorkerListItem>> SearchAsync(
        string searchText,
        bool activeOnly,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ExternalWorkerListItem?> GetByIdAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<ExternalWorkerListItem> SaveAsync(
        ExternalWorkerSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IMaterialReceiptService
{
    Task<MaterialReceiptPostResult> CreateDraftAndPostAsync(
        MaterialReceiptDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IMaterialIssueService
{
    Task<long> CreateAndPostAsync(
        MaterialIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IOutsourcedCraftService
{
    Task<long> CreateAndPostIssueAsync(
        OutsourcedCraftIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<long> CreateAndPostReturnAsync(
        CraftReturnReceiptDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OutsourcedHoldingRow>> SearchHoldingsAsync(
        long? externalWorkerId,
        string statusCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IFinishedGoodsIssueService
{
    Task<long> CreateAndPostAsync(
        FinishedGoodsIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task ReverseAsync(
        long documentId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
