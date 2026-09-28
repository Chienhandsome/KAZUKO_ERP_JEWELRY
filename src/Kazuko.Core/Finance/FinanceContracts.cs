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
