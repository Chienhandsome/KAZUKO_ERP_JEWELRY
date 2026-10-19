using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class LabelPrintDialog : Window
{
    public LabelPrintDialog(LabelPrintDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LabelPrintDialogViewModel.DialogResult)
            && DataContext is LabelPrintDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
