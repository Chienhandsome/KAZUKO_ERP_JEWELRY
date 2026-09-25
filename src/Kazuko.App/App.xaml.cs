using System.IO;
using System.Windows;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.Updates;
using Kazuko.Infrastructure.Authentication;
using Kazuko.Infrastructure.Settings;
using Kazuko.Infrastructure.Updates;
using Kazuko.Modules.Authentication;
using Kazuko.Modules.Dashboard;
using Kazuko.Shared.UI.Services;

namespace Kazuko.App;

public partial class App : Application
{
    private readonly ServiceRegistry _services = new();
    private readonly ModuleNavigationRegistry _navigation = new();

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
        IModulePackageInstaller packageInstaller = new ModulePackageInstaller(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KazukoERP", "Updates"));

        _services.AddSingleton(preferences);
        _services.AddSingleton(appearance);
        _services.AddSingleton(authentication);
        _services.AddSingleton(packageInstaller);
        _services.AddSingleton<IModuleNavigationRegistry>(_navigation);
    }

    private void RegisterModules()
    {
        IKazukoModule[] modules = [new DashboardModule()];
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
        var viewModel = new LoginViewModel(_services.GetRequiredService<IAuthenticationService>());
        var loginView = new LoginView { DataContext = viewModel };
        var window = new LoginWindow { Content = loginView };

        viewModel.SignedIn += (_, displayName) =>
        {
            var shell = new MainWindow
            {
                DataContext = new ShellViewModel(
                    displayName,
                    _navigation,
                    _services.GetRequiredService<IAppearanceService>())
            };
            MainWindow = shell;
            shell.Show();
            window.Close();
        };

        window.Show();
    }
}
