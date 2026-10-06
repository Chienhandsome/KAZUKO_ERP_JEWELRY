using System.Windows;
using Kazuko.Core.Authentication;
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

public interface ISalesCustomerLookupDialogService
{
    SalesCustomerOption? Show(Func<string, Task<IReadOnlyList<SalesCustomerOption>>> searchAsync, SalesCustomerOption? current);
}

public interface ISalesPriceOverrideDialogService
{
    bool TryEdit(SellLineRowViewModel line);
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

public sealed class WpfSalesReceiptPresenter : ISalesReceiptPresenter
{
    private readonly ISalesPrintDataService _printData;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly SalesReceiptPrintService _printService;

    public WpfSalesReceiptPresenter(
        ISalesPrintDataService printData,
        ICurrentSessionAccessor sessionAccessor,
        SalesReceiptPrintService? printService = null)
    {
        _printData = printData ?? throw new ArgumentNullException(nameof(printData));
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        _printService = printService ?? new SalesReceiptPrintService();
    }

    public async Task ShowAsync(long checkoutId, CancellationToken cancellationToken = default)
    {
        var session = _sessionAccessor.Current
            ?? throw new InvalidOperationException("Chưa đăng nhập.");
        var receipt = await _printData.GetReceiptAsync(checkoutId, session, cancellationToken);
        var document = _printService.CreateDocument(receipt);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var viewModel = new Dialogs.SalesReceiptPreviewDialogViewModel(receipt, document);
            var dialog = new Dialogs.SalesReceiptPreviewDialog(viewModel)
            {
                Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                    ?? Application.Current?.MainWindow
            };
            dialog.ShowDialog();
        });
    }
}
