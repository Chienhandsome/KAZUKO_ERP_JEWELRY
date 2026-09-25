using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Input;
using Kazuko.Core.Modules;
using Kazuko.Core.Mvvm;

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
    public DashboardViewModel(IModuleNavigationRegistry registry, INavigationService navigation)
    {
        HomeTiles = new ObservableCollection<DashboardTileViewModel>(
            registry.Items
                .Where(item => item.ShowOnHome)
                .Select(item => new DashboardTileViewModel(item, navigation)));

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText = $"Phiên bản: {version?.ToString(3) ?? "1.0.0"}";
    }

    public ObservableCollection<DashboardTileViewModel> HomeTiles { get; }
    public string VersionText { get; }
    public string UpdatedText => "Cập nhật: 25/09/2026";
}
