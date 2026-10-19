using System.IO;
using Kazuko.Core.Modules;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Dashboard;
using Xunit;

namespace Kazuko.Modules.Dashboard.Tests;

public sealed class DashboardShortcutTests
{
    private static readonly string[] Routes = ["PRODUCTION", "WAREHOUSE", "SUPPLIER", "SALES", "DEBT", "CUSTOMER",
        "FINANCE", "HR", "REPORT", "PRICE_BOARD", "USER_MANAGEMENT", "SETTINGS"];

    [Fact]
    public void Defaults_are_capped_and_hidden_home_modules_are_available_to_choose() => Sta(() =>
    {
        var vm = Create(new Store());
        Assert.Equal(10, vm.HomeTiles.Count);
        Assert.Equal(Routes.Length, vm.ShortcutOptions.Count);
        Assert.False(vm.ShortcutOptions.Single(x => x.Key == "SUPPLIER").IsSelected);
        Assert.DoesNotContain(vm.ShortcutOptions, x => x.Key == "DASHBOARD");
        var supplier = vm.ShortcutOptions.Single(x => x.Key == "SUPPLIER");
        Assert.False(supplier.CanToggle);
        supplier.IsSelected = true;
        Assert.Equal(10, vm.HomeTiles.Count);
        Assert.False(supplier.IsSelected);
        vm.ShortcutOptions.First(x => x.IsSelected).IsSelected = false;
        Assert.True(supplier.CanToggle);
        supplier.IsSelected = true;
        Assert.Equal(10, vm.HomeTiles.Count);
        Assert.Contains(vm.HomeTiles, x => x.Key == "SUPPLIER");
    });

    [Fact]
    public void All_hidden_is_saved_and_stays_empty_after_reopening() => Sta(() =>
    {
        var store = new Store();
        var vm = Create(store);
        foreach (var option in vm.ShortcutOptions.ToArray()) option.IsSelected = false;
        Assert.Empty(vm.HomeTiles);
        Assert.True(vm.HasNoShortcuts);
        Assert.Empty(Create(store).HomeTiles);
    });

    [Fact]
    public void Choices_survive_permission_loss_and_restoration_without_bypassing_access() => Sta(() =>
    {
        var store = new Store { Keys = ["SALES", "CUSTOMER"] };
        var auth = new Authorization();
        var vm = Create(store, auth);
        auth.Set(PermissionSnapshot.Denied);
        Assert.Empty(vm.HomeTiles);
        Assert.Empty(vm.ShortcutOptions);
        Assert.Equal(new[] { "SALES", "CUSTOMER" }, store.Keys);
        auth.Set(new PermissionSnapshot(true, []));
        Assert.Equal(2, vm.HomeTiles.Count);
    });

    [Fact]
    public void Oversized_stale_saved_data_never_displays_more_than_ten() => Sta(() =>
    {
        var vm = Create(new Store { Keys = [..Routes, "UNKNOWN", "DASHBOARD", "sales"] });
        Assert.Equal(10, vm.HomeTiles.Count);
        Assert.Equal(10, vm.ShortcutOptions.Count(x => x.IsSelected));
    });

    [Fact]
    public void Save_failure_keeps_the_previous_selection_and_reports_error() => Sta(() =>
    {
        var vm = Create(new Store { Fail = true });
        var option = vm.ShortcutOptions.First(x => x.IsSelected);
        option.IsSelected = false;
        Assert.True(option.IsSelected);
        Assert.Equal(10, vm.HomeTiles.Count);
        Assert.NotEmpty(vm.ShortcutError);
    });

    [Fact]
    public void Selected_shortcut_navigates_to_its_registered_route() => Sta(() =>
    {
        var nav = new NavigationService();
        string? requested = null;
        nav.NavigationRequested += (_, e) => requested = e.Key;
        var vm = new DashboardViewModel(Registry(), nav, new Authorization(), new Store { Keys = ["SALES"] }, 1);
        vm.HomeTiles.Single().NavigateCommand.Execute(null);
        Assert.Equal("SALES", requested);
    });

    [Fact]
    public void File_store_separates_users_preserves_empty_and_recovers_from_invalid_json()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kazuko-home-tests-" + Guid.NewGuid());
        try
        {
            var store = new JsonDashboardShortcutStore(directory);
            Assert.Null(store.Load(1));
            store.Save(1, ["SALES", "SUPPLIER"]);
            store.Save(2, []);
            var reopened = new JsonDashboardShortcutStore(directory);
            Assert.Equal(new[] { "SALES", "SUPPLIER" }, reopened.Load(1));
            Assert.Empty(reopened.Load(2)!);
            File.WriteAllText(Path.Combine(directory, "user-1.json"), "invalid json");
            Assert.Null(reopened.Load(1));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    private static DashboardViewModel Create(Store store, Authorization? authorization = null) =>
        new(Registry(), new NavigationService(), authorization ?? new Authorization(), store, 1);

    private static ModuleNavigationRegistry Registry()
    {
        var registry = new ModuleNavigationRegistry();
        registry.Register(new("DASHBOARD", "Trang chủ", "", 0, null, () => new object()));
        for (var i = 0; i < Routes.Length; i++)
            registry.Register(new(Routes[i], Routes[i], "", i + 1, null, () => new object(),
                ShowOnHome: Routes[i] != "SUPPLIER"));
        return registry;
    }

    private static void Sta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class Store : IDashboardShortcutStore
    {
        public IReadOnlyList<string>? Keys { get; set; }
        public bool Fail { get; init; }
        public IReadOnlyList<string>? Load(long userId) => Keys;
        public void Save(long userId, IReadOnlyList<string> keys)
        {
            if (Fail) throw new IOException("Test write failure");
            Keys = keys.ToArray();
        }
    }

    private sealed class Authorization : IAuthorizationService
    {
        public PermissionSnapshot Current { get; private set; } = new(true, []);
        public event EventHandler? Changed;
        public void Set(PermissionSnapshot value) { Current = value; Changed?.Invoke(this, EventArgs.Empty); }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Clear() => Set(PermissionSnapshot.Denied);
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
