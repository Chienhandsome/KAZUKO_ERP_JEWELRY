using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;

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
        var debts = GetRequiredService<IPayableDebtRepository>(services);
        var funds = GetRequiredService<ICashFundRepository>(services);
        var banks = GetRequiredService<IBankAccountRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "DEBT",
            "CÔNG NỢ",
            string.Empty,
            50,
            null,
            () => new DebtView
            {
                DataContext = new DebtViewModel(debts, funds, banks, session)
            },
            AssetRoot + "TC.png",
            "Theo dõi và thanh toán\ncông nợ phải trả",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
