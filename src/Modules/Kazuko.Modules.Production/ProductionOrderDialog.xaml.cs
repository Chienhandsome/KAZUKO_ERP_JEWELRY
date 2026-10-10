using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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
        if (DataContext is not ProductionOrderDialogViewModel viewModel)
            return;

        for (var element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is DataGridRow { Item: ProductionLineEditorViewModel row })
            {
                if (viewModel.EditLineCommand.CanExecute(row))
                    viewModel.EditLineCommand.Execute(row);
                e.Handled = true;
                return;
            }
            if (ReferenceEquals(element, sender))
                return;
        }
    }
}
