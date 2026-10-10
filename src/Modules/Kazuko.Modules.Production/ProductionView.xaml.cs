using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kazuko.Modules.Production;

public partial class ProductionView : UserControl
{
    public ProductionView() => InitializeComponent();

    private void OrdersGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not ProductionViewModel viewModel)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is DataGridRow { Item: ProductionOrderRowViewModel row })
            {
                if (viewModel.OpenDetailCommand.CanExecute(row))
                    viewModel.OpenDetailCommand.Execute(row);
                e.Handled = true;
                return;
            }
            if (ReferenceEquals(element, sender))
                return;
        }
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ProductionViewModel viewModel)
            return;

        if (e.Key == Key.F5)
        {
            if (viewModel.RefreshCommand.CanExecute(null))
                viewModel.RefreshCommand.Execute(null);
            e.Handled = true;
        }
    }
}
