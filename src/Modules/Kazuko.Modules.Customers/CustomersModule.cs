using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Modules;

namespace Kazuko.Modules.Customers;

public sealed class CustomersModule : IKazukoModule
{
    private const string AssetRoot = "/Kazuko.Modules.Customers;component/Assets/icons/";

    public string Id => "Kazuko.Customers";
    public Version Version => new(1, 0, 0);

    public void RegisterServices(IServiceRegistry services)
    {
        var repository = GetRequiredService<ICustomerRepository>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);
        services.AddSingleton<ICustomerEditorService>(new CustomerEditorService(repository, session));
    }

    public void RegisterNavigation(IModuleNavigationRegistry navigation, IServiceProvider services)
    {
        var repository = GetRequiredService<ICustomerRepository>(services);
        var editor = GetRequiredService<ICustomerEditorService>(services);
        var session = GetRequiredService<ICurrentSessionAccessor>(services);

        navigation.Register(new NavigationItem(
            "CUSTOMER",
            "KHÁCH HÀNG",
            string.Empty,
            60,
            null,
            () => new CustomerView
            {
                DataContext = new CustomerViewModel(repository, editor, session)
            },
            AssetRoot + "KH.png",
            "Quản lý thông tin\nkhách hàng",
            ShowInSidebar: true,
            ShowOnHome: true,
            IsPlaceholder: false));
    }

    private static T GetRequiredService<T>(IServiceProvider services) where T : class =>
        services.GetService(typeof(T)) as T
        ?? throw new InvalidOperationException($"Service {typeof(T).FullName} is not registered.");
}
