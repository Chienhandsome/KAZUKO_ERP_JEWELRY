using Kazuko.Core.Authentication;

namespace Kazuko.Core.Warehouse;

public static class StockTransferDocumentTypes
{
    public const string Transfer = "TRANSFER";
}

public static class FinishedToSalesPurposes
{
    public const string FinishedToSales = "FINISHED_TO_SALES";
}

public static class SalesIntakeRequestStatuses
{
    public const string Pending = "PENDING";
    public const string Approved = "APPROVED";
    public const string Rejected = "REJECTED";
}

public sealed record StockTransferLineDraft(
    long MaterialId,
    decimal Quantity,
    string? Notes);

public sealed record StockTransferDraft(
    DateTime DocumentDate,
    long SourceLocationId,
    long DestinationLocationId,
    string? Notes,
    IReadOnlyList<StockTransferLineDraft> Lines);

public sealed record FinishedProductTransferDraft(
    DateTime DocumentDate,
    long SourceLocationId,
    long DestinationLocationId,
    string? Notes,
    IReadOnlyList<long> FinishedProductIds);

public sealed record SalesIntakeRequestSummary(
    long Id,
    string Code,
    DateTime DocumentDate,
    string StatusCode,
    string SourceLocationName,
    string DestinationLocationName,
    string CreatedByName,
    DateTime CreatedAt,
    int LineCount,
    string Notes,
    string RejectionReason);

public sealed record SalesIntakeRequestLine(
    long FinishedProductId,
    string ProductCode,
    string ProductName,
    string GoldPurity,
    decimal UnitCost);

public sealed record SalesIntakeRequestDetail(
    long Id,
    string Code,
    DateTime DocumentDate,
    string StatusCode,
    long SourceLocationId,
    string SourceLocationName,
    long DestinationLocationId,
    string DestinationLocationName,
    string CreatedByName,
    DateTime CreatedAt,
    string Notes,
    string RejectionReason,
    IReadOnlyList<SalesIntakeRequestLine> Lines);

public sealed record StockAdjustmentLineDraft(
    long MaterialId,
    decimal QuantityChange,
    string? Notes);

public sealed record StockAdjustmentDraft(
    DateTime DocumentDate,
    long LocationId,
    bool IsIncrease,
    string Reason,
    string? Notes,
    IReadOnlyList<StockAdjustmentLineDraft> Lines);

public interface IStockTransferService
{
    Task<long> CreateAndPostAsync(
        StockTransferDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IFinishedProductTransferService
{
    /// <summary>Tạo yêu cầu TP→BH (chưa lập phiếu chuyển kho, chưa chuyển tồn).</summary>
    Task<long> CreateRequestAsync(
        FinishedProductTransferDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>BH đồng ý: mới lập phiếu TRANSFER và ghi sổ.</summary>
    Task ApproveAsync(
        long requestId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>BH từ chối: hủy yêu cầu + lý do; không tạo phiếu chuyển kho.</summary>
    Task RejectAsync(
        long requestId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesIntakeRequestSummary>> ListRequestsAsync(
        string? statusCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<SalesIntakeRequestDetail?> GetRequestDetailAsync(
        long requestId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IStockAdjustmentService
{
    Task<long> CreateAndPostAsync(
        StockAdjustmentDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
