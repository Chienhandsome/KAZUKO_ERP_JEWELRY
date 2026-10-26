using System.Windows;
namespace Kazuko.Modules.HumanResources;
public partial class EmployeeMoneyDialog : Window
{
    private bool _saved;
    public EmployeeMoneyDialog(EmployeeMoneyDialogViewModel vm)
    {
        InitializeComponent(); DataContext = vm;
        Loaded += async (_, _) => await vm.InitializeAsync();
        vm.CloseRequested += result => { _saved = result; DialogResult = result; };
        Closing += (_, e) => { if (vm.IsBusy && !_saved) e.Cancel = true; };
    }
}
