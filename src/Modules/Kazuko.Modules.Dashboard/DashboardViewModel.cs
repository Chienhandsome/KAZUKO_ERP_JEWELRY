using System.Collections.ObjectModel;
using System.Reflection;
using System.IO;
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

public sealed class DashboardShortcutOption : ObservableObject
{
    private readonly DashboardViewModel _owner;
    private bool _isSelected;

    internal DashboardShortcutOption(NavigationItem item, bool selected, DashboardViewModel owner)
    {
        Item = item; _isSelected = selected; _owner = owner;
    }

    internal NavigationItem Item { get; }
    public string Key => Item.Key;
    public string Label => Item.Label;
    public bool IsSelected
    {
        get => _isSelected;
        set => _owner.SetShortcutSelected(this, value);
    }
    public bool CanToggle => IsSelected || _owner.HomeTiles.Count < DashboardViewModel.MaximumShortcuts;
    internal void UpdateSelected(bool selected) => SetProperty(ref _isSelected, selected, nameof(IsSelected));
    internal void NotifySelectionRejected() => OnPropertyChanged(nameof(IsSelected));
    internal void NotifyLimitChanged() => OnPropertyChanged(nameof(CanToggle));
}

public sealed class DashboardViewModel : ObservableObject
{
    public const int MaximumShortcuts = 10;
    private readonly IModuleNavigationRegistry _registry;
    private readonly INavigationService _navigation;
    private readonly IAuthorizationService _authorization;
    private readonly IDashboardShortcutStore _shortcuts;
    private readonly long _userId;
    private HashSet<string> _selectedKeys;
    private bool _isShortcutSettingsOpen;
    private string _shortcutError = string.Empty;

    public DashboardViewModel(IModuleNavigationRegistry registry, INavigationService navigation,
        IAuthorizationService authorization, IDashboardShortcutStore shortcuts, long userId)
    {
        _registry = registry; _navigation = navigation; _authorization = authorization;
        _shortcuts = shortcuts; _userId = userId;
        var saved = shortcuts.Load(userId);
        _selectedKeys = new HashSet<string>(saved ?? registry.Items
            .Where(item => item.ShowOnHome && NavigationAccess.Allows(authorization.Current, item.Key))
            .Take(MaximumShortcuts).Select(item => item.Key).ToArray(), StringComparer.OrdinalIgnoreCase);
        System.Windows.WeakEventManager<IAuthorizationService, EventArgs>.AddHandler(authorization, nameof(authorization.Changed), OnPermissionsChanged);
        RefreshShortcuts();

        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText = $"Phiên bản: {version?.ToString(3) ?? "1.0.0"}";
    }

    public ObservableCollection<DashboardTileViewModel> HomeTiles { get; } = [];
    public ObservableCollection<DashboardShortcutOption> ShortcutOptions { get; } = [];
    public string ShortcutCountText => $"Đã chọn {HomeTiles.Count}/{MaximumShortcuts} shortcut";
    public bool HasNoShortcuts => HomeTiles.Count == 0;
    public string ShortcutError { get => _shortcutError; private set => SetProperty(ref _shortcutError, value); }
    public bool IsShortcutSettingsOpen
    {
        get => _isShortcutSettingsOpen;
        set => SetProperty(ref _isShortcutSettingsOpen, value);
    }

    private void RefreshShortcuts()
    {
        ShortcutOptions.Clear();
        var count = 0;
        foreach (var item in _registry.Items.Where(item => item.Key != "DASHBOARD"
            && (item.ShowOnHome || item.ShowInSidebar) && NavigationAccess.Allows(_authorization.Current, item.Key)))
        {
            var selected = _selectedKeys.Contains(item.Key) && count < MaximumShortcuts;
            if (selected) count++;
            ShortcutOptions.Add(new DashboardShortcutOption(item, selected, this));
        }
        UpdateTiles();
    }

    internal void SetShortcutSelected(DashboardShortcutOption option, bool selected)
    {
        if (option.IsSelected == selected || !ShortcutOptions.Contains(option)) return;
        if (selected && (HomeTiles.Count >= MaximumShortcuts || !NavigationAccess.Allows(_authorization.Current, option.Key))) return;
        // Retain choices for temporarily unavailable modules/permissions.
        var keys = new HashSet<string>(_selectedKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var current in ShortcutOptions) keys.Remove(current.Key);
        foreach (var current in ShortcutOptions.Where(x => x == option ? selected : x.IsSelected)) keys.Add(current.Key);
        try
        {
            _shortcuts.Save(_userId, keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShortcutError = "Không thể lưu thiết lập. Vui lòng thử lại.";
            option.NotifySelectionRejected();
            return;
        }
        ShortcutError = string.Empty;
        _selectedKeys = keys;
        option.UpdateSelected(selected);
        UpdateTiles();
    }

    private void UpdateTiles()
    {
        HomeTiles.Clear();
        foreach (var option in ShortcutOptions.Where(x => x.IsSelected).Take(MaximumShortcuts))
            HomeTiles.Add(new DashboardTileViewModel(option.Item, _navigation));
        foreach (var option in ShortcutOptions) option.NotifyLimitChanged();
        OnPropertyChanged(nameof(ShortcutCountText));
        OnPropertyChanged(nameof(HasNoShortcuts));
    }

    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        { dispatcher.InvokeAsync(() => OnPermissionsChanged(sender, e)); return; }
        RefreshShortcuts();
    }
    public string VersionText { get; }
    public string UpdatedText => "Cập nhật: 25/09/2026";
}
