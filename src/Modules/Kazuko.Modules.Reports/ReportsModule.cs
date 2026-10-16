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
        var fundBalance = GetRequiredService<IFundBalanceReportService>(services);
        var fundCash = GetRequiredService<IFundCashReportService>(services);
        var fundCashReceipts = GetRequiredService<IFundCashReceiptsReportService>(services);
        var fundCashPayments = GetRequiredService<IFundCashPaymentsReportService>(services);
        var masterCustomers = GetRequiredService<IMasterCustomersReportService>(services);
        var masterSuppliers = GetRequiredService<IMasterSuppliersReportService>(services);
        var salesInvoices = GetRequiredService<ISalesInvoicesReportService>(services);
        var salesProfitLoss = GetRequiredService<ISalesProfitLossReportService>(services);
        var salesInvoicesByDay = GetRequiredService<ISalesInvoicesByDayReportService>(services);
        var salesDetailByDay = GetRequiredService<ISalesDetailByDayReportService>(services);
        var salesNewGoldDetail = GetRequiredService<ISalesNewGoldDetailByTypeReportService>(services);
        var salesOldGoldDetail = GetRequiredService<ISalesOldGoldPurchaseDetailByTypeReportService>(services);
        var salesNewGoldSummary = GetRequiredService<ISalesNewGoldSummaryByTypeReportService>(services);
        var salesOldGoldSummary = GetRequiredService<ISalesOldGoldPurchaseSummaryByTypeReportService>(services);
        var salesTopItems = GetRequiredService<ISalesTopItemsReportService>(services);
        var whApproved = GetRequiredService<IWarehouseApprovedReceiptsReportService>(services);
        var whPending = GetRequiredService<IWarehousePendingReceiptsReportService>(services);
        var whByType = GetRequiredService<IWarehouseReceiptsByTypeReportService>(services);

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
                    new JewelryOnHandSummaryReportViewModel(summaryService, session, goldTypes),
                    new FundBalanceReportViewModel(fundBalance, session),
                    new FundCashReportViewModel(fundCash, session),
                    new FundCashReceiptsReportViewModel(fundCashReceipts, session),
                    new FundCashPaymentsReportViewModel(fundCashPayments, session),
                    new MasterCustomersReportViewModel(masterCustomers, session),
                    new MasterSuppliersReportViewModel(masterSuppliers, session),
                    new SalesInvoicesReportViewModel(salesInvoices, session),
                    new SalesProfitLossReportViewModel(salesProfitLoss, session),
                    new SalesInvoicesByDayReportViewModel(salesInvoicesByDay, session),
                    new SalesDetailByDayReportViewModel(salesDetailByDay, session),
                    new SalesNewGoldDetailByTypeReportViewModel(salesNewGoldDetail, session),
                    new SalesOldGoldPurchaseDetailByTypeReportViewModel(salesOldGoldDetail, session),
                    new SalesNewGoldSummaryByTypeReportViewModel(salesNewGoldSummary, session),
                    new SalesOldGoldPurchaseSummaryByTypeReportViewModel(salesOldGoldSummary, session),
                    new SalesTopItemsReportViewModel(salesTopItems, session),
                    new WarehouseApprovedReceiptsReportViewModel(whApproved, session),
                    new WarehousePendingReceiptsReportViewModel(whPending, session),
                    new WarehouseReceiptsByTypeReportViewModel(whByType, session))
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
