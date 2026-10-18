using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;

namespace Kazuko.Modules.Finance;

public sealed class FinanceModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Finance;component/Assets/icons/";

    public string Id => "Kazuko.Finance";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        // Dialog tất toán công nợ dùng chung: mở từ module Công nợ nhưng
        // implementation WPF (CashVoucherDialog) thuộc module Tài chính.
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

        navigation.Register(new NavigationItem(
            "FINANCE",
            "TÀI CHÍNH",
            string.Empty,
            55,
            null,
            () => new FinanceView
            {
                DataContext = new FinanceViewModel(dashboard, vouchers, payables, receivables, session, funds, banks,
                    GetRequiredService<Kazuko.Core.UserManagement.IAuthorizationService>(services))
            },
            AssetRoot + "TC.png",
            "Thu chi tiền, phiếu thu/chi\nvà tổng quan tài chính",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
