using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Production;

public partial class ProductionOrderDialog : Window
{
    public ProductionOrderDialog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProductionOrderDialogViewModel viewModel)
            viewModel.RequestClose += success => DialogResult = success;
    }

    private void LinesGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is ProductionOrderDialogViewModel viewModel
            && viewModel.EditLineCommand.CanExecute(viewModel.SelectedLine))
        {
            viewModel.EditLineCommand.Execute(viewModel.SelectedLine);
        }
    }
}
