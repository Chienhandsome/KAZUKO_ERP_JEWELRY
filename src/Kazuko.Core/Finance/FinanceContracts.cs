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
}

public static class PayablePartyTypes
{
    public const string Supplier = "SUPPLIER";
    public const string Customer = "CUSTOMER";
    public const string ExternalWorker = "EXTERNAL_WORKER";
}

public static class PayableDebtKinds
{
    public const string Purchase = "PURCHASE";
    public const string Labor = "LABOR";
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
    string StatusCode);

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
    string StatusCode);

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

public sealed record DebtLedgerEntry(
    DateTime EntryDate,
    string DocumentCode,
    string Description,
    decimal ReceiptAmountVnd,
    decimal PaymentAmountVnd,
    decimal BalanceAfterVnd);

public sealed record CustomerCollectPaymentRequest(
    long CustomerId,
    decimal AmountVnd,
    string PaymentMethod,
    string? ReferenceNo,
    string? Notes);

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
}
