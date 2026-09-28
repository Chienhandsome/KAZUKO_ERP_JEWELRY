using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class WarehouseModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Warehouse;component/Assets/icons/";

    public string Id => "Kazuko.Warehouse";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<IWarehouseRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "WAREHOUSE",
            "KHO",
            string.Empty,
            20,
            null,
            () => new WarehouseView
            {
                DataContext = new WarehouseViewModel(repository, session)
            },
            AssetRoot + "KHO.png",
            "Quản lý nguyên vật liệu\nvà trang sức thành phẩm",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
