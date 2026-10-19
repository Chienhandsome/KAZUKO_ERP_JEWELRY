using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class CraftOrderWorkspaceTests
{
    internal static (CraftOrderWorkspaceViewModel Vm, CraftSearchProxy Service) Create()
    {
        var service = DispatchProxy.Create<ICraftOrderApplicationService, CraftSearchProxy>();
        return (new CraftOrderWorkspaceViewModel(service,
            DispatchProxy.Create<IWarehouseRepository, UnusedDependencyProxy>(),
            () => new AuthenticatedSession(1, "admin", "Admin", "ADMIN", 1, "CN", "Chi nhánh"),
            _ => null, _ => null), (CraftSearchProxy)(object)service);
    }

    [Fact]
    public async Task Unopened_tab_does_not_query_when_filters_change()
    {
        var (vm, service) = Create();
        vm.SearchText = "DH";
        vm.FromDate = DateTime.Today.AddDays(-10);
        await Task.Delay(400);
        Assert.Empty(service.Filters);
        await vm.EnsureLoadedAsync();
        Assert.Equal("DH", Assert.Single(service.Filters).SearchText);
    }

    [Fact]
    public async Task Typing_is_debounced_and_clearing_search_also_reloads()
    {
        var (vm, service) = Create();
        await vm.EnsureLoadedAsync();
        vm.SearchText = "D";
        vm.SearchText = "DH";
        vm.SearchText = "DH-2026";
        await WaitUntilAsync(() => service.Filters.Count == 2 && !vm.IsLoading);
        Assert.Equal("DH-2026", service.Filters.Last().SearchText);
        vm.SearchText = "";
        await WaitUntilAsync(() => service.Filters.Count == 3 && !vm.IsLoading);
        Assert.Equal("", service.Filters.Last().SearchText);
    }

    [Fact]
    public async Task Date_and_status_filters_reload_together()
    {
        var (vm, service) = Create();
        await vm.EnsureLoadedAsync();
        vm.FromDate = DateTime.Today.AddDays(-7);
        vm.ToDate = DateTime.Today.AddDays(-1);
        vm.SelectedStatusFilter = vm.StatusFilterOptions.First(x => x.Code == CraftOrderStatuses.Ready);
        await WaitUntilAsync(() => service.Filters.Count == 2 && !vm.IsLoading);
        var filter = service.Filters.Last();
        Assert.Equal(vm.FromDate, filter.FromDate);
        Assert.Equal(vm.ToDate, filter.ToDate);
        Assert.Equal(CraftOrderStatuses.Ready, filter.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Obsolete_request_cannot_publish_results_or_errors(bool failOldRequest)
    {
        var (vm, service) = Create();
        var oldResult = new TaskCompletionSource<IReadOnlyList<CraftOrderListItem>>();
        service.Search = filter => filter.SearchText == "old" ? oldResult.Task
            : Task.FromResult<IReadOnlyList<CraftOrderListItem>>([]);
        vm.SearchText = "old";
        var firstLoad = vm.EnsureLoadedAsync();
        vm.SearchText = "new";
        await WaitUntilAsync(() => service.Filters.Count == 2 && !vm.IsLoading);
        if (failOldRequest) oldResult.SetException(new Exception("obsolete error"));
        else oldResult.SetResult([SampleOrder]);
        await firstLoad;
        Assert.Empty(vm.Items);
        Assert.Equal("Không có đơn đặt phù hợp.", vm.StatusMessage);
        Assert.False(vm.IsLoading);
    }

    internal static CraftOrderListItem SampleOrder => new(1, "DH-2026-000001", DateTime.Today,
        CraftOrderStatuses.Ready, 1, "Khách đặt trang sức", "0901000002", 1405000000, 421500000, false);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(20, timeout.Token);
    }
}

public class CraftSearchProxy : DispatchProxy
{
    public CraftOrderHeader? Header { get; set; } = new(1, "DH-2026-000001", CraftOrderStatuses.Ready,
        1, "KH000002", "Khách đặt trang sức", "0901000002", 1405000000, 421500000, false);
    public List<CraftOrderSearchFilter> Filters { get; } = [];
    public Func<CraftOrderSearchFilter, Task<IReadOnlyList<CraftOrderListItem>>> Search { get; set; }
        = _ => Task.FromResult<IReadOnlyList<CraftOrderListItem>>([]);
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        switch (targetMethod?.Name)
        {
            case nameof(ICraftOrderApplicationService.SearchOrdersAsync):
                var filter = (CraftOrderSearchFilter)args![0]!;
                Filters.Add(filter);
                return Search(filter);
            case nameof(ICraftOrderApplicationService.GetHeaderAsync):
                return Task.FromResult(Header);
            case nameof(ICraftOrderApplicationService.GetLinesAsync):
                return Task.FromResult<IReadOnlyList<CraftOrderLineItem>>([
                    new(1, "Vòng tay vàng chế tác theo mẫu", "9999", 10, 5, 28000000, 500000, 0, 1405000000, null, null, null, [])]);
            default: throw new InvalidOperationException($"Unexpected call: {targetMethod?.Name}");
        }
    }
}
