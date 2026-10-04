using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
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
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<IWarehouseRepository>(services);
        var warehouseGoldPrices = GetRequiredService<IWarehouseGoldPriceRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        var materialReceipts = GetRequiredService<IMaterialReceiptService>(services);
        var materialIssues = GetRequiredService<IMaterialIssueService>(services);
        var outsourcedCraft = GetRequiredService<IOutsourcedCraftService>(services);
        var finishedGoods = GetRequiredService<IFinishedGoodsIssueService>(services);
        var finishedProductTransfers = GetRequiredService<IFinishedProductTransferService>(services);
        var externalWorkers = GetRequiredService<IExternalWorkerRepository>(services);
        var suppliers = GetRequiredService<ISupplierRepository>(services);
        var customers = GetRequiredService<ICustomerRepository>(services);
        var supplierEditor = GetRequiredService<ISupplierEditorService>(services);
        var customerEditor = GetRequiredService<ICustomerEditorService>(services);
        var funds = GetRequiredService<ICashFundRepository>(services);
        var banks = GetRequiredService<IBankAccountRepository>(services);

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
                    materialIssues,
                    outsourcedCraft,
                    finishedGoods,
                    finishedProductTransfers,
                    externalWorkers,
                    suppliers,
                    customers,
                    supplierEditor,
                    customerEditor,
                    funds,
                    banks)
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
