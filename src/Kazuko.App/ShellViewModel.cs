using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Kazuko.Core.Modules;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;
using Kazuko.Core.Settings;

namespace Kazuko.App;

public sealed class NavigationItemViewModel : ObservableObject
{
    private bool _isActive;

    public NavigationItemViewModel(NavigationItem item, INavigationService navigation)
    {
        Item = item;
        NavigateCommand = new RelayCommand(() => navigation.Navigate(item.Key));
    }

    public NavigationItem Item { get; }
    public ICommand NavigateCommand { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

public sealed class ShellViewModel : ObservableObject, IDisposable
{
    private const double ExpandedSidebarWidth = 330;
    private const double CollapsedSidebarWidth = 88;

    private readonly IModuleNavigationRegistry _registry;
    private readonly INavigationService _navigation;
    private readonly IAuthorizationService _authorization;
    private readonly ITableColumnLayoutSession? _tableLayouts;
    private readonly DispatcherTimer _clockTimer;
    private object? _currentView;
    private string _currentTitle = string.Empty;
    private string _currentKey = "DASHBOARD";
    private string _dateText = string.Empty;
    private string _timeText = string.Empty;
    private bool _isSidebarCollapsed;
    private bool _disposed;
    private bool _refreshingPermissions;
    private DateTime _lastPermissionRefresh = DateTime.MinValue;

    public ShellViewModel(
        string displayName,
        IModuleNavigationRegistry registry,
        INavigationService navigation,
        IAuthorizationService authorization,
        Action logout,
        ITableColumnLayoutSession? tableLayouts = null)
    {
        DisplayName = displayName;
        _registry = registry;
        _navigation = navigation;
        _authorization = authorization;
        _tableLayouts = tableLayouts;
        if (_tableLayouts is not null) _tableLayouts.StatusChanged += OnTableLayoutStatusChanged;

        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
            registry.Items
                .Where(item => item.ShowInSidebar && NavigationAccess.Allows(authorization.Current, item.Key))
                .Select(item => new NavigationItemViewModel(item, navigation)));

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        NavigatePriceBoardCommand = new RelayCommand(() => navigation.Navigate("PRICE_BOARD"), () => authorization.Current.Allows("CAT_GOLD_PRICE"));
        LogoutCommand = new RelayCommand(logout);

        _navigation.NavigationRequested += OnNavigationRequested;
        _authorization.Changed += OnPermissionsChanged;
        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += OnClockTick;
        _clockTimer.Start();
        UpdateClock();

        var first = registry.Items.FirstOrDefault(item => item.Key == "DASHBOARD")
                    ?? registry.Items.FirstOrDefault();
        if (first is not null)
            Navigate(first);
    }

    public string DisplayName { get; }
    public string TableLayoutStatus => _tableLayouts?.StatusMessage ?? "KAZUKO ERP JEWELRY";
    public string TableLayoutDetail => _tableLayouts?.LastError ?? TableLayoutStatus;

    private void OnTableLayoutStatusChanged(object? sender, EventArgs args)
    {
        System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(TableLayoutStatus));
            OnPropertyChanged(nameof(TableLayoutDetail));
        });
    }
    public System.Windows.Visibility PriceBoardVisibility => _authorization.Current.Allows("CAT_GOLD_PRICE")
        ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public ICommand ToggleSidebarCommand { get; }
    public ICommand NavigatePriceBoardCommand { get; }
    public ICommand LogoutCommand { get; }

    public object? CurrentView
    {
        get => _currentView;
        private set => SetProperty(ref _currentView, value);
    }

    public string CurrentTitle
    {
        get => _currentTitle;
        private set => SetProperty(ref _currentTitle, value);
    }

    public string DateText
    {
        get => _dateText;
        private set => SetProperty(ref _dateText, value);
    }

    public string TimeText
    {
        get => _timeText;
        private set => SetProperty(ref _timeText, value);
    }

    public bool IsSidebarCollapsed
    {
        get => _isSidebarCollapsed;
        set
        {
            if (!SetProperty(ref _isSidebarCollapsed, value))
                return;

            OnPropertyChanged(nameof(SidebarWidth));
            OnPropertyChanged(nameof(SidebarToggleGlyph));
            OnPropertyChanged(nameof(SidebarToggleLabel));
            OnPropertyChanged(nameof(SidebarToggleToolTip));
        }
    }

    public double SidebarWidth => IsSidebarCollapsed
        ? CollapsedSidebarWidth
        : ExpandedSidebarWidth;

    public string SidebarToggleGlyph => IsSidebarCollapsed ? "\uE76C" : "\uE76B";

    public string SidebarToggleLabel => IsSidebarCollapsed ? string.Empty : "THU GỌN";

    public string SidebarToggleToolTip => IsSidebarCollapsed
        ? "Mở rộng thanh điều hướng"
        : "Thu gọn thanh điều hướng";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _clockTimer.Stop();
        _clockTimer.Tick -= OnClockTick;
        _navigation.NavigationRequested -= OnNavigationRequested;
        _authorization.Changed -= OnPermissionsChanged;
        if (_tableLayouts is not null) _tableLayouts.StatusChanged -= OnTableLayoutStatusChanged;
    }

    private void OnNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        var item = _registry.Items.FirstOrDefault(
            candidate => string.Equals(candidate.Key, e.Key, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            Navigate(item);
    }

    private void Navigate(NavigationItem item)
    {
        if (!NavigationAccess.Allows(_authorization.Current, item.Key)) return;
        _currentKey = item.Key;
        CurrentTitle = item.Label;
        CurrentView = item.CreateView();
        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsActive = string.Equals(
                navigationItem.Item.Key,
                item.Key,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnPermissionsChanged(object? sender, EventArgs e)
    {
        if (!System.Windows.Application.Current.Dispatcher.CheckAccess())
        { System.Windows.Application.Current.Dispatcher.InvokeAsync(() => OnPermissionsChanged(sender, e)); return; }
        var allowed = _registry.Items.Where(x => x.ShowInSidebar && NavigationAccess.Allows(_authorization.Current, x.Key)).ToArray();
        OnPropertyChanged(nameof(PriceBoardVisibility));
        ((RelayCommand)NavigatePriceBoardCommand).NotifyCanExecuteChanged();
        if (!NavigationItems.Select(x => x.Item.Key).SequenceEqual(allowed.Select(x => x.Key)))
        {
            NavigationItems.Clear();
            foreach (var item in allowed) NavigationItems.Add(new(item, _navigation) { IsActive = item.Key == _currentKey });
        }
    }

    private void OnClockTick(object? sender, EventArgs e)
    {
        UpdateClock();
        if (!_refreshingPermissions && DateTime.UtcNow - _lastPermissionRefresh > TimeSpan.FromSeconds(15))
            _ = RefreshPermissionsAsync();
    }

    private async Task RefreshPermissionsAsync()
    {
        _refreshingPermissions = true;
        _lastPermissionRefresh = DateTime.UtcNow;
        try { await _authorization.RefreshAsync(); }
        catch { _authorization.Clear(); }
        finally { _refreshingPermissions = false; }
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateText = now.ToString("dddd, dd/MM/yyyy", CultureInfo.InvariantCulture);
        TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
