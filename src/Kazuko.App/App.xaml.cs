using System.Windows;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Infrastructure.Printing;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Reports;
using Kazuko.Core.Sales;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Updates;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Authentication;
using Kazuko.Infrastructure.Company;
using Kazuko.Infrastructure.Customers;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.HumanResources;
using Kazuko.Infrastructure.PriceBoard;
using Kazuko.Infrastructure.Production;
using Kazuko.Infrastructure.Reports;
using Kazuko.Infrastructure.Sales;
using Kazuko.Infrastructure.Settings;
using Kazuko.Infrastructure.Suppliers;
using Kazuko.Infrastructure.Updates;
using Kazuko.Infrastructure.Updates.Activation;
using Kazuko.Infrastructure.Warehouse;
using Kazuko.Modules.Authentication;
using Kazuko.Modules.Customers;
using Kazuko.Modules.Dashboard;
using Kazuko.Modules.Debt;
using Kazuko.Modules.HumanResources;
using Kazuko.Modules.PriceBoard;
using Kazuko.Modules.Production;
using Kazuko.Modules.Reports;
using Kazuko.Modules.Sales;
using Kazuko.Modules.Suppliers;
using Kazuko.Modules.Warehouse;
using Kazuko.Shared.UI.Services;

namespace Kazuko.App;

