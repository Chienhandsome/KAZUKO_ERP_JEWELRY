using Kazuko.Core.Authentication;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Modules;

namespace Kazuko.Modules.HumanResources;

public sealed class HumanResourcesModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.HumanResources;component/Assets/icons/";

    public string Id => "Kazuko.HumanResources";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        var repository = GetRequiredService<IHumanResourcesRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        services.AddSingleton<IEmployeeEditorService>(new EmployeeEditorService(repository, session));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<IHumanResourcesRepository>(services);
        var editor = GetRequiredService<IEmployeeEditorService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "HR",
            "NHÂN SỰ",
            string.Empty,
            80,
            HumanResourcesPermissions.EmployeeRead,
            () => new HumanResourcesView
            {
                DataContext = new HumanResourcesViewModel(repository, editor, session)
            },
            AssetRoot + "NHAN_SU.png",
            "Quản lý nhân viên,\nchấm công, lương",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
