using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class FinishedProductGroupDialog : Window
{
    public FinishedProductGroupDialog(FinishedProductGroupDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FinishedProductGroupDialogViewModel.DialogResult)
            && DataContext is FinishedProductGroupDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
