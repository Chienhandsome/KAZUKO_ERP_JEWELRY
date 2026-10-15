using System.Windows.Controls;
using Kazuko.Core.Modules;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Quick-menu host that opens the external-worker catalog dialog, then returns home.
/// </summary>
public sealed class ExternalWorkerLaunchView : UserControl
{
    public ExternalWorkerLaunchView(
        IExternalWorkerEditorService workerEditor,
        INavigationService navigation)
    {
        Background = System.Windows.Media.Brushes.Transparent;
        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                workerEditor.Show();
                navigation.Navigate("DASHBOARD");
            });
        };
    }
}
