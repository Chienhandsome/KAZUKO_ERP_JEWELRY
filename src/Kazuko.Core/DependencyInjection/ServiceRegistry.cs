namespace Kazuko.Core.DependencyInjection;

public interface IServiceRegistry : IServiceProvider
{
    void AddSingleton<TService>(TService instance) where TService : class;
    TService GetRequiredService<TService>() where TService : class;
}

public sealed class ServiceRegistry : IServiceRegistry
{
    private readonly Dictionary<Type, object> _services = [];

    public void AddSingleton<TService>(TService instance) where TService : class =>
        _services[typeof(TService)] = instance;

    public object? GetService(Type serviceType) =>
        _services.TryGetValue(serviceType, out var service) ? service : null;

    public TService GetRequiredService<TService>() where TService : class =>
        GetService(typeof(TService)) as TService
        ?? throw new InvalidOperationException($"Service {typeof(TService).FullName} is not registered.");
}
