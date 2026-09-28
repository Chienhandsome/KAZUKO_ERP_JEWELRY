using System.Windows.Controls;
using System.Windows.Input;

namespace Kazuko.Modules.HumanResources;

public partial class HumanResourcesView : UserControl
{
    public HumanResourcesView()
    {
        InitializeComponent();
    }

    private void UserControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F1 && DataContext is HumanResourcesViewModel vm && vm.AddCommand.CanExecute(null))
        {
            vm.AddCommand.Execute(null);
            e.Handled = true;
        }
    }
}
