using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Customers;

public partial class CitizenScanDialog : Window
{
    public CitizenScanDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => ScanInput.Focus();
        Closed += (_, _) => (DataContext as CitizenScanDialogViewModel)?.OnClosed();
    }

    private void ScanInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; // Never bubble scanner Enter to the parent's default Save button.
        if (DataContext is CitizenScanDialogViewModel vm && vm.ReadCommand.CanExecute(null)) vm.ReadCommand.Execute(null);
    }
}
