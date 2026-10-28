using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Warehouse;

public partial class ProductionFinishedReceiptDialog : Window
{
    public ProductionFinishedReceiptDialog(ProductionFinishedReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closing+=(_,e)=>e.Cancel=!viewModel.CanClose();
        Closed+=(_,_)=>viewModel.PropertyChanged-=OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductionFinishedReceiptDialogViewModel.DialogResult)
            && DataContext is ProductionFinishedReceiptDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }

    private void PrintCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: ManualFinishedProductItem item }
            && DataContext is ProductionFinishedReceiptDialogViewModel vm)
            vm.SetPrintChecked(item, true);
    }

    private void PrintCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { Tag: ManualFinishedProductItem item }
            && DataContext is ProductionFinishedReceiptDialogViewModel vm)
            vm.SetPrintChecked(item, false);
    }
}
