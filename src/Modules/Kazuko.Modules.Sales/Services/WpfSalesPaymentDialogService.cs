using System.Windows;
using Kazuko.Modules.Sales.Dialogs;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.Services;

public sealed class WpfSalesPaymentDialogService : ISalesPaymentDialogService
{
    public SalesPaymentDialogResult? Show(SalesPaymentDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var viewModel = new SalesPaymentDialogViewModel(
            request.NetAmountVnd,
            request.IsPayout,
            request.CanApproveDebt,
            request.AllowReceivable,
            request.CashFunds,
            request.BankAccounts,
            request.DocumentHint);
        var dialog = new SalesPaymentDialog(viewModel)
        {
            Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive)
                ?? Application.Current?.MainWindow
        };
        return dialog.ShowDialog() == true ? viewModel.Result : null;
    }
}
