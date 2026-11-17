using System.Windows;
using System.Windows.Controls;

namespace Kazuko.Shared.UI.Controls;

public partial class CatalogDialog : Window
{
    public bool ShowContactDetails { get; set; } = true;
    public CatalogDialog() => InitializeComponent();

    private void OnCustomerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox { SelectedItem: { } item } list)
            list.ScrollIntoView(item);
    }
}
