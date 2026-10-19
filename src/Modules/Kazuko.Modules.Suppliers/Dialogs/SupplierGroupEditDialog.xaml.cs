using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Suppliers.Dialogs;

public partial class SupplierGroupEditDialog : Window
{
    public SupplierGroupEditDialog()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is SupplierGroupEditDialogViewModel vm && vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
