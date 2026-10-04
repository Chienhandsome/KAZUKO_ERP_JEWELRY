using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class WarehouseGoldPriceDialog : Window
{
    public WarehouseGoldPriceDialog(WarehouseGoldPriceDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WarehouseGoldPriceDialogViewModel.DialogResult)
            && DataContext is WarehouseGoldPriceDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
