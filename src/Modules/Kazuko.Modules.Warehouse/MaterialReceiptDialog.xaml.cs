using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public partial class MaterialReceiptDialog : Window
{
    public MaterialReceiptDialog(MaterialReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += ViewModelOnPropertyChanged;
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
        if (e.PropertyName is nameof(MaterialReceiptDialogViewModel.IsSupplierSource)
                or nameof(MaterialReceiptDialogViewModel.SourceType)
            && DataContext is MaterialReceiptDialogViewModel vm)
        {
            if (vm.IsSupplierSource)
                BindSupplierPartner();
            else
                BindCustomerPartner();
        }
    }

    private void BindSupplierPartner()
    {
        if (DataContext is not MaterialReceiptDialogViewModel vm)
            return;
        PartnerCombo.ItemsSource = vm.Suppliers;
        PartnerCombo.SetBinding(ComboBox.SelectedItemProperty,
            new Binding(nameof(MaterialReceiptDialogViewModel.SelectedSupplier)));
        CreatePartnerButton.Command = vm.CreateSupplierCommand;
    }

    private void BindCustomerPartner()
    {
        if (DataContext is not MaterialReceiptDialogViewModel vm)
            return;
        PartnerCombo.ItemsSource = vm.Customers;
        PartnerCombo.SetBinding(ComboBox.SelectedItemProperty,
            new Binding(nameof(MaterialReceiptDialogViewModel.SelectedCustomer)));
        CreatePartnerButton.Command = vm.CreateCustomerCommand;
    }
}
