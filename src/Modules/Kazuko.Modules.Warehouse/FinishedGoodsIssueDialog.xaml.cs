using System.ComponentModel;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class FinishedGoodsIssueDialog : Window
{
    public FinishedGoodsIssueDialog(FinishedGoodsIssueDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FinishedGoodsIssueDialogViewModel.DialogResult)
            && DataContext is FinishedGoodsIssueDialogViewModel { DialogResult: not null } vm)
            DialogResult = vm.DialogResult;
    }
}
