using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Finance;

public sealed class FinanceModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Finance;component/Assets/icons/";

    public string Id => "Kazuko.Finance";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        // Dialog tất toán công nợ dùng chung cho các tab Tài chính.
        services.AddSingleton<IDebtSettlementDialogService>(
            new FinanceDebtSettlementDialogService(
                services.GetRequiredService<IFinanceVoucherService>(),
                services.GetRequiredService<ICurrentSessionAccessor>(),
                services.GetRequiredService<ICashFundRepository>(),
                services.GetRequiredService<IBankAccountRepository>()));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var dashboard = GetRequiredService<IFinanceDashboardRepository>(services);
        var vouchers = GetRequiredService<IFinanceVoucherService>(services);
        var payables = GetRequiredService<IPayableDebtRepository>(services);
        var receivables = GetRequiredService<IDebtWorkbenchRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        var funds = GetRequiredService<ICashFundRepository>(services);
        var banks = GetRequiredService<IBankAccountRepository>(services);

        var authorization = GetRequiredService<IAuthorizationService>(services);
        var debtTabs = GetRequiredService<IDebtTabFactory>(services);
        var categories = GetRequiredService<IVoucherCategoryService>(services);

        FinanceView CreateView(bool openDebt) => new(debtTabs, authorization.Current, openDebt)
        {
            DataContext = new FinanceViewModel(dashboard, vouchers, payables, receivables, session,
                funds, banks, authorization, categories)
        };

        navigation.Register(new NavigationItem(
            "FINANCE",
            "TÀI CHÍNH",
            string.Empty,
            55,
            null,
            () => CreateView(openDebt: false),
            AssetRoot + "TC.png",
            "Quản lý thu chi, quỹ tiền\nvà công nợ",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));

        // Giữ lối tắt Trang chủ đã lưu, mở thẳng tab công nợ trong Tài chính.
        navigation.Register(new NavigationItem(
            "DEBT",
            "CÔNG NỢ",
            string.Empty,
            50,
            null,
            () => CreateView(openDebt: true),
            AssetRoot + "TC.png",
            "Theo dõi công nợ tiền và vàng\ntrong Tài chính",
            ShowInSidebar: false,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
