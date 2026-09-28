using Kazuko.Core.Authentication;

namespace Kazuko.Core.Finance;

public static class DocumentReversalSourceTypes
{
    public const string StockDocument = "STOCK_DOCUMENT";
    public const string FinancialSettlement = "FINANCIAL_SETTLEMENT";
    public const string CashVoucher = "CASH_VOUCHER";
}

public static class PartyCreditKinds
{
    public const string SupplierReturnHold = "SUPPLIER_RETURN_HOLD";
    public const string CustomerReturnHold = "CUSTOMER_RETURN_HOLD";
    public const string ManualAdjust = "MANUAL_ADJUST";
}

public static class PartyCreditStatuses
{
    public const string Open = "OPEN";
    public const string Partial = "PARTIAL";
    public const string Used = "USED";
    public const string Cancelled = "CANCELLED";
}

public sealed record PartyCreditDraft(
    string PartyType,
    long PartyId,
    string CreditKind,
    decimal AmountVnd,
    string OriginDocumentType,
    long OriginDocumentId,
    string? Notes);

public sealed record PartyCreditResult(long CreditId, string Code);

/// <summary>
/// Credit giữ lại để cấn trừ sau — không tạo CASH_VOUCHERS / không ghi nhận tiền đã thu/đã chi.
/// </summary>
public interface IPartyCreditService
{
    Task<PartyCreditResult> CreateAsync(
        PartyCreditDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public sealed record DocumentReversalResult(
    long ReversalId,
    string SourceDocumentType,
    long SourceDocumentId,
    bool AlreadyReversed);

public interface IDocumentReversalService
{
    Task<DocumentReversalResult> ReverseStockDocumentAsync(
        long documentId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<DocumentReversalResult> ReverseSettlementAsync(
        long settlementId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
