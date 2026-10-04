using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class MaterialIssueDialog : Window
{
    public MaterialIssueDialog(MaterialIssueDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MaterialIssueDialogViewModel.DialogResult)
            && DataContext is MaterialIssueDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
