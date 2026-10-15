using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class WarehouseModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Warehouse;component/Assets/icons/";

    public string Id => "Kazuko.Warehouse";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        var workers = GetRequiredService<IExternalWorkerRepository>(services);
        var craft = GetRequiredService<IOutsourcedCraftService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        services.AddSingleton<IExternalWorkerEditorService>(
            new ExternalWorkerEditorService(workers, craft, session));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<IWarehouseRepository>(services);
        var warehouseGoldPrices = GetRequiredService<IWarehouseGoldPriceRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        var navigationService = GetRequiredService<INavigationService>(services);
        var materialReceipts = GetRequiredService<IMaterialReceiptService>(services);
        var finishedProductTransfers = GetRequiredService<IFinishedProductTransferService>(services);
        var suppliers = GetRequiredService<ISupplierRepository>(services);
        var supplierEditor = GetRequiredService<ISupplierEditorService>(services);
        var goldTypeEditor = GetRequiredService<IGoldTypeEditorService>(services);
        var funds = GetRequiredService<ICashFundRepository>(services);
        var banks = GetRequiredService<IBankAccountRepository>(services);
        var goldTypes = GetRequiredService<IGoldTypeCatalogService>(services);
        var productionQueries = GetRequiredService<IProductionQueryRepository>(services);
        var productionApplication = GetRequiredService<IProductionApplicationService>(services);
        var workerEditor = GetRequiredService<IExternalWorkerEditorService>(services);

        navigation.Register(new NavigationItem(
            "WAREHOUSE",
            "KHO",
            string.Empty,
            20,
            null,
            () => new WarehouseView
            {
                DataContext = new WarehouseViewModel(
                    repository,
                    warehouseGoldPrices,
                    session,
                    materialReceipts,
                    finishedProductTransfers,
                    suppliers,
                    supplierEditor,
                    goldTypeEditor,
                    funds,
                    banks,
                    goldTypes,
                    productionQueries,
                    productionApplication)
            },
            AssetRoot + "KHO.png",
            "Quản lý nguyên vật liệu\nvà trang sức thành phẩm",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));

        navigation.Register(new NavigationItem(
            "WAREHOUSE_GOLD_PRICES",
            "GIÁ VÀNG KHO",
            string.Empty,
            25,
            null,
            () => new WarehouseGoldPriceLaunchView(warehouseGoldPrices, session, navigationService),
            AssetRoot + "KHO.png",
            "Cập nhật giá mua và\ngiá bán vàng trong kho",
            ShowInSidebar: false,
            ShowOnHome: false,
            IsPlaceholder: false));

        navigation.Register(new NavigationItem(
            "WAREHOUSE_EXTERNAL_WORKERS",
            "THỢ NGOÀI",
            string.Empty,
            26,
            null,
            () => new ExternalWorkerLaunchView(workerEditor, navigationService),
            AssetRoot + "KHO.png",
            "Quản lý danh mục thợ ngoài\ncho lệnh sản xuất OUTSOURCED",
            ShowInSidebar: false,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
