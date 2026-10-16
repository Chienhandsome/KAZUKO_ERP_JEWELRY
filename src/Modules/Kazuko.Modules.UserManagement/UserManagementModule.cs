using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.UserManagement;

public sealed class UserManagementModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.UserManagement;component/Assets/icons/";

    public string Id => "Kazuko.UserManagement";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        // Repository được App đăng ký trước (App.xaml.cs) — module chỉ đọc ra để dựng view.
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<IUserManagementRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "USER_MANAGEMENT",
            "NGƯỜI DÙNG",
            string.Empty,
            90,
            null,
            () => new UserManagementView
            {
                DataContext = new UserManagementViewModel(repository, session)
            },
            AssetRoot + "CAIDAT.png",
            "Người dùng và phân quyền\ntheo nhóm",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
