using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class PriceBoardModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.PriceBoard;component/Assets/icons/";

    public string Id => "Kazuko.PriceBoard";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        var repository = GetRequiredService<IPriceBoardRepository>(services);
        var goldTypes = GetRequiredService<IGoldTypeCatalogService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        services.AddSingleton<ITvDisplayService>(new WindowsTvDisplayService());
        IPriceBoardTvController tv = new PriceBoardTvService(
            GetRequiredService<Kazuko.Core.Company.ICompanyProfileService>(services),session,
            GetRequiredService<Kazuko.Core.UserManagement.IAuthorizationService>(services),
            GetRequiredService<IPriceBoardTvHostClientFactory>(services),
            GetRequiredService<ITvDisplayService>(services));
        services.AddSingleton(tv);
        services.AddSingleton<IPriceBoardDialogService>(
            new PriceBoardDialogService(repository, goldTypes, session, tv));
        services.AddSingleton<IGoldTypeEditorService>(
            new GoldTypeEditorService(goldTypes));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var dialogs = GetRequiredService<IPriceBoardDialogService>(services);
        var nav = GetRequiredService<INavigationService>(services);

        navigation.Register(new NavigationItem(
            "PRICE_BOARD",
            "TỈ GIÁ",
            string.Empty,
            100,
            PriceBoardPermissions.Read,
            () => new PriceBoardLaunchView(dialogs, nav),
            AssetRoot + "TY_GIA.png",
            "Giá vàng, ngoại tệ,\nbảng giá bán",
            ShowInSidebar: false,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
