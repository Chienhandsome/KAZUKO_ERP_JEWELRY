using System.Windows;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Modules;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Updates;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Authentication;
using Kazuko.Infrastructure.Customers;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.HumanResources;
using Kazuko.Infrastructure.PriceBoard;
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
        IHumanResourcesRepository humanResources = new FirebirdHumanResourcesRepository();
        IPriceBoardRepository priceBoard = new FirebirdPriceBoardRepository();
        IMaterialReceiptService materialReceipts = new FirebirdMaterialReceiptService();
        IOutsourcedCraftService outsourcedCraft = new FirebirdOutsourcedCraftService();
        IFinishedGoodsIssueService finishedGoodsIssue = new FirebirdFinishedGoodsIssueService();
        IExternalWorkerRepository externalWorkers = new FirebirdExternalWorkerRepository();
        ICashFundRepository cashFunds = new FirebirdCashFundRepository();
        IBankAccountRepository bankAccounts = new FirebirdBankAccountRepository();
        IPayableDebtRepository payableDebts = new FirebirdPayableDebtRepository();

        _services.AddSingleton(preferences);
        _services.AddSingleton(appearance);
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
        _services.AddSingleton(humanResources);
        _services.AddSingleton(priceBoard);
        _services.AddSingleton(materialReceipts);
        _services.AddSingleton(outsourcedCraft);
        _services.AddSingleton(finishedGoodsIssue);
        _services.AddSingleton(externalWorkers);
        _services.AddSingleton(cashFunds);
        _services.AddSingleton(bankAccounts);
        _services.AddSingleton(payableDebts);
    }

    private void RegisterModules()
    {
        IKazukoModule[] modules =
        [
            new DashboardModule(),
            new CustomersModule(),
            new SuppliersModule(),
            new WarehouseModule(),
            new DebtModule(),
            new HumanResourcesModule(),
            new PriceBoardModule()
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
