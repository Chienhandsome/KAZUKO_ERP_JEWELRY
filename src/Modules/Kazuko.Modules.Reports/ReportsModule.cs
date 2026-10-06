using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class ReportsModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Reports;component/Assets/icons/";

    public string Id => "Kazuko.Reports";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var reportService = GetRequiredService<IJewelryOnHandReportService>(services);
        var summaryService = GetRequiredService<IJewelryOnHandSummaryReportService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        var goldTypes = GetRequiredService<IGoldTypeCatalogService>(services);

        navigation.Register(new NavigationItem(
            "REPORT",
            "BÁO CÁO",
            string.Empty,
            90,
            null,
            () => new ReportsView
            {
                DataContext = new ReportsViewModel(
                    new JewelryOnHandReportViewModel(reportService, session, goldTypes),
                    new JewelryOnHandSummaryReportViewModel(summaryService, session, goldTypes))
            },
            AssetRoot + "BC.png",
            "Thống kê, báo cáo\ntổng hợp",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
