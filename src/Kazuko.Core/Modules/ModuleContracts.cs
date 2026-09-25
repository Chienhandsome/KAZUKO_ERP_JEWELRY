using Kazuko.Core.DependencyInjection;

namespace Kazuko.Core.Modules;

public sealed record NavigationItem(
    string Key,
    string Label,
    string IconGlyph,
    int Order,
    string? RequiredPermission,
    Func<object> CreateView,
    string? IconUri = null,
    string? Description = null,
    bool ShowInSidebar = true,
    bool ShowOnHome = false,
    bool IsPlaceholder = false);

public interface IModuleNavigationRegistry
{
    IReadOnlyList<NavigationItem> Items { get; }
    void Register(NavigationItem item);
}

public sealed class ModuleNavigationRegistry : IModuleNavigationRegistry
{
    private readonly List<NavigationItem> _items = [];
    public IReadOnlyList<NavigationItem> Items => _items.OrderBy(x => x.Order).ToArray();

    public void Register(NavigationItem item)
    {
        var existingIndex = _items.FindIndex(
            x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
        {
            var existing = _items[existingIndex];
            if (existing.IsPlaceholder && !item.IsPlaceholder)
            {
                _items[existingIndex] = item;
                return;
            }

            if (!existing.IsPlaceholder && item.IsPlaceholder)
                return;

            throw new InvalidOperationException($"Navigation key '{item.Key}' is already registered.");
        }

        _items.Add(item);
    }
}

public sealed class NavigationRequestedEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}

public interface INavigationService
{
    event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    void Navigate(string key);
}

public sealed class NavigationService : INavigationService
{
    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;

    public void Navigate(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs(key));
    }
}

public interface IKazukoModule
{
    string Id { get; }
    Version Version { get; }
    void RegisterServices(IServiceRegistry services);
    void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services);
}
