using System.Windows;
using System.Windows.Controls;

namespace Kazuko.Modules.Reports;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private void ReportTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is ReportsViewModel viewModel)
            viewModel.SelectedNode = e.NewValue as ReportTreeNode;
    }
}
