using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Core.Warehouse;

public static class StockTransferDocumentTypes
{
    public const string Transfer = "TRANSFER";
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
    Task<long> CreateAndPostAsync(
        FinishedProductTransferDraft draft,
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