public partial class App : Application
{
    private readonly ServiceRegistry _services = new();
    private readonly ModuleNavigationRegistry _navigation = new();
    private readonly ICurrentSessionAccessor _sessionAccessor = new CurrentSessionAccessor();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterFoundation();
        RegisterModules();
        ApplySavedAppearance();
        ShowLogin();
    }

    private void RegisterFoundation()
    {
        IPreferencesService preferences = new JsonPreferencesService();
        IAppearanceService appearance = new AppearanceService(preferences);
        ICompanyProfileService companyProfile = new FirebirdCompanyProfileService();
        IPrintTemplateStore printTemplates = new FirebirdPrintTemplateStore();
        IAuthenticationService authentication = new FirebirdAuthenticationService();
        INavigationService navigation = new NavigationService();
        var moduleLayout = ModuleLayoutPaths.CreateDefaultLocalAppData();
        moduleLayout.EnsureCreated();
        IModulePackageInstaller packageInstaller = new ModulePackageInstaller(moduleLayout.StagingRoot);
        IModuleActivationLockFactory activationLocks = new ModuleActivationLockFactory(moduleLayout);
        IModuleActivationCatalog activationCatalog = new ModuleActivationCatalog(
            moduleLayout,
            activationLocks,
            new FixedHostVersionProvider(1));
        ICustomerRepository customers = new FirebirdCustomerRepository();
        ISupplierRepository suppliers = new FirebirdSupplierRepository();
        IWarehouseRepository warehouse = new FirebirdWarehouseRepository();
        IWarehouseGoldPriceRepository warehouseGoldPrices = new FirebirdWarehouseGoldPriceRepository();
        IHumanResourcesRepository humanResources = new FirebirdHumanResourcesRepository();
        IPriceBoardRepository priceBoard = new FirebirdPriceBoardRepository();
        IGoldTypeCatalogService goldTypeCatalog = new FirebirdGoldTypeCatalogService();
        IMaterialReceiptService materialReceipts = new FirebirdMaterialReceiptService();
        IMaterialIssueService materialIssues = new FirebirdMaterialIssueService();
        IOutsourcedCraftService outsourcedCraft = new FirebirdOutsourcedCraftService();
        IFinishedGoodsIssueService finishedGoodsIssue = new FirebirdFinishedGoodsIssueService();
        IExternalWorkerRepository externalWorkers = new FirebirdExternalWorkerRepository();
        IStockTransferService stockTransfers = new FirebirdStockTransferService();
        IFinishedProductTransferService finishedProductTransfers = new FirebirdFinishedProductTransferService();
        IStockAdjustmentService stockAdjustments = new FirebirdStockAdjustmentService();
        ICashFundRepository cashFunds = new FirebirdCashFundRepository();
        IBankAccountRepository bankAccounts = new FirebirdBankAccountRepository();
        IPayableDebtRepository payableDebts = new FirebirdPayableDebtRepository();
        IDebtWorkbenchRepository debtWorkbench = new FirebirdDebtWorkbenchRepository();
        IFinancialVoucherPostingService financialVouchers = new FirebirdFinancialVoucherPostingService();
        IFinancialSettlementService financialSettlements = new FirebirdFinancialSettlementService();
        IReceivableDebtRepository receivableDebts = new FirebirdReceivableDebtRepository();
        IDocumentReversalService documentReversals = new FirebirdDocumentReversalService();
        IPartyCreditService partyCredits = new FirebirdPartyCreditService();
        IProductionQueryRepository productionQueries = new FirebirdProductionQueryRepository();
        IProductionAttachmentStore productionAttachments = new LocalProductionAttachmentStore();
        IProductionApplicationService productionApplication =
            new FirebirdProductionApplicationService(productionAttachments);
        IProductionPrintDataService productionPrintData = new FirebirdProductionPrintDataService();
        IJewelryOnHandReportService jewelryOnHandReport = new FirebirdJewelryOnHandReportService();
        IJewelryOnHandSummaryReportService jewelryOnHandSummaryReport =
            new FirebirdJewelryOnHandSummaryReportService();
        ISalesCheckoutApplicationService salesCheckout = new FirebirdSalesCheckoutApplicationService();
        ISalesQueryRepository salesQueries = new FirebirdSalesQueryRepository();
        ISalesAuthorizationService salesAuthorization = new FirebirdSalesAuthorizationService();
        ISalesPrintDataService salesPrintData = new FirebirdSalesPrintDataService(salesQueries);
        ICraftOrderApplicationService craftOrders = new FirebirdCraftOrderApplicationService();

        _services.AddSingleton(preferences);
        _services.AddSingleton(appearance);
        _services.AddSingleton(companyProfile);
        _services.AddSingleton(printTemplates);
        _services.AddSingleton(authentication);
        _services.AddSingleton(navigation);
        _services.AddSingleton(packageInstaller);
        _services.AddSingleton<IModuleLayoutPaths>(moduleLayout);
        _services.AddSingleton(activationLocks);
        _services.AddSingleton(activationCatalog);
        _services.AddSingleton<IModuleNavigationRegistry>(_navigation);
        _services.AddSingleton(_sessionAccessor);
        _services.AddSingleton(customers);
        _services.AddSingleton(suppliers);
        _services.AddSingleton(warehouse);
        _services.AddSingleton(warehouseGoldPrices);
        _services.AddSingleton(humanResources);
        _services.AddSingleton(priceBoard);
        _services.AddSingleton(goldTypeCatalog);
        _services.AddSingleton(materialReceipts);
        _services.AddSingleton(materialIssues);
        _services.AddSingleton(outsourcedCraft);
        _services.AddSingleton(finishedGoodsIssue);
        _services.AddSingleton(externalWorkers);
        _services.AddSingleton(stockTransfers);
        _services.AddSingleton(finishedProductTransfers);
        _services.AddSingleton(stockAdjustments);
        _services.AddSingleton(cashFunds);
        _services.AddSingleton(bankAccounts);
        _services.AddSingleton(payableDebts);
        _services.AddSingleton(debtWorkbench);
        _services.AddSingleton(financialVouchers);
        _services.AddSingleton(financialSettlements);
        _services.AddSingleton(receivableDebts);
        _services.AddSingleton(documentReversals);
        _services.AddSingleton(partyCredits);
        _services.AddSingleton(productionQueries);
        _services.AddSingleton(productionApplication);
        _services.AddSingleton(productionAttachments);
        _services.AddSingleton(productionPrintData);
        _services.AddSingleton(jewelryOnHandReport);
        _services.AddSingleton(jewelryOnHandSummaryReport);
        _services.AddSingleton(salesCheckout);
        _services.AddSingleton(salesQueries);
        _services.AddSingleton(craftOrders);
        _services.AddSingleton(salesAuthorization);
        _services.AddSingleton(salesPrintData);
    }

    private void RegisterModules()
    {
        IKazukoModule[] modules =
        [
            new DashboardModule(),
            new ProductionModule(),
            new SalesModule(),
            new CustomersModule(),
            new SuppliersModule(),
            new PriceBoardModule(),
            new WarehouseModule(),
            new DebtModule(),
            new HumanResourcesModule(),
            new ReportsModule()
        ];
        foreach (var module in modules)
        {
            module.RegisterServices(_services);
            module.RegisterNavigation(_navigation, _services);
        }
    }

    private void ApplySavedAppearance()
    {
        var appearance = _services.GetRequiredService<IAppearanceService>();
        appearance.Apply(appearance.Current.Theme, appearance.Current.FontScale);
    }

    private void ShowLogin()
    {
        _sessionAccessor.Clear();
        var viewModel = new LoginViewModel(_services.GetRequiredService<IAuthenticationService>());
        var loginView = new LoginView { DataContext = viewModel };
        var window = new LoginWindow { Content = loginView };

        viewModel.SignedIn += (_, session) =>
        {
            _sessionAccessor.Set(session);
            MainWindow? shell = null;
            var shellViewModel = new ShellViewModel(
                session.DisplayName,
                _navigation,
                _services.GetRequiredService<INavigationService>(),
                () =>
                {
                    _sessionAccessor.Clear();
                    ShowLogin();
                    shell?.Close();
                });
            shell = new MainWindow { DataContext = shellViewModel };
            MainWindow = shell;
            shell.Show();
            window.Close();
        };

        window.Show();
    }
}
