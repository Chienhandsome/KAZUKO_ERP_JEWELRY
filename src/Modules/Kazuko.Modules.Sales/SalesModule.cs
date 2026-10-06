using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales;

public sealed class SalesModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Sales;component/Assets/icons/";

    public string Id => "Kazuko.Sales";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        services.AddSingleton<ISalesPaymentDialogService>(new WpfSalesPaymentDialogService());
        var printData = GetRequiredService<ISalesPrintDataService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        services.AddSingleton<ISalesReceiptPresenter>(
            new WpfSalesReceiptPresenter(printData, session, new SalesReceiptPrintService()));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        navigation.Register(new NavigationItem(
            "SALES",
            "BÁN HÀNG",
            string.Empty,
            40,
            SalesPermissions.CheckoutPost,
            () => new SalesView
            {
                DataContext = CreateViewModel(services)
            },
            AssetRoot + "BAN_HANG.png",
            "Quản lý bán hàng,\nđơn hàng, hóa đơn",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static SalesViewModel CreateViewModel(IServiceProvider services) =>
        new(
            GetRequiredService<ISalesQueryRepository>(services),
            GetRequiredService<ISalesCheckoutApplicationService>(services),
            GetRequiredService<ISalesAuthorizationService>(services),
            GetRequiredService<ICashFundRepository>(services),
            GetRequiredService<IBankAccountRepository>(services),
            GetRequiredService<ICurrentSessionAccessor>(services),
            GetRequiredService<ISalesPaymentDialogService>(services),
            GetRequiredService<ISalesReceiptPresenter>(services));

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
