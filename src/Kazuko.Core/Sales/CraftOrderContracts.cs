using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Core.Sales;

public static class SalesOrderTypes
{
    public const string Retail = "RETAIL";
    public const string Custom = "CUSTOM";
    public const string Wholesale = "WHOLESALE";
}

public static class CraftOrderStatuses
{
    public const string Draft = "DRAFT";
    public const string Confirmed = "CONFIRMED";
    public const string Deposited = "DEPOSITED";
    public const string InProduction = "IN_PRODUCTION";
    public const string Ready = "READY";
    public const string PartiallyDelivered = "PARTIALLY_DELIVERED";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";

    public static bool IsEligibleForProduction(string? statusCode)
    {
        var status = (statusCode ?? string.Empty).Trim().ToUpperInvariant();
        return status is Deposited or Confirmed;
    }
}

public static class CraftOrderPermissions
{
    public const string Create = "sales.craft_order.create";
}

public sealed record CraftOrderLineDraft(
    string ModelName,
    string GoldPurity,
    int Quantity,
    decimal EstimatedWeightChi,
    decimal GoldUnitPricePerChi,
    decimal LaborAmountPerUnitVnd,
    decimal StoneAmountPerUnitVnd,
    string? Notes,
    string? ModelCode = null,
    string? Description = null,
    IReadOnlyList<CraftOrderAttachmentDraft>? Attachments = null);

public sealed record CraftOrderAttachmentDraft(
    string AttachmentKind,
    string? StagingPath,
    string? StoredPath,
    string OriginalFileName,
    string? MimeType,
    string? Caption,
    int DisplayOrder);

public sealed record CraftOrderAttachmentItem(
    long Id,
    string AttachmentKind,
    string StoredPath,
    string OriginalFileName,
    string? MimeType,
    string? Caption,
    int DisplayOrder);

public sealed record CraftOrderDraft(
    long CustomerId,
    DateTime OrderDate,
    long? SalespersonId,
    IReadOnlyList<CraftOrderLineDraft> Lines,
    IReadOnlyList<PaymentAllocationDraft> DepositAllocations,
    string? Notes);

public sealed record CraftOrderCreateResult(
    long SalesOrderId,
    string Code,
    string StatusCode,
    decimal QuotedTotalVnd,
    decimal DepositAmountVnd,
    long? SettlementId,
    string? SettlementCode);

public sealed record CraftOrderHeader(
    long Id,
    string Code,
    string StatusCode,
    long CustomerId,
    string CustomerCode,
    string CustomerName,
    string? CustomerPhone,
    decimal GrandTotalVnd,
    decimal DepositPaidVnd,
    bool HasFinishedGoodsIssue)
{
    public decimal RemainingDueVnd => Math.Max(0m, GrandTotalVnd - DepositPaidVnd);

    public bool CanDeliver =>
        !HasFinishedGoodsIssue
        && string.Equals(StatusCode, CraftOrderStatuses.Ready, StringComparison.OrdinalIgnoreCase);

    public bool CanCollectRemaining =>
        RemainingDueVnd > 0
        && HasFinishedGoodsIssue
        && !string.Equals(StatusCode, CraftOrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(StatusCode, CraftOrderStatuses.Completed, StringComparison.OrdinalIgnoreCase);
}

public sealed record CraftOrderLineItem(
    int LineNumber,
    string ProductName,
    string GoldPurity,
    int Quantity,
    decimal EstimatedWeightChi,
    decimal GoldUnitPricePerChi,
    decimal LaborAmountPerUnitVnd,
    decimal StoneAmountPerUnitVnd,
    decimal LineTotalVnd,
    string? ModelCode,
    string? Description,
    string? Notes,
    IReadOnlyList<CraftOrderAttachmentItem> Attachments);

public sealed record CraftOrderListItem(
    long Id,
    string Code,
    DateTime OrderDate,
    string StatusCode,
    long CustomerId,
    string CustomerName,
    string? CustomerPhone,
    decimal GrandTotalVnd,
    decimal PaidAmountVnd,
    bool HasFinishedGoodsIssue)
{
    public decimal RemainingDueVnd => Math.Max(0m, GrandTotalVnd - PaidAmountVnd);

    public bool CanDeliver =>
        !HasFinishedGoodsIssue
        && string.Equals(StatusCode, CraftOrderStatuses.Ready, StringComparison.OrdinalIgnoreCase);

    public bool CanCollectRemaining =>
        RemainingDueVnd > 0
        && HasFinishedGoodsIssue
        && !string.Equals(StatusCode, CraftOrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(StatusCode, CraftOrderStatuses.Completed, StringComparison.OrdinalIgnoreCase);
}

public sealed record CraftOrderSearchFilter(
    DateTime? FromDate,
    DateTime? ToDate,
    string? SearchText,
    string? StatusCode);

public sealed record CraftOrderCollectDraft(
    long SalesOrderId,
    DateTime PaymentDate,
    IReadOnlyList<PaymentAllocationDraft> CollectionAllocations,
    decimal ReceivableAmountVnd,
    string? Notes);

public sealed record CraftOrderCollectResult(
    long SalesOrderId,
    string OrderCode,
    string StatusCode,
    decimal CollectedAmountVnd,
    decimal ReceivableAmountVnd,
    long? SettlementId,
    string? SettlementCode);

public sealed record CraftOrderDeliverDraft(
    long SalesOrderId,
    DateTime DocumentDate,
    long SourceLocationId,
    string DeliveredByName,
    string ReceivedByName,
    string? Notes,
    IReadOnlyList<long> FinishedProductIds,
    IReadOnlyList<PaymentAllocationDraft>? CollectionAllocations,
    decimal ReceivableAmountVnd);

public sealed record CraftOrderDeliverResult(
    long SalesOrderId,
    string OrderCode,
    string StatusCode,
    long IssueDocumentId,
    decimal CollectedAmountVnd,
    decimal ReceivableAmountVnd);

public sealed record CraftCustomerOption(
    long Id,
    string Code,
    string Name,
    string? Phone,
    bool IsWalkIn)
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(Phone)
            ? $"{Code} — {Name}"
            : $"{Code} — {Name} ({Phone})";
}

public interface ICraftOrderApplicationService
{
    Task<IReadOnlyList<CraftCustomerOption>> SearchCustomersAsync(
        string? searchText,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CraftOrderListItem>> SearchOrdersAsync(
        CraftOrderSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<CraftOrderHeader?> GetHeaderAsync(
        long salesOrderId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CraftOrderLineItem>> GetLinesAsync(
        long salesOrderId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<CraftOrderCreateResult> CreateWithDepositAsync(
        CraftOrderDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<CraftOrderCollectResult> CollectRemainingAsync(
        CraftOrderCollectDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Giao hàng đơn đặt: xuất TP + thu phần còn (nếu còn) trong một transaction → COMPLETED.
    /// </summary>
    Task<CraftOrderDeliverResult> DeliverAsync(
        CraftOrderDeliverDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
