using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.Printing;

namespace Kazuko.Modules.Dashboard;

public sealed class DashboardModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Dashboard;component/Assets/icons/";

    public string Id => "Kazuko.Dashboard";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var navigationService = GetRequiredService<INavigationService>(services);

        navigation.Register(new NavigationItem(
            "DASHBOARD",
            "TRANG CHỦ",
            "\uE80F",
            0,
            null,
            () => new DashboardView
            {
                DataContext = new DashboardViewModel(navigation, navigationService)
            }));

        var placeholders = new[]
        {
            Feature("PRODUCTION", "SẢN XUẤT", "SX.png", 10,
                "Quản lý quy trình\nsản xuất trang sức", true),
            Feature("WAREHOUSE", "KHO", "KHO.png", 20,
                "Quản lý nguyên vật liệu\nvà trang sức thành phẩm", true),
            Feature("SUPPLIER", "NHÀ CUNG CẤP", "hop.png", 30,
                "Danh mục và giao dịch\nnhà cung cấp", false),
            Feature("SALES", "BÁN HÀNG", "BAN_HANG.png", 40,
                "Quản lý bán hàng,\nđơn hàng, hóa đơn", true),
            Feature("DEBT", "CÔNG NỢ", "TC.png", 50,
                "Theo dõi công nợ\ntiền và vàng", false),
            Feature("CUSTOMER", "KHÁCH HÀNG", "KH.png", 60,
                "Quản lý thông tin\nkhách hàng", true),
            Feature("HR", "NHÂN SỰ", "NHAN_SU.png", 80,
                "Quản lý nhân viên,\nchấm công, lương", true),
            Feature("REPORT", "BÁO CÁO", "BC.png", 90,
                "Thống kê, báo cáo\ntổng hợp", true),
            Feature("PRICE_BOARD", "TỈ GIÁ", "TY_GIA.png", 100,
                "Giá vàng, ngoại tệ,\nbảng giá bán", true, showInSidebar: false)
        };

        foreach (var item in placeholders)
        {
            navigation.Register(item with
            {
                CreateView = () => new ModulePlaceholderView
                {
                    DataContext = new ModulePlaceholderViewModel(item)
                }
            });
        }

        var companyProfile = GetRequiredService<ICompanyProfileService>(services);
        var sessionAccessor = GetRequiredService<ICurrentSessionAccessor>(services);
        var appearanceService = GetRequiredService<IAppearanceService>(services);
        var printTemplates = GetRequiredService<IPrintTemplateStore>(services);

        navigation.Register(new NavigationItem(
            "SETTINGS",
            "CÀI ĐẶT",
            string.Empty,
            110,
            null,
            () => new SettingsView
            {
                DataContext = new SettingsViewModel(
                    companyProfile, sessionAccessor, appearanceService, printTemplates)
            },
            AssetRoot + "CAIDAT.png"));
    }

    private static NavigationItem Feature(
        string key,
        string label,
        string iconFile,
        int order,
        string description,
        bool showOnHome,
        bool showInSidebar = true) =>
        new(
            key,
            label,
            string.Empty,
            order,
            null,
            () => throw new InvalidOperationException("Feature view factory has not been assigned."),
            AssetRoot + iconFile,
            description,
            showInSidebar,
            showOnHome,
            IsPlaceholder: true);

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
