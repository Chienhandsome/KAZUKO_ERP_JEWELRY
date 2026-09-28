using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public partial class MaterialReceiptDialog : Window
{
    public MaterialReceiptDialog(MaterialReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        CreatePartnerButton.Command = viewModel.CreateSupplierCommand;
        BindSupplierPartner();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MaterialReceiptDialogViewModel.DialogResult)
                && viewModel.DialogResult is not null)
            {
                DialogResult = viewModel.DialogResult;
            }
        };
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialReceiptDialogViewModel.IsSupplierSource)
            && DataContext is MaterialReceiptDialogViewModel vm)
        {
            if (vm.IsSupplierSource)
                BindSupplierPartner();
            else
                BindCustomerPartner();
        }
    }

    private void SupplierSource_OnChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MaterialReceiptDialogViewModel vm)
            vm.SourceType = StockSourceTypes.Supplier;
        BindSupplierPartner();
    }

    private void OldGoldSource_OnChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is MaterialReceiptDialogViewModel vm)
            vm.SourceType = StockSourceTypes.OldGoldCustomer;
        BindCustomerPartner();
    }

    private void BindSupplierPartner()
    {
        if (DataContext is not MaterialReceiptDialogViewModel vm)
            return;
        PartnerCombo.ItemsSource = vm.Suppliers;
        PartnerCombo.SetBinding(ComboBox.SelectedItemProperty,
            new System.Windows.Data.Binding(nameof(MaterialReceiptDialogViewModel.SelectedSupplier)));
        CreatePartnerButton.Command = vm.CreateSupplierCommand;
    }

    private void BindCustomerPartner()
    {
        if (DataContext is not MaterialReceiptDialogViewModel vm)
            return;
        PartnerCombo.ItemsSource = vm.Customers;
        PartnerCombo.SetBinding(ComboBox.SelectedItemProperty,
            new System.Windows.Data.Binding(nameof(MaterialReceiptDialogViewModel.SelectedCustomer)));
        CreatePartnerButton.Command = vm.CreateCustomerCommand;
    }
}
