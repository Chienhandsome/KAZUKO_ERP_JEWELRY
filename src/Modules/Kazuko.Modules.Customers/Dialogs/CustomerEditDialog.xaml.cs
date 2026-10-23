using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Customers;

public partial class CustomerEditDialog : Window
{
    public CustomerEditDialog()
    {
        InitializeComponent();
    }

    public void FocusPhoneIfEmpty()
    {
        if (DataContext is CustomerEditDialogViewModel vm && string.IsNullOrWhiteSpace(vm.Phone)) PhoneInput.Focus();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is CustomerEditDialogViewModel vm && vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
