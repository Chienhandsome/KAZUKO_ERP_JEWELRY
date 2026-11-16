using System.Windows.Controls;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Dashboard;

public partial class AdministrationView
{
    internal AdministrationView(
        IAuthorizationService authorization,
        Func<SettingsView> createSettings,
        Func<object> createUsers,
        bool openUsers)
    {
        InitializeComponent();

        var rights = authorization.Current;
        var canOpenSettings = CanOpenSettings(rights);
        if (rights.Allows("SYS_USER_MANAGEMENT") && (openUsers || !canOpenSettings))
            AdministrationTabs.SelectedItem = UsersTab;
        else if (canOpenSettings)
            AdministrationTabs.SelectedItem = SettingsTab;

        LoadSelectedTab();
        AdministrationTabs.SelectionChanged += (_, e) =>
        {
            if (ReferenceEquals(e.OriginalSource, AdministrationTabs))
                LoadSelectedTab();
        };

        void LoadSelectedTab()
        {
            var permissions = authorization.Current;
            if (ReferenceEquals(AdministrationTabs.SelectedItem, UsersTab) && permissions.Allows("SYS_USER_MANAGEMENT"))
                UsersTab.Content ??= createUsers();
            else if (ReferenceEquals(AdministrationTabs.SelectedItem, SettingsTab) &&
                     CanOpenSettings(permissions))
                SettingsTab.Content ??= createSettings();
        }
    }

    private static bool CanOpenSettings(PermissionSnapshot permissions) =>
        permissions.Allows("SYS_SETTINGS") || permissions.Allows("SYS_DATABASE");
}
