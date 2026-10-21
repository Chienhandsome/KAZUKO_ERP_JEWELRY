using System.Reflection;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.Printing;
using Kazuko.Core.Settings;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Dashboard;
using Xunit;

namespace Kazuko.Modules.Dashboard.Tests;

public sealed class LabelDesignerCommandTests
{
    [Fact]
    public void Settings_resolves_designer_after_warehouse_registration_and_notifies_command_when_category_changes() => Sta(() =>
    {
        var (services, navigation) = RegisterDashboardBeforeWarehouse();
        var designer = new Designer();
        services.AddSingleton<ILabelDesignerService>(designer);
        var view = (SettingsView)navigation.Items.Single(x => x.Key == "SETTINGS").CreateView();
        var vm = Assert.IsType<SettingsViewModel>(view.DataContext);
        var notifications = 0;
        vm.OpenLabelDesignerCommand.CanExecuteChanged += (_, _) => notifications++;
        Assert.False(vm.OpenLabelDesignerCommand.CanExecute(null));
        vm.SelectedCategory = vm.Categories.Single(x => x.Key == "receipt_template");
        Assert.True(notifications > 0);
        Assert.True(vm.OpenLabelDesignerCommand.CanExecute(null));
        vm.OpenLabelDesignerCommand.Execute(null);
        Assert.Equal(1, designer.Calls);
        Assert.Empty(vm.ErrorMessage);
        vm.SelectedCategory = vm.Categories[0];
        Assert.False(vm.OpenLabelDesignerCommand.CanExecute(null));
    });

    [Fact]
    public void Designer_failure_is_reported_and_command_becomes_available_again() => Sta(() =>
    {
        var (services, navigation) = RegisterDashboardBeforeWarehouse();
        services.AddSingleton<ILabelDesignerService>(new Designer { Fail = true });
        var vm = (SettingsViewModel)((SettingsView)navigation.Items.Single(x => x.Key == "SETTINGS").CreateView()).DataContext;
        vm.SelectedCategory = vm.Categories.Single(x => x.Key == "receipt_template");
        vm.OpenLabelDesignerCommand.Execute(null);
        Assert.Equal("Không tải được mẫu tem.", vm.ErrorMessage);
        Assert.True(vm.OpenLabelDesignerCommand.CanExecute(null));
    });

    private static (ServiceRegistry, ModuleNavigationRegistry) RegisterDashboardBeforeWarehouse()
    {
        var services = new ServiceRegistry();
        services.AddSingleton<INavigationService>(new NavigationService());
        services.AddSingleton<ICompanyProfileService>(Proxy<ICompanyProfileService>());
        services.AddSingleton<IAppearanceService>(Proxy<IAppearanceService>());
        services.AddSingleton<IPrintTemplateStore>(Proxy<IPrintTemplateStore>());
        services.AddSingleton<IDatabaseManagementService>(Proxy<IDatabaseManagementService>());
        services.AddSingleton<IPreferencesService>(Proxy<IPreferencesService>());
        services.AddSingleton<IAuthorizationService>(Proxy<IAuthorizationService>());
        var session = new CurrentSessionAccessor();
        session.Set(new(1, "admin", "Admin", "ADMIN", 1, "CN", "Chi nhánh"));
        services.AddSingleton<ICurrentSessionAccessor>(session);
        var navigation = new ModuleNavigationRegistry();
        var module = new DashboardModule();
        module.RegisterServices(services);
        module.RegisterNavigation(navigation, services);
        return (services, navigation);
    }

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, Stub>();

    public class Stub : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "get_Current" when method.DeclaringType == typeof(IAuthorizationService) => new PermissionSnapshot(true, []),
            "get_Current" or "Load" => new AppPreferences(),
            "GetAsync" => Task.FromResult(new CompanyProfileDto(1, "KAZUKO", null, null, null, null)),
            "GetInfoAsync" => Task.FromResult(new DatabaseInfo("test.fdb", 0, "0", 1, "1", 1, 1)),
            "GetByCodeAsync" => Task.FromResult(new PrintTemplateDto(1, "BILL", "Bill", [], "hash", DateTime.Today, null)),
            _ => null
        };
    }

    private sealed class Designer : ILabelDesignerService
    {
        public int Calls { get; private set; }
        public bool Fail { get; init; }
        public Task<bool> OpenDesignerAsync(AuthenticatedSession session)
        {
            Calls++;
            return Fail ? Task.FromException<bool>(new InvalidOperationException("Không tải được mẫu tem.")) : Task.FromResult(true);
        }
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
