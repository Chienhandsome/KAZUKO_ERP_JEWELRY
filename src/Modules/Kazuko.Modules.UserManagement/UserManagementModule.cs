using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.UserManagement;

public sealed class UserManagementModule : IKazukoModule
{
    public string Id => "Kazuko.UserManagement";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        services.AddSingleton<IUserManagementViewFactory>(new UserManagementViewFactory(
            services.GetRequiredService<IUserManagementRepository>(),
            services.GetRequiredService<ICurrentSessionAccessor>()));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
    }

    private sealed class UserManagementViewFactory(
        IUserManagementRepository repository,
        ICurrentSessionAccessor session) : IUserManagementViewFactory
    {
        public object CreateView() => new UserManagementView
        {
            DataContext = new UserManagementViewModel(repository, session)
        };
    }
}
