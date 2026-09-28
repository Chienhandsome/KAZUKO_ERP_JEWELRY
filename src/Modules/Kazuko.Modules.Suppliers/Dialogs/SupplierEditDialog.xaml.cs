using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Suppliers;

public partial class SupplierEditDialog : Window
{
    public SupplierEditDialog()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is SupplierEditDialogViewModel vm && vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
