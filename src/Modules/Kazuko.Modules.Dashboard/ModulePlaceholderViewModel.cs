using Kazuko.Core.Modules;

namespace Kazuko.Modules.Dashboard;

public sealed class ModulePlaceholderViewModel(NavigationItem item)
{
    public string Label { get; } = item.Label;
    public string? IconUri { get; } = item.IconUri;
    public string Description { get; } = (item.Description ?? string.Empty).Replace('\n', ' ');
}
