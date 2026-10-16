using Kazuko.Core.Authentication;

namespace Kazuko.Core.Finance;

public static class FinancePermissions
{
    public const string FundRead = "finance.fund.read";
    public const string BankRead = "finance.bank.read";
}

public static class DebtPermissions
{
    public const string PayableRead = "debt.payable.read";
    public const string PayablePay = "debt.payable.pay";
    public const string ReceivableRead = "debt.receivable.read";
    public const string ReceivableCollect = "debt.receivable.collect";
}

public static class PaymentMethods
{
    public const string Cash = "CASH";
    public const string BankTransfer = "BANK_TRANSFER";
    public const string Card = "CARD";
    /// <summary>Phiếu thu/chi gộp nhiều phương thức (chỉ dùng cho voucher bulk, migration 74).</summary>
    public const string Mixed = "MIXED";
}

public static class PayablePartyTypes
{
    public const string Supplier = "SUPPLIER";
    public const string Customer = "CUSTOMER";
    public const string ExternalWorker = "EXTERNAL_WORKER";
}

public static class VarianceResponsibilities
{
    public const string WithinAllowance = "WITHIN_ALLOWANCE";
    public const string CompanyAccepted = "COMPANY_ACCEPTED";
    public const string WorkerCompensation = "WORKER_COMPENSATION";
}

public static class ReceivableDebtKinds
{
    public const string LossCompensation = "LOSS_COMPENSATION";
    public const string Sales = "SALES";
}

public static class ReceivableDebtStatuses
{
    public const string Open = "OPEN";
    public const string Partial = "PARTIAL";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
}

public sealed record ReceivableDebtCollectRequest(
    long DebtId,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    string? Notes);

