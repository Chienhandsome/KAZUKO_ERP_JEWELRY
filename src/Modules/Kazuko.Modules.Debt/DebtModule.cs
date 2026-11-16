using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Kazuko.Core.Suppliers;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Debt;

public sealed class DebtModule : IKazukoModule
{
    public string Id => "Kazuko.Debt";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        services.AddSingleton<IDebtTabFactory>(new DebtTabFactory(services));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
    }

    private sealed class DebtTabFactory(IServiceRegistry services) : IDebtTabFactory
    {
        public object CreateView(DebtTabKind tab) =>
            new DebtView(new DebtViewModel(
                services.GetRequiredService<IDebtWorkbenchRepository>(),
                services.GetRequiredService<ICustomerRepository>(),
                services.GetRequiredService<ICustomerEditorService>(),
                services.GetRequiredService<ISupplierEditorService>(),
                services.GetRequiredService<ICurrentSessionAccessor>(),
                services.GetRequiredService<IDebtSettlementDialogService>(),
                tab,
                services.GetRequiredService<IAuthorizationService>()), tab);
    }
}
