using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.HumanResources;

public partial class ShiftEditDialog : Window
{
    public ShiftEditDialog()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (DataContext is ShiftEditDialogViewModel { IsSaving: true } && DialogResult != true) e.Cancel = true;
        };
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is ShiftEditDialogViewModel vm && vm.CancelCommand.CanExecute(null))
            {
                vm.CancelCommand.Execute(null);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (DataContext is ShiftEditDialogViewModel vm && vm.SaveCommand.CanExecute(null))
            {
                vm.SaveCommand.Execute(null);
                e.Handled = true;
            }
        }
    }
}
