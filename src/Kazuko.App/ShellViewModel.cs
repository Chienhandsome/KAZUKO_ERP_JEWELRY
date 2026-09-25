using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;
using Kazuko.Core.Modules;
using Kazuko.Core.Mvvm;

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
    private readonly DispatcherTimer _clockTimer;
    private object? _currentView;
    private string _currentTitle = string.Empty;
    private string _dateText = string.Empty;
    private string _timeText = string.Empty;
    private bool _isSidebarCollapsed;
    private bool _disposed;

    public ShellViewModel(
        string displayName,
        IModuleNavigationRegistry registry,
        INavigationService navigation,
        Action logout)
    {
        DisplayName = displayName;
        _registry = registry;
        _navigation = navigation;

        NavigationItems = new ObservableCollection<NavigationItemViewModel>(
            registry.Items
                .Where(item => item.ShowInSidebar)
                .Select(item => new NavigationItemViewModel(item, navigation)));

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarCollapsed = !IsSidebarCollapsed);
        NavigatePriceBoardCommand = new RelayCommand(() => navigation.Navigate("PRICE_BOARD"));
        LogoutCommand = new RelayCommand(logout);

        _navigation.NavigationRequested += OnNavigationRequested;
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

    private void OnClockTick(object? sender, EventArgs e) => UpdateClock();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        DateText = now.ToString("dddd, dd/MM/yyyy", CultureInfo.InvariantCulture);
        TimeText = now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
