using System.Windows;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Settings;
using Kazuko.Infrastructure.Data;
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
using Kazuko.Core.UserManagement;
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
using Kazuko.Infrastructure.UserManagement;
using Kazuko.Infrastructure.Updates;
using Kazuko.Infrastructure.Updates.Activation;
using Kazuko.Infrastructure.Warehouse;
using Kazuko.Modules.Authentication;
using Kazuko.Modules.Customers;
using Kazuko.Modules.Dashboard;
using Kazuko.Modules.Debt;
using Kazuko.Modules.Finance;
using Kazuko.Modules.HumanResources;
using Kazuko.Modules.PriceBoard;
using Kazuko.Modules.Production;
using Kazuko.Modules.Reports;
using Kazuko.Modules.Sales;
using Kazuko.Modules.Suppliers;
using Kazuko.Modules.UserManagement;
using Kazuko.Modules.Warehouse;
using Kazuko.Shared.UI.Behaviors;
using Kazuko.Shared.UI.Services;
using Kazuko.Shared.UI.DataGridLayout;
using Kazuko.App.PriceBoardHost;

namespace Kazuko.App;

public partial class App : Application
{
    private readonly ServiceRegistry _services = new();
    private readonly ModuleNavigationRegistry _navigation = new();
    private readonly ICurrentSessionAccessor _sessionAccessor = new CurrentSessionAccessor();
    private IDatabaseManagementService _backgroundDatabase = null!;
    private DatabaseTableColumnLayoutSession? _tableLayouts;
    private PriceBoardHostRuntime? _priceBoardHost;
    private Mutex? _priceBoardHostMutex;
    private bool _hostMode, _ownsHostMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.FirstOrDefault() == "--price-board-host")
        {
            _hostMode=true;
            ShutdownMode=ShutdownMode.OnExplicitShutdown;
            try
            {
                if(e.Args.Length!=2)throw new ArgumentException("Thiếu phạm vi bảng giá TV.");
                var scope=e.Args[1];
                var pipe=PriceBoardHostProtocol.PipeName(scope);
                _priceBoardHostMutex=new Mutex(true,"Local\\"+pipe,out _ownsHostMutex);
                if(!_ownsHostMutex){Shutdown();return;}
                var session=new CurrentSessionAccessor();
                _priceBoardHost=new PriceBoardHostRuntime(scope,new FirebirdTvPriceSource(),new FirebirdCompanyProfileService(),
                    new JsonPriceBoardTvSettingsStore(Environment.GetEnvironmentVariable("KAZUKO_TV_SETTINGS_ROOT")),
                    new PriceBoardTvServerFactory(),new WindowsTvDisplayService(),session,new FirebirdAuthorizationService(session),()=>Shutdown());
            }
            catch(Exception ex){System.Diagnostics.Trace.TraceError("TV host startup failed: {0}",ex.Message);Shutdown(1);}
            return;
        }
        TextBoxSelectAllBehavior.Register();
        Kazuko.Shared.UI.DataGridLayout.DataGridColumnLayoutStore.Configure(new JsonTableColumnLayoutStore());
        RegisterFoundation();
        RegisterModules();
        ApplySavedAppearance();
        _ = RunAutoBackupAsync();
        ShowLogin();
    }

    /// <summary>
    /// Backup tự động DB khi khởi động (nếu được bật trong Cài đặt &gt; Cơ sở dữ liệu).
    /// Chạy nền, không chặn đăng nhập; kết quả/lỗi được lưu để xem trong Cài đặt.
    /// </summary>
    private async Task RunAutoBackupAsync()
    {
        try
        {
            var database = _backgroundDatabase;
            if (database is not null)
                await database.RunAutoBackupIfEnabledAsync();
        }
        catch
        {
            // Backup tự động không được làm gián đoạn khởi động.
        }
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
        _services.AddSingleton<IBusinessTaxLookupService>(new VietQrBusinessTaxLookupService(
            new System.Net.Http.HttpClient(new System.Net.Http.HttpClientHandler { AllowAutoRedirect = false })));
        IWarehouseRepository warehouse = new FirebirdWarehouseRepository();
        IWarehouseGoldPriceRepository warehouseGoldPrices = new FirebirdWarehouseGoldPriceRepository();
        IHumanResourcesRepository humanResources = new FirebirdHumanResourcesRepository();
        IPayrollService payroll = new FirebirdPayrollService(_sessionAccessor);
        IEmployeeMoneyService employeeMoney = new FirebirdEmployeeMoneyService();
        IPriceBoardRepository priceBoard = new FirebirdPriceBoardRepository();
        _services.AddSingleton<IPriceBoardTvSettingsStore>(new JsonPriceBoardTvSettingsStore());
        _services.AddSingleton<IPriceBoardTvHostClientFactory>(new PriceBoardTvHostClientFactory(
            Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được executable ERP."),typeof(App).Assembly.Location));
        _services.AddSingleton<IPriceBoardTvServerFactory>(new PriceBoardTvServerFactory());
        IGoldTypeCatalogService goldTypeCatalog = new FirebirdGoldTypeCatalogService();
        IMaterialReceiptService materialReceipts = new FirebirdMaterialReceiptService();
        IMaterialIssueService materialIssues = new FirebirdMaterialIssueService();
        IOutsourcedCraftService outsourcedCraft = new FirebirdOutsourcedCraftService();
        IFinishedGoodsIssueService finishedGoodsIssue = new FirebirdFinishedGoodsIssueService();
        IExternalWorkerRepository externalWorkers = new FirebirdExternalWorkerRepository();
        IStockTransferService stockTransfers = new FirebirdStockTransferService();
        IFinishedProductTransferService finishedProductTransfers = new FirebirdFinishedProductTransferService();
        IStockAdjustmentService stockAdjustments = new FirebirdStockAdjustmentService();
        IStocktakeService stocktakes = new FirebirdStocktakeService();
        ICashFundRepository cashFunds = new FirebirdCashFundRepository();
        IBankAccountRepository bankAccounts = new FirebirdBankAccountRepository();
        IPayableDebtRepository payableDebts = new FirebirdPayableDebtRepository();
        IDebtWorkbenchRepository debtWorkbench = new FirebirdDebtWorkbenchRepository();
        IFinancialVoucherPostingService financialVouchers = new FirebirdFinancialVoucherPostingService();
        IFinancialSettlementService financialSettlements = new FirebirdFinancialSettlementService();
        IReceivableDebtRepository receivableDebts = new FirebirdReceivableDebtRepository();
        IDocumentReversalService documentReversals = new FirebirdDocumentReversalService();
        IPartyCreditService partyCredits = new FirebirdPartyCreditService();
        IFinanceDashboardRepository financeDashboard = new FirebirdFinanceDashboardRepository();
        IFinanceVoucherService financeVouchers = new FirebirdFinanceVoucherService();
        IVoucherCategoryService voucherCategories = new FirebirdVoucherCategoryService();
        IProductionQueryRepository productionQueries = new FirebirdProductionQueryRepository();
        _services.AddSingleton<IFinishedReceiptDraftStore>(new JsonFinishedReceiptDraftStore());
        _services.AddSingleton<IFinishedReceiptDocuments>(new FirebirdFinishedReceiptDocuments());
        IProductionAttachmentStore productionAttachments = new LocalProductionAttachmentStore();
        IProductionApplicationService productionApplication =
            new FirebirdProductionApplicationService(productionAttachments);
        IProductionPrintDataService productionPrintData = new FirebirdProductionPrintDataService();
        IJewelryOnHandReportService jewelryOnHandReport = new FirebirdJewelryOnHandReportService();
        IJewelryOnHandSummaryReportService jewelryOnHandSummaryReport =
            new FirebirdJewelryOnHandSummaryReportService();
        IFundBalanceReportService fundBalanceReport = new FirebirdFundBalanceReportService();
        IPayrollReportService payrollReport = new FirebirdPayrollReportService();
        IFundCashReportService fundCashReport = new FirebirdFundCashReportService();
        IFundCashReceiptsReportService fundCashReceiptsReport = new FirebirdFundCashReceiptsReportService();
        IFundCashPaymentsReportService fundCashPaymentsReport = new FirebirdFundCashPaymentsReportService();
        IMasterCustomersReportService masterCustomersReport = new FirebirdMasterCustomersReportService();
        IMasterSuppliersReportService masterSuppliersReport = new FirebirdMasterSuppliersReportService();
        ISalesInvoicesReportService salesInvoicesReport = new FirebirdSalesInvoicesReportService();
        ISalesProfitLossReportService salesProfitLossReport = new FirebirdSalesProfitLossReportService();
        ISalesInvoicesByDayReportService salesInvoicesByDayReport = new FirebirdSalesInvoicesByDayReportService();
        ISalesDetailByDayReportService salesDetailByDayReport = new FirebirdSalesDetailByDayReportService();
        ISalesNewGoldDetailByTypeReportService salesNewGoldDetailByTypeReport =
            new FirebirdSalesNewGoldDetailByTypeReportService();
        ISalesOldGoldPurchaseDetailByTypeReportService salesOldGoldPurchaseDetailByTypeReport =
            new FirebirdSalesOldGoldPurchaseDetailByTypeReportService();
        ISalesNewGoldSummaryByTypeReportService salesNewGoldSummaryByTypeReport =
            new FirebirdSalesNewGoldSummaryByTypeReportService();
        ISalesOldGoldPurchaseSummaryByTypeReportService salesOldGoldPurchaseSummaryByTypeReport =
            new FirebirdSalesOldGoldPurchaseSummaryByTypeReportService();
        ISalesTopItemsReportService salesTopItemsReport = new FirebirdSalesTopItemsReportService();
        IWarehouseApprovedReceiptsReportService warehouseApprovedReceiptsReport =
            new FirebirdWarehouseApprovedReceiptsReportService();
        IWarehousePendingReceiptsReportService warehousePendingReceiptsReport =
            new FirebirdWarehousePendingReceiptsReportService();
        IWarehouseReceiptsByTypeReportService warehouseReceiptsByTypeReport =
            new FirebirdWarehouseReceiptsByTypeReportService();
        ISalesCheckoutApplicationService salesCheckout = new FirebirdSalesCheckoutApplicationService();
        ISalesQueryRepository salesQueries = new FirebirdSalesQueryRepository();
        ISalesAuthorizationService salesAuthorization = new FirebirdSalesAuthorizationService();
        ISalesPrintDataService salesPrintData = new FirebirdSalesPrintDataService(salesQueries);
        ICraftOrderApplicationService craftOrders = new FirebirdCraftOrderApplicationService(productionAttachments);
        IWholesaleSalesApplicationService wholesaleSales = new FirebirdWholesaleSalesApplicationService();
        IUserManagementRepository userManagement = new FirebirdUserManagementRepository();

        _services.AddSingleton(preferences);
        _services.AddSingleton(appearance);
        _services.AddSingleton<IDatabaseManagementService>(
            new FirebirdDatabaseManagementService(preferences));
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
        _services.AddSingleton(payroll);
        _services.AddSingleton(employeeMoney);
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
        _services.AddSingleton(stocktakes);
        _services.AddSingleton(cashFunds);
        _services.AddSingleton(bankAccounts);
        _services.AddSingleton(payableDebts);
        _services.AddSingleton(debtWorkbench);
        _services.AddSingleton(financialVouchers);
        _services.AddSingleton(financialSettlements);
        _services.AddSingleton(receivableDebts);
        _services.AddSingleton(documentReversals);
        _services.AddSingleton(partyCredits);
        _services.AddSingleton(financeDashboard);
        _services.AddSingleton(financeVouchers);
        _services.AddSingleton(voucherCategories);
        _services.AddSingleton(productionQueries);
        _services.AddSingleton(productionApplication);
        _services.AddSingleton(productionAttachments);
        _services.AddSingleton(productionPrintData);
        _services.AddSingleton(jewelryOnHandReport);
        _services.AddSingleton(jewelryOnHandSummaryReport);
        _services.AddSingleton(fundBalanceReport);
        _services.AddSingleton(payrollReport);
        _services.AddSingleton(fundCashReport);
        _services.AddSingleton(fundCashReceiptsReport);
        _services.AddSingleton(fundCashPaymentsReport);
        _services.AddSingleton(masterCustomersReport);
        _services.AddSingleton(masterSuppliersReport);
        _services.AddSingleton(salesInvoicesReport);
        _services.AddSingleton(salesProfitLossReport);
        _services.AddSingleton(salesInvoicesByDayReport);
        _services.AddSingleton(salesDetailByDayReport);
        _services.AddSingleton(salesNewGoldDetailByTypeReport);
        _services.AddSingleton(salesOldGoldPurchaseDetailByTypeReport);
        _services.AddSingleton(salesNewGoldSummaryByTypeReport);
        _services.AddSingleton(salesOldGoldPurchaseSummaryByTypeReport);
        _services.AddSingleton(salesTopItemsReport);
        _services.AddSingleton(warehouseApprovedReceiptsReport);
        _services.AddSingleton(warehousePendingReceiptsReport);
        _services.AddSingleton(warehouseReceiptsByTypeReport);
        _services.AddSingleton(salesCheckout);
        _services.AddSingleton(salesQueries);
        _services.AddSingleton(craftOrders);
        _services.AddSingleton(wholesaleSales);
        _services.AddSingleton(salesAuthorization);
        _services.AddSingleton(salesPrintData);
        _services.AddSingleton(userManagement);
        IAuthorizationService authorization = new FirebirdAuthorizationService(_sessionAccessor);
        _services.AddSingleton(authorization);
        void Protect<T>() where T : class => _services.AddSingleton(
            AuthorizedServiceProxy<T>.Create(_services.GetRequiredService<T>(), authorization, _sessionAccessor));
        Protect<ICustomerRepository>(); Protect<ISupplierRepository>(); Protect<IHumanResourcesRepository>(); Protect<IPayrollService>();
        Protect<IEmployeeMoneyService>();
        Protect<IWarehouseRepository>(); Protect<IWarehouseGoldPriceRepository>(); Protect<IGoldTypeCatalogService>();
        Protect<IStocktakeService>();
        Protect<IPriceBoardRepository>(); Protect<IExternalWorkerRepository>();
        Protect<IMaterialReceiptService>(); Protect<IMaterialIssueService>(); Protect<IOutsourcedCraftService>();
        Protect<IFinishedGoodsIssueService>(); Protect<IStockTransferService>(); Protect<IFinishedProductTransferService>();
        Protect<IStockAdjustmentService>(); Protect<IPayableDebtRepository>(); Protect<IDebtWorkbenchRepository>();
        Protect<IFinancialVoucherPostingService>(); Protect<IFinancialSettlementService>(); Protect<IReceivableDebtRepository>();
        Protect<IDocumentReversalService>(); Protect<IPartyCreditService>(); Protect<IFinanceDashboardRepository>();
        Protect<IFinanceVoucherService>(); Protect<ICashFundRepository>(); Protect<IBankAccountRepository>();
        Protect<IVoucherCategoryService>();
        Protect<IFinishedReceiptDocuments>();
        Protect<IProductionQueryRepository>(); Protect<IProductionApplicationService>(); Protect<IProductionPrintDataService>();
        Protect<ISalesCheckoutApplicationService>(); Protect<ISalesQueryRepository>(); Protect<ISalesPrintDataService>(); Protect<ISalesAuthorizationService>();
        Protect<ICraftOrderApplicationService>(); Protect<IWholesaleSalesApplicationService>(); Protect<IUserManagementRepository>();
        Protect<ICompanyProfileService>(); Protect<IPrintTemplateStore>();
        _backgroundDatabase = _services.GetRequiredService<IDatabaseManagementService>();
        Protect<IDatabaseManagementService>();
        Protect<IJewelryOnHandReportService>(); Protect<IJewelryOnHandSummaryReportService>(); Protect<IFundBalanceReportService>();
        Protect<IPayrollReportService>();
        Protect<IFundCashReportService>(); Protect<IFundCashReceiptsReportService>(); Protect<IFundCashPaymentsReportService>();
        Protect<IMasterCustomersReportService>(); Protect<IMasterSuppliersReportService>(); Protect<ISalesInvoicesReportService>();
        Protect<ISalesProfitLossReportService>(); Protect<ISalesInvoicesByDayReportService>(); Protect<ISalesDetailByDayReportService>();
        Protect<ISalesNewGoldDetailByTypeReportService>(); Protect<ISalesOldGoldPurchaseDetailByTypeReportService>();
        Protect<ISalesNewGoldSummaryByTypeReportService>(); Protect<ISalesOldGoldPurchaseSummaryByTypeReportService>();
        Protect<ISalesTopItemsReportService>(); Protect<IWarehouseApprovedReceiptsReportService>();
        Protect<IWarehousePendingReceiptsReportService>(); Protect<IWarehouseReceiptsByTypeReportService>();
        PermissionAccess.Initialize(authorization);
        ExcelExportService.ConfigureAuthorization(authorization);
        Kazuko.Modules.Reports.Shared.FastReportPdfExporter.ConfigureAuthorization(authorization);
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
            new FinanceModule(),
            new HumanResourcesModule(),
            new ReportsModule(),
            new UserManagementModule()
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
        (_services.GetService(typeof(IPriceBoardTvController)) as IPriceBoardTvController)?.Detach();
        _sessionAccessor.Clear();
        var viewModel = new LoginViewModel(_services.GetRequiredService<IAuthenticationService>());
        var loginView = new LoginView { DataContext = viewModel };
        var window = new LoginWindow { Content = loginView };

        var signingIn = false;
        viewModel.SignedIn += async (_, session) =>
        {
            if (signingIn) return;
            signingIn = true;
            window.IsEnabled = false;
            _sessionAccessor.Set(session);
            var authorization = _services.GetRequiredService<IAuthorizationService>();
            DatabaseTableColumnLayoutSession layouts;
            try
            {
                await authorization.RefreshAsync();
                var repository = new FirebirdTableColumnLayoutRepository(session);
                var journalPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "KazukoERP", "table-layouts", repository.CacheScope, "cache.json");
                layouts = new DatabaseTableColumnLayoutSession(repository, journalPath, new JsonTableColumnLayoutStore());
                await layouts.InitializeAsync();
                if (!window.IsLoaded) { await layouts.DisposeAsync(); return; }
                _tableLayouts = layouts;
                DataGridColumnLayoutStore.Configure(layouts);
            }
            catch (Exception)
            {
                _sessionAccessor.Clear(); authorization.Clear();
                signingIn = false;
                window.IsEnabled = true;
                MessageBox.Show("Không tải được phiên làm việc. Vui lòng đăng nhập lại.", "Đăng nhập", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            MainWindow? shell = null;
            var loggingOut = false;
            var closing = false;
            var allowClose = false;
            var shellViewModel = new ShellViewModel(
                session.DisplayName,
                _navigation,
                _services.GetRequiredService<INavigationService>(),
                authorization,
                () =>
                {
                    loggingOut = true;
                    shell?.Close();
                }, layouts);
            shell = new MainWindow { DataContext = shellViewModel };
            shell.Closing += async (_, args) =>
            {
                if (allowClose) return;
                args.Cancel = true;
                if (closing) return;
                closing = true;
                shell.IsEnabled = false;
                try
                {
                    DataGridColumnLayout.FlushPendingLayouts(shell);
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                    await layouts.FlushAsync(timeout.Token);
                }
                catch (OperationCanceledException) { /* The durable outbox is retried on next login. */ }
                catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Cannot flush table layouts: {0}", ex.Message); }
                finally
                {
                    if (loggingOut)
                    {
                        authorization.Clear();
                        ShowLogin();
                    }
                    allowClose = true;
                    shell.Close();
                    await layouts.DisposeAsync();
                    if (ReferenceEquals(_tableLayouts, layouts)) _tableLayouts = null;
                }
            };
            MainWindow = shell;
            shell.Show();
            window.Close();
        };

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if(_hostMode)
        {
            _priceBoardHost?.Dispose();
            if(_ownsHostMutex)_priceBoardHostMutex?.ReleaseMutex();
            _priceBoardHostMutex?.Dispose();
            base.OnExit(e);
            return;
        }
        // Application.Shutdown (for example after switching DB) can bypass an
        // asynchronous Closing handler. The repository is pinned to the old DB.
        if (_tableLayouts is { } layouts)
        {
            try
            {
                if (MainWindow is not null) DataGridColumnLayout.FlushPendingLayouts(MainWindow);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                layouts.FlushAsync(timeout.Token).GetAwaiter().GetResult();
                layouts.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Table layouts retained locally: {0}", ex.Message); }
        }
        (_services.GetService(typeof(IPriceBoardTvController)) as IPriceBoardTvController)?.Detach();
        base.OnExit(e);
    }
}
