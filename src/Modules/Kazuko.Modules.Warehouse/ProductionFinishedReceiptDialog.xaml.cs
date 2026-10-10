using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class ProductionFinishedReceiptDialog : Window
{
    public ProductionFinishedReceiptDialog(ProductionFinishedReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductionFinishedReceiptDialogViewModel.DialogResult)
            && DataContext is ProductionFinishedReceiptDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
