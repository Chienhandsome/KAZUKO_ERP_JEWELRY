using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Sales.Dialogs;

public partial class SalesPaymentDialog : Window
{
    public SalesPaymentDialog(SalesPaymentDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalesPaymentDialogViewModel.DialogResult)
            && DataContext is SalesPaymentDialogViewModel vm
            && vm.DialogResult is not null)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }

    private void PaymentMethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SalesPaymentDialogViewModel vm)
            return;
        if (sender is not ComboBox { DataContext: SalesPaymentAllocationRow row })
            return;

        if (row.RequiresCashFund)
        {
            row.CashFund ??= vm.DefaultCashFund;
            row.BankAccount = null;
        }
        else if (row.RequiresBankAccount)
        {
            row.BankAccount ??= vm.DefaultBankAccount;
            row.CashFund = null;
        }

        vm.NotifyAllocationChanged();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SalesPaymentDialogViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
