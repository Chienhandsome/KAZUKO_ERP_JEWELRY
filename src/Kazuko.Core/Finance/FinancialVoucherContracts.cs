using Kazuko.Core.Authentication;

namespace Kazuko.Core.Finance;

public static class FinancialVoucherTypes
{
    public const string In = "IN";
    public const string Out = "OUT";
}

public static class FinancialVoucherStatuses
{
    public const string Posted = "POSTED";
    public const string Cancelled = "CANCELLED";
}

/// <summary>
/// Idempotency key types for CASH_VOUCHERS.SOURCE_ALLOCATION_*.
/// One allocation row must never create two vouchers.
/// </summary>
public static class FinancialVoucherSourceAllocationTypes
{
    public const string StockDocumentPayment = "STOCK_DOCUMENT_PAYMENT";
    public const string SettlementAllocation = "SETTLEMENT_ALLOCATION";
    public const string PayableDebtPaymentAllocation = "PAYABLE_DEBT_PAY_ALLOC";
}

public sealed record FinancialVoucherPostRequest(
    PaymentAllocationDraft Allocation,
    string SourceDocumentType,
    long SourceDocumentId,
    string SourceAllocationType,
    long SourceAllocationId,
    string? Notes = null);

public sealed record FinancialVoucherPostResult(
    long VoucherId,
    string Code,
    string VoucherType,
    bool AlreadyExisted);

/// <summary>
/// WPF-neutral posting of phiếu thu (IN) / phiếu chi (OUT) into CASH_VOUCHERS.
/// Standalone methods open their own connection/transaction.
/// Callers already inside a business transaction must use the Infrastructure
/// in-transaction overloads so work shares the ambient Firebird transaction.
/// </summary>
public interface IFinancialVoucherPostingService
{
    Task<FinancialVoucherPostResult> PostReceiptAsync(
        FinancialVoucherPostRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<FinancialVoucherPostResult> PostPaymentAsync(
        FinancialVoucherPostRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
