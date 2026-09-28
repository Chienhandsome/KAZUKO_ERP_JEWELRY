using System.Windows;
using System.Windows.Controls;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

/// <summary>
/// Navigation host: opens the price-board dialog then returns to the dashboard.
/// </summary>
public sealed class PriceBoardLaunchView : UserControl
{
    public PriceBoardLaunchView(IPriceBoardDialogService dialogs, INavigationService navigation)
    {
        Background = System.Windows.Media.Brushes.Transparent;
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                dialogs.ShowPriceBoard();
                navigation.Navigate("DASHBOARD");
            });
        };
    }
}
