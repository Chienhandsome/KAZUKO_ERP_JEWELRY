using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Sales.Dialogs;

public partial class SalesCustomerLookupDialog : Window
{
    public SalesCustomerLookupDialog(SalesCustomerLookupDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalesCustomerLookupDialogViewModel.DialogResult)
            && DataContext is SalesCustomerLookupDialogViewModel vm
            && vm.DialogResult is not null)
        {
            DialogResult = vm.DialogResult;
            Close();
        }
    }

    private void DataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is SalesCustomerLookupDialogViewModel vm)
            vm.ConfirmSelection();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is SalesCustomerLookupDialogViewModel vm)
        {
            vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }
}
