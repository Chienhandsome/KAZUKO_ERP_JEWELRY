using System.Windows;

namespace Kazuko.Modules.Warehouse;

public partial class MaterialReceiptDialog : Window
{
    public MaterialReceiptDialog(MaterialReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MaterialReceiptDialogViewModel.DialogResult)
                && viewModel.DialogResult is not null)
            {
                DialogResult = viewModel.DialogResult;
            }
        };
    }
}
