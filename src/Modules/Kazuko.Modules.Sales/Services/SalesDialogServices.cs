using Kazuko.Core.Finance;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Services;

public sealed record SalesPaymentDialogRequest(
    decimal NetAmountVnd,
    bool IsPayout,
    bool CanApproveDebt,
    bool AllowReceivable,
    IReadOnlyList<CashFundOption> CashFunds,
    IReadOnlyList<BankAccountOption> BankAccounts,
    string DocumentHint);

public sealed record SalesPaymentDialogResult(
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    decimal ReceivableAmountVnd);

public interface ISalesPaymentDialogService
{
    SalesPaymentDialogResult? Show(SalesPaymentDialogRequest request);
}

public interface ISalesReceiptPresenter
{
    Task ShowAsync(long checkoutId, CancellationToken cancellationToken = default);
}

public sealed class NullSalesReceiptPresenter : ISalesReceiptPresenter
{
    public Task ShowAsync(long checkoutId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
