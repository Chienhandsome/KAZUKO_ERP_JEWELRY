using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SuppliersModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Suppliers;component/Assets/icons/";

    public string Id => "Kazuko.Suppliers";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        var repository = GetRequiredService<ISupplierRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        services.AddSingleton<ISupplierEditorService>(new SupplierEditorService(repository, session));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<ISupplierRepository>(services);
        var editor = GetRequiredService<ISupplierEditorService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "SUPPLIER",
            "NHÀ CUNG CẤP",
            string.Empty,
            30,
            null,
            () => new SupplierView
            {
                DataContext = new SupplierViewModel(repository, editor, session)
            },
            AssetRoot + "hop.png",
            "Danh mục và công nợ\nnhà cung cấp",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
