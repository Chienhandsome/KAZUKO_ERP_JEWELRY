using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class SalesIntakeApprovalDialog : Window
{
    public SalesIntakeApprovalDialog(SalesIntakeApprovalDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SalesIntakeApprovalDialogViewModel.DialogResult)
            && DataContext is SalesIntakeApprovalDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
