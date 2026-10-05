using System.Windows;
using System.Windows.Input;

namespace Kazuko.Modules.Production.Dialogs;

public partial class ProductionImageViewerDialog : Window
{
    public ProductionImageViewerDialog()
    {
        InitializeComponent();
    }

    private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DataContext is not ProductionImageViewerViewModel vm)
            return;

        if (e.Delta > 0)
            vm.ZoomIn();
        else
            vm.ZoomOut();
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }
}
