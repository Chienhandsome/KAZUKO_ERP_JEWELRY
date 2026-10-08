using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.HumanResources;

public partial class EmployeeEditDialog : Window
{
    public EmployeeEditDialog()
    {
        InitializeComponent();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is EmployeeEditDialogViewModel vm && vm.CancelCommand.CanExecute(null))
                vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (e.OriginalSource is System.Windows.Controls.TextBox { AcceptsReturn: true })
                return;

            if (DataContext is EmployeeEditDialogViewModel vm && vm.SaveCommand.CanExecute(null))
            {
                vm.SaveCommand.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void SalaryTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is EmployeeEditDialogViewModel vm)
            vm.FormatSalaryTexts();
    }
}
