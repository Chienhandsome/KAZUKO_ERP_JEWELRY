using System.Windows;

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
}
