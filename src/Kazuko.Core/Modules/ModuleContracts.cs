using Kazuko.Core.DependencyInjection;

namespace Kazuko.Core.Modules;

public sealed record NavigationItem(
    string Key,
    string Label,
    string IconGlyph,
    int Order,
    string? RequiredPermission,
    Func<object> CreateView);

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
        if (_items.Any(x => string.Equals(x.Key, item.Key, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Navigation key '{item.Key}' is already registered.");

        _items.Add(item);
    }
}

public interface IKazukoModule
{
    string Id { get; }
    Version Version { get; }
    void RegisterServices(IServiceRegistry services);
    void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services);
}
