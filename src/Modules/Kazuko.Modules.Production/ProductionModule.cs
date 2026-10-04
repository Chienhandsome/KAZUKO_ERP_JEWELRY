using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Production;

public sealed class ProductionModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Production;component/Assets/icons/";

    public string Id => "Kazuko.Production";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var queries = GetRequiredService<IProductionQueryRepository>(services);
        var application = GetRequiredService<IProductionApplicationService>(services);
        var attachments = GetRequiredService<IProductionAttachmentStore>(services);
        var printData = GetRequiredService<IProductionPrintDataService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "PRODUCTION",
            "SẢN XUẤT",
            string.Empty,
            10,
            ProductionPermissions.OrderRead,
            () =>
            {
                var externalWorkerEditor = GetRequiredService<IExternalWorkerEditorService>(services);
                return new ProductionView
                {
                    DataContext = new ProductionViewModel(
                        queries, application, attachments, printData, session, externalWorkerEditor)
                };
            },
            AssetRoot + "SX.png",
            "Quản lý quy trình\nsản xuất trang sức",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
