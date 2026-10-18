using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using Kazuko.Core.Modules;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Dashboard;

public sealed class DashboardTileViewModel
{
    public DashboardTileViewModel(NavigationItem item, INavigationService navigation)
    {
        Key = item.Key;
        Label = item.Label;
        IconUri = item.IconUri;
        Description = item.Description ?? string.Empty;
        NavigateCommand = new RelayCommand(() => navigation.Navigate(Key));
    }

    public string Key { get; }
    public string Label { get; }
    public string? IconUri { get; }
    public string Description { get; }
    public ICommand NavigateCommand { get; }
}

public sealed class DashboardViewModel
{
    private readonly IModuleNavigationRegistry _registry;
    private readonly INavigationService _navigation;
    private readonly IAuthorizationService _authorization;
    public DashboardViewModel(IModuleNavigationRegistry registry, INavigationService navigation, IAuthorizationService authorization)
    {
        _registry = registry; _navigation = navigation; _authorization = authorization;
        System.Windows.WeakEventManager<IAuthorizationService, EventArgs>.AddHandler(authorization, nameof(authorization.Changed), OnPermissionsChanged);
        HomeTiles = new ObservableCollection<DashboardTileViewModel>(
            registry.Items
                .Where(item => item.ShowOnHome && NavigationAccess.Allows(authorization.Current, item.Key))
                .Select(item => new DashboardTileViewModel(item, navigation)));

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText = $"Phiên bản: {version?.ToString(3) ?? "1.0.0"}";
    }

    public ObservableCollection<DashboardTileViewModel> HomeTiles { get; }
    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        { System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnPermissionsChanged(sender, e)); return; }
        HomeTiles.Clear();
        foreach (var item in _registry.Items.Where(x => x.ShowOnHome && NavigationAccess.Allows(_authorization.Current, x.Key)))
            HomeTiles.Add(new(item, _navigation));
    }
    public string VersionText { get; }
    public string UpdatedText => "Cập nhật: 25/09/2026";
}
