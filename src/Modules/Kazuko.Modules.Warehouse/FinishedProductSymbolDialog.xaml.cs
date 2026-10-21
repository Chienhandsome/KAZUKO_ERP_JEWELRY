using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class FinishedProductSymbolDialog : Window
{
    public FinishedProductSymbolDialog(FinishedProductSymbolDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FinishedProductSymbolDialogViewModel.DialogResult)
            && DataContext is FinishedProductSymbolDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
