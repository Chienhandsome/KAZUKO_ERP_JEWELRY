using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;

namespace Kazuko.Modules.Sales;

public sealed class SalesModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Sales;component/Assets/icons/";

    public string Id => "Kazuko.Sales";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        navigation.Register(new NavigationItem(
            "SALES",
            "BÁN HÀNG",
            string.Empty,
            40,
            null,
            () => new SalesView
            {
                DataContext = new SalesViewModel()
            },
            AssetRoot + "BAN_HANG.png",
            "Quản lý bán hàng,\nđơn hàng, hóa đơn",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }
}
