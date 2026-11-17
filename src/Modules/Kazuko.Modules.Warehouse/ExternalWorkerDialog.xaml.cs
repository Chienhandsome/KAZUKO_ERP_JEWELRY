using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class ExternalWorkerDialog : Window
{
    public ExternalWorkerDialog(ExternalWorkerDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += (_, args) => args.Cancel = viewModel.IsBusy;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExternalWorkerDialogViewModel.DialogResult)
            && DataContext is ExternalWorkerDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
