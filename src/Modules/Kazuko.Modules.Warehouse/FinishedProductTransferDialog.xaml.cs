using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class FinishedProductTransferDialog : Window
{
    public FinishedProductTransferDialog(FinishedProductTransferDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FinishedProductTransferDialogViewModel.DialogResult)
            && DataContext is FinishedProductTransferDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
