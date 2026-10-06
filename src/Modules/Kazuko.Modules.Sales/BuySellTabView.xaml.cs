using System.Windows.Controls;
using System.Windows.Input;

namespace Kazuko.Modules.Sales;

public partial class BuySellTabView : UserControl
{
    public BuySellTabView() => InitializeComponent();

    private void ProductCodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not BuySellWorkspaceViewModel vm)
            return;

        if (e.Key == Key.Escape)
        {
            vm.IsProductSuggestionOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Return)
        {
            if (vm.TryAcceptProductFromEnter())
            {
                e.Handled = true;
                return;
            }

            if (vm.AddSellLineCommand.CanExecute(null))
            {
                vm.AddSellLineCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Down && vm.ProductSuggestions.Count > 0)
        {
            vm.IsProductSuggestionOpen = true;
            ProductSuggestionList.Focus();
            if (ProductSuggestionList.SelectedIndex < 0 && ProductSuggestionList.Items.Count > 0)
                ProductSuggestionList.SelectedIndex = 0;
            e.Handled = true;
        }
    }

    private void ProductSuggestionList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not BuySellWorkspaceViewModel vm)
            return;

        if (e.Key is Key.Enter or Key.Return)
        {
            vm.AcceptHighlightedSuggestion();
            e.Handled = true;
            ProductCodeBox.Focus();
            return;
        }

        if (e.Key == Key.Escape)
        {
            vm.IsProductSuggestionOpen = false;
            ProductCodeBox.Focus();
            e.Handled = true;
        }
    }

    private void ProductSuggestionList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not BuySellWorkspaceViewModel vm)
            return;
        if (ProductSuggestionList.SelectedItem is ProductSuggestionItem)
        {
            vm.AcceptHighlightedSuggestion();
            ProductCodeBox.Focus();
        }
    }
}
