using System.Windows.Controls;
using System.Windows.Input;

namespace Kazuko.Modules.Production;

public partial class ProductionView : UserControl
{
    public ProductionView() => InitializeComponent();

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
