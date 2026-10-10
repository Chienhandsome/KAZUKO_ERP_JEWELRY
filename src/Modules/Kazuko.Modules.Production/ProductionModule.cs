using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Production;

public sealed class ProductionModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Production;component/Assets/icons/";

    public string Id => "Kazuko.Production";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        // Resolve deps (incl. IExternalWorkerEditorService from Warehouse) lazily on ShowCreate.
        services.AddSingleton<IProductionOrderEditorService>(
            new Services.ProductionOrderEditorService(services));
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
                var goldTypes = GetRequiredService<IGoldTypeCatalogService>(services);
                var craftOrders = GetRequiredService<ICraftOrderApplicationService>(services);
                var cashFunds = GetRequiredService<ICashFundRepository>(services);
                var bankAccounts = GetRequiredService<IBankAccountRepository>(services);
                var customerEditor = GetRequiredService<ICustomerEditorService>(services);
                return new ProductionView
                {
                    DataContext = new ProductionViewModel(
                        queries, application, attachments, printData, session, externalWorkerEditor, goldTypes,
                        craftOrders, cashFunds, bankAccounts, customerEditor)
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
