using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Kazuko.Modules.Dashboard;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private void ShortcutSettings_Opened(object sender, EventArgs e)
    {
        if (sender is Popup { Child: { } child })
            child.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void ShortcutSettings_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not Popup popup) return;
        popup.SetCurrentValue(Popup.IsOpenProperty, false);
        ShortcutSettingsButton.Focus();
        e.Handled = true;
    }
}
