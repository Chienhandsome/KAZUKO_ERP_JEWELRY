using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Kazuko.Modules.Customers;

public partial class CustomerCatalogDialog : Window
{
    public CustomerCatalogDialog() => InitializeComponent();

    private void OnCustomerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } item } list)
            list.ScrollIntoView(item);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = DataContext is CustomerCatalogViewModel { IsBusy: true };
    }
}
