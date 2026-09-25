using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;

namespace Kazuko.Modules.Dashboard;

public sealed class DashboardModule : IKazukoModule
{
    public string Id => "Kazuko.Dashboard";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services) =>
        navigation.Register(new NavigationItem(
            "DASHBOARD",
            "Tổng quan",
            "⌂",
            0,
            null,
            () => new DashboardView()));
}
