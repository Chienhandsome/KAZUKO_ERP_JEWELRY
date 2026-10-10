using System.Windows;
using System.Windows.Controls;
using Kazuko.Core.Authentication;
using Kazuko.Core.Modules;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Quick-menu host that opens the warehouse gold-price dialog, then returns home.
/// </summary>
public sealed class WarehouseGoldPriceLaunchView : UserControl
{
    public WarehouseGoldPriceLaunchView(
        IWarehouseGoldPriceRepository repository,
        ICurrentSessionAccessor sessionAccessor,
        INavigationService navigation)
    {
        Background = System.Windows.Media.Brushes.Transparent;
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                var session = sessionAccessor.Current;
                if (session is not null)
                {
                    var dialog = new WarehouseGoldPriceDialog(
                        new WarehouseGoldPriceDialogViewModel(repository, session))
                    {
                        Owner = Application.Current?.MainWindow
                    };
                    dialog.ShowDialog();
                }

                navigation.Navigate("DASHBOARD");
            });
        };
    }
}
