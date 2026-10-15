using System.Windows;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Dialog đối chiếu vàng: hiển thị vàng đã giao / đã nhận / hao hụt / nợ vàng
/// và cho phép ghi công nợ thợ. Dùng chung DataContext với dialog nhập kho.
/// </summary>
public partial class GoldReconcileDialog : Window
{
    public GoldReconcileDialog(ProductionFinishedReceiptDialogViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
