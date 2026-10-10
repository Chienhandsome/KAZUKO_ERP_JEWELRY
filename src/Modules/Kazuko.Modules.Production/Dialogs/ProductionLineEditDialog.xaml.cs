using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Production.Dialogs;

public partial class ProductionLineEditDialog : Window
{
    private bool _accepted;

    public ProductionLineEditDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductionLineEditDialogViewModel viewModel)
        {
            viewModel.RequestClose += success =>
            {
                _accepted = success;
                DialogResult = success;
            };
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_accepted && DataContext is ProductionLineEditDialogViewModel viewModel)
            viewModel.CleanupSessionStaging();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is ProductionLineEditDialogViewModel vm)
                vm.CancelCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void MoneyField_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductionLineEditDialogViewModel vm)
            vm.FormatMoneyFieldsOnLostFocus();
    }
}
