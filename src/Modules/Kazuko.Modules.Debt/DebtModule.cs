using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Debt;

public sealed class DebtModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Debt;component/Assets/icons/";

    public string Id => "Kazuko.Debt";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var workbench = GetRequiredService<IDebtWorkbenchRepository>(services);
        var funds = GetRequiredService<ICashFundRepository>(services);
        var banks = GetRequiredService<IBankAccountRepository>(services);
        var customers = GetRequiredService<ICustomerRepository>(services);
        var customerEditor = GetRequiredService<ICustomerEditorService>(services);
        var supplierEditor = GetRequiredService<ISupplierEditorService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "DEBT",
            "CÔNG NỢ",
            string.Empty,
            50,
            null,
            () => new DebtView
            {
                DataContext = new DebtViewModel(
                    workbench, funds, banks, customers, customerEditor, supplierEditor, session)
            },
            AssetRoot + "TC.png",
            "Theo dõi công nợ khách hàng\nvà nhà cung cấp",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
