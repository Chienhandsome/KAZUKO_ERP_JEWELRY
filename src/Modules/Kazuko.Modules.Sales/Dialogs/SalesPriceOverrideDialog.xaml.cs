using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Sales.Dialogs;

public partial class SalesPriceOverrideDialog : Window
{
    public SalesPriceOverrideDialog(SalesPriceOverrideDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalesPriceOverrideDialogViewModel.DialogResult)
            && DataContext is SalesPriceOverrideDialogViewModel vm
            && vm.DialogResult is not null)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SalesPriceOverrideDialogViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