public interface IReceivableDebtRepository
{
    Task CollectAsync(
        ReceivableDebtCollectRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}


public static class PayableDebtStatuses
{
    public const string Open = "OPEN";
    public const string Partial = "PARTIAL";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
}

public static class DebtDisplayStatuses
{
    public const string Normal = "NORMAL";
    public const string Overdue = "OVERDUE";
    public const string Paid = "PAID";
}

public sealed record CashFundOption(long Id, string Code, string Name, bool IsDefault)
{
    public string DisplayName => $"{Code} - {Name}";
    public override string ToString() => DisplayName;
}

public sealed record BankAccountOption(long Id, string Code, string BankName, string AccountNumber)
{
    public string DisplayName => $"{BankName} - {AccountNumber}";
    public override string ToString() => DisplayName;
}

public sealed record PaymentAllocationDraft(
    string PaymentMethod,
    decimal AmountVnd,
    long? CashFundId,
    long? BankAccountId,
    string? ReferenceNumber);

public sealed record PayableDebtListItem(
    long Id,
    string Code,
    string PartyType,
    string PartyName,
    string DebtKind,
    string OriginDocumentType,
    long OriginDocumentId,
    decimal OriginalAmountVnd,
    decimal PaidAmountVnd,
    decimal RemainingAmountVnd,
    string StatusCode,
    DateTime CreatedAt);

public sealed record PayableDebtSearchFilter(
    string SearchText,
    string StatusCode,
    string PartyType);

public sealed record PayableDebtPaymentRequest(
    long DebtId,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    string? Notes);

public sealed record DebtWorkbenchSummary(
    decimal CustomerDebtTotal,
    decimal CustomerCollectedTotal,
    decimal CustomerRemainingTotal,
    decimal SupplierDebtTotal,
    decimal SupplierPaidTotal,
    decimal SupplierRemainingTotal,
    decimal CustomerOverdueTotal,
    int CustomerOverdueCount,
    decimal SupplierOverdueTotal,
    int SupplierOverdueCount);

public sealed record CustomerDebtRow(
    long CustomerId,
    string Code,
    string Name,
    string Phone,
    string GroupName,
    string Address,
    decimal TotalIncurredVnd,
    decimal PaidVnd,
    decimal RemainingVnd,
    DateTime? LastDebtDate,
    string StatusCode,
    // Công nợ 2 chiều (số dư hiện tại, không lọc theo kỳ):
    decimal ReceivableRemainingVnd = 0m,  // còn phải thu: đơn hàng + khoản phải thu (RECEIVABLE_DEBTS)
    decimal PayableRemainingVnd = 0m);    // còn phải trả: DN nợ KH (PAYABLE_DEBTS party=CUSTOMER)

public sealed record SupplierDebtRow(
    long SupplierId,
    string Code,
    string Name,
    string Phone,
    string GroupName,
    string Address,
    decimal TotalIncurredVnd,
    decimal PaidVnd,
    decimal RemainingVnd,
    DateTime? LastDebtDate,
    string StatusCode,
    // Công nợ 2 chiều (số dư hiện tại, không lọc theo kỳ):
    decimal ReceivableRemainingVnd = 0m,  // còn phải thu: NCC nợ DN (RECEIVABLE_DEBTS party=SUPPLIER)
    decimal PayableRemainingVnd = 0m);     // còn phải trả: DN nợ NCC (PAYABLE_DEBTS)

public sealed record CustomerDebtSearchFilter(
    string SearchText,
    long GroupId,
    string StatusCode,
    DateTime? FromDate,
    DateTime? ToDate);

public sealed record SupplierDebtSearchFilter(
    string SearchText,
    string StatusCode,
    DateTime? FromDate,
    DateTime? ToDate);

/// <summary>Hướng của dòng sổ cái công nợ: phải thu (khách hàng nợ) hay phải trả (nợ nhà cung cấp).</summary>
public static class DebtLedgerDirections
{
    public const string Receive = "RECEIVE"; // phải thu
    public const string Pay = "PAY";         // phải trả
}

/// <summary>Nguồn gốc của dòng sổ cái công nợ.</summary>
public static class DebtLedgerSourceTypes
{
    public const string SalesOrder = "SALES_ORDER";
    public const string OrderPayment = "ORDER_PAYMENT";
    public const string SalesCheckoutOffset = "SALES_CHECKOUT_OFFSET";
    public const string PayableDebt = "PAYABLE_DEBT";
    public const string PayableDebtPayment = "PAYABLE_DEBT_PAYMENT";
    public const string ReceivableDebt = "RECEIVABLE_DEBT";
    public const string CashVoucher = "CASH_VOUCHER";
}

/// <summary>
/// Dòng sổ cái công nợ. Các tham số mới đều có giá trị mặc định nên các chỗ
/// khởi tạo cũ (positional, 6 tham số đầu) vẫn biên dịch không đổi.
/// </summary>
public sealed record DebtLedgerEntry(
    DateTime EntryDate,
    string DocumentCode,
    string Description,
    decimal ReceiptAmountVnd,
    decimal PaymentAmountVnd,
    decimal BalanceAfterVnd,
    string? SourceType = null,           // DebtLedgerSourceTypes.*
    long? SourceId = null,               // Id của bản ghi gốc (đơn hàng, khoản nợ...)
    string? Direction = null,            // DebtLedgerDirections.RECEIVE | PAY
    decimal OriginalAmountVnd = 0m,      // số tiền gốc của chứng từ nợ
    decimal PaidAmountVnd = 0m,          // đã thu/chi
    decimal RemainingAmountVnd = 0m,     // còn dư
    bool CanSettle = false,             // có hiện nút Thu/Chi: còn dư > 0 và không phải dòng hủy/bù trừ/phiếu
    string? LinkedVoucherCode = null,    // mã phiếu thu/chi đã gắn (nếu có)
    long? LinkedVoucherId = null,
    string? StatusCode = null);         // OPEN / PARTIAL / PAID / CANCELLED / ...

public sealed record CustomerCollectPaymentRequest(
    long CustomerId,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    string? Notes);

public sealed record SupplierCollectPaymentRequest(
    long SupplierId,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    string? Notes);

/// <summary>
/// Yêu cầu tất toán lẻ đúng 1 khoản nguồn trong sổ cái công nợ
/// (nút Thu/Chi trên từng dòng lịch sử giao dịch).
/// </summary>
/// <param name="SourceType">DebtLedgerSourceTypes.SalesOrder | ReceivableDebt | PayableDebt.</param>
/// <param name="SourceId">Id của bản ghi gốc (đơn hàng / khoản nợ).</param>
public sealed record SingleLedgerSettleRequest(
    string SourceType,
    long SourceId,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    string? Notes);

/// <summary>Một dòng phân bổ trong yêu cầu tất toán N khoản công nợ.</summary>
/// <param name="SourceType">DebtLedgerSourceTypes.SalesOrder | ReceivableDebt | PayableDebt.</param>
/// <param name="SourceId">Id của bản ghi gốc (đơn hàng / khoản nợ).</param>
/// <param name="AmountVnd">Số tiền thanh toán cho dòng này (phải &gt; 0).</param>
public sealed record DebtAllocationSettleLine(
    string SourceType,
    long SourceId,
    decimal AmountVnd);

/// <summary>
/// Yêu cầu tất toán N khoản công nợ (1 hoặc nhiều): 1 settlement cho mỗi dòng +
/// phiếu thu/chi + cập nhật N khoản nợ, tất cả trong 1 transaction.
/// Tổng 3 ô phương thức phải bằng tổng các dòng.
/// </summary>
public sealed record DebtAllocationSettleRequest(
    IReadOnlyList<DebtAllocationSettleLine> Lines,
    decimal CashAmountVnd,
    decimal BankTransferAmountVnd,
    decimal CardAmountVnd,
    long? FundId,
    long? BankAccountId,
    string? Notes);

/// <summary>Một đơn hàng còn nợ của khách hàng (để tất toán gộp).</summary>
public sealed record OpenCustomerReceivableLine(
    long OrderId,
    string OrderCode,
    decimal RemainingAmountVnd,
    DateTime OrderDate);

/// <summary>Một khoản phải thu mở (RECEIVABLE_DEBTS) của một đối tượng.</summary>
public sealed record ReceivableDebtListItem(
    long Id,
    string Code,
    string PartyType,
    long PartyId,
    string DebtKind,
    string OriginDocumentType,
    long OriginDocumentId,
    decimal OriginalAmountVnd,
    decimal CollectedAmountVnd,
    decimal RemainingAmountVnd,
    string StatusCode,
    DateTime CreatedAt,
    string? Notes = null);

public interface ICashFundRepository
{
    Task<IReadOnlyList<CashFundOption>> GetActiveFundsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IBankAccountRepository
{
    Task<IReadOnlyList<BankAccountOption>> GetActiveAccountsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IPayableDebtRepository
{
    Task<IReadOnlyList<PayableDebtListItem>> SearchAsync(
        PayableDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task PayAsync(
        PayableDebtPaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public interface IDebtWorkbenchRepository
{
    Task<DebtWorkbenchSummary> GetSummaryAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerDebtRow>> SearchCustomerDebtsAsync(
        CustomerDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SupplierDebtRow>> SearchSupplierDebtsAsync(
        SupplierDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebtLedgerEntry>> GetCustomerLedgerAsync(
        long customerId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DebtLedgerEntry>> GetSupplierLedgerAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PayableDebtListItem>> GetSupplierOpenPayablesAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Các khoản NCC còn nợ DN (RECEIVABLE_DEBTS party=SUPPLIER), FIFO theo ngày tạo.</summary>
    Task<IReadOnlyList<ReceivableDebtListItem>> GetSupplierReceivableDebtsAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Các khoản DN còn nợ KH (PAYABLE_DEBTS party=CUSTOMER), FIFO theo ngày tạo.</summary>
    Task<IReadOnlyList<PayableDebtListItem>> GetCustomerOpenPayablesAsync(
        long customerId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Thu gộp tiền NCC nợ DN: 1 settlement + 1 phiếu thu, phân bổ FIFO.</summary>
    Task CollectSupplierPaymentAsync(
        SupplierCollectPaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task PayCustomerFifoAsync(
        long customerId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task CollectCustomerPaymentAsync(
        CustomerCollectPaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task PaySupplierFifoAsync(
        long supplierId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task PayPayablePartyFifoAsync(
        string partyType,
        long partyId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tất toán lẻ đúng 1 khoản nguồn (theo SourceType + SourceId của dòng sổ cái):
    /// 1 settlement + 1 phiếu thu/chi + cập nhật khoản nợ, trong 1 transaction.
    /// </summary>
    Task SettleSingleLedgerEntryAsync(
        SingleLedgerSettleRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tất toán N khoản công nợ (1 hoặc nhiều): mỗi dòng 1 settlement +
    /// phiếu thu/chi + cập nhật N khoản nợ, tất cả trong 1 transaction.
    /// Tổng 3 ô phương thức (tiền mặt/CK/thẻ) phải bằng tổng các dòng.
    /// </summary>
    Task SettleDebtAllocationsAsync(
        DebtAllocationSettleRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Các đơn hàng còn nợ của khách hàng (số dư &gt; 0), FIFO theo ngày đặt.</summary>
    Task<IReadOnlyList<OpenCustomerReceivableLine>> GetCustomerOpenReceivablesAsync(
        long customerId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tìm các khoản nợ vàng của thợ (WORKER_GOLD_DEBTS), kèm tên hiển thị
    /// "Tên (Mã NV)" và số còn nợ = WEIGHT_CHI - WEIGHT_PAID_CHI.
    /// </summary>
    Task<IReadOnlyList<WorkerGoldDebtListItem>> SearchWorkerGoldDebtsAsync(
        WorkerGoldDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi nhận 1 lần thợ trả vàng: INSERT lịch sử WORKER_GOLD_DEBT_PAYMENTS,
    /// cộng dồn WEIGHT_PAID_CHI và cập nhật STATUS_CODE (PARTIAL/PAID),
    /// tất cả trong 1 transaction.
    /// </summary>
    Task RecordWorkerGoldRepaymentAsync(
        WorkerGoldRepaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Lịch sử các lần trả vàng của 1 khoản nợ, mới nhất trước.</summary>
    Task<IReadOnlyList<WorkerGoldDebtPaymentItem>> GetWorkerGoldDebtPaymentsAsync(
        long debtId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public static class WorkerGoldDebtStatuses
{
    public const string Open = "OPEN";
    public const string Partial = "PARTIAL";
    public const string Paid = "PAID";
    public const string Cancelled = "CANCELLED";
}

/// <summary>Một khoản nợ vàng của thợ (WORKER_GOLD_DEBTS).</summary>
public sealed record WorkerGoldDebtListItem(
    long Id,
    string Code,
    string WorkerType,
    long WorkerId,
    string WorkerDisplayName,
    string WorkerCode,
    string? GoldPurityCode,
    decimal WeightChi,
    decimal WeightPaidChi,
    decimal RemainingChi,
    long ProductionOrderId,
    string ProductionOrderCode,
    string StatusCode,
    DateTime CreatedAt);

/// <summary>Bộ lọc tìm nợ vàng thợ.</summary>
public sealed record WorkerGoldDebtSearchFilter(
    string? WorkerText,
    string? GoldPurityCode,
    bool OnlyOpen,
    DateTime? FromDate,
    DateTime? ToDate);

/// <summary>Yêu cầu ghi nhận 1 lần thợ trả vàng.</summary>
public sealed record WorkerGoldRepaymentRequest(
    long DebtId,
    decimal WeightChi,
    DateTime PaidDate,
    string? Notes);

/// <summary>Một dòng lịch sử trả vàng (WORKER_GOLD_DEBT_PAYMENTS).</summary>
public sealed record WorkerGoldDebtPaymentItem(
    long Id,
    decimal WeightPaidChi,
    DateTime PaidDate,
    string? Notes,
    string? CreatedByName,
    DateTime CreatedAt);
