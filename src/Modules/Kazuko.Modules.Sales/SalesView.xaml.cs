using System.Windows.Controls;
using System.Windows.Input;

namespace Kazuko.Modules.Sales;

public partial class SalesView : UserControl
{
    public SalesView() => InitializeComponent();

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SalesViewModel viewModel)
            return;

        if (e.Key == Key.F9)
        {
            if (viewModel.PayCommand.CanExecute(null))
                viewModel.PayCommand.Execute(null);
            e.Handled = true;
        }
    }
}
