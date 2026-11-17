using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.UserManagement;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class FinishedProductCatalogTests
{
    [Theory]
    [InlineData("SaveFinishedProductCatalogAsync", 0, PermissionAction.Add)]
    [InlineData("SaveFinishedProductCatalogAsync", 10, PermissionAction.Edit)]
    [InlineData("DeactivateFinishedProductCatalogAsync", 10, PermissionAction.Delete)]
    public void Backend_requires_the_matching_mutation_permission(string method, long id, PermissionAction action)
    {
        var viewOnly = new PermissionSnapshot(false, [new(1, "INV_FINISHED_RECEIPT", true, false, false, false, false)]);
        var allowed = new PermissionSnapshot(false, [new(1, "INV_FINISHED_RECEIPT", true,
            action == PermissionAction.Add, action == PermissionAction.Edit, action == PermissionAction.Delete, false)]);
        object?[] args = [false, id, "N", "Nhẫn"];
        Assert.False(Kazuko.Infrastructure.UserManagement.BackendAccess.Allows("IWarehouseRepository", method, args, viewOnly));
        Assert.True(Kazuko.Infrastructure.UserManagement.BackendAccess.Allows("IWarehouseRepository", method, args, allowed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Search_deactivate_and_choose_preserve_changes_when_catalog_is_closed(bool symbols)
    {
        var items = new List<FinishedProductCatalogItem>
        {
            new(1, "N", "Nhẫn", true), new(2, "L", "Lắc", true)
        };
        var repository = DispatchProxy.Create<IWarehouseRepository, RepositoryProxy>();
        ((RepositoryProxy)(object)repository).Call = (method, args) =>
        {
            Assert.Equal(symbols, args[0]);
            if (method == nameof(IWarehouseRepository.GetFinishedProductCatalogAsync))
                return Task.FromResult<IReadOnlyList<FinishedProductCatalogItem>>(items.ToArray());
            Assert.Equal(nameof(IWarehouseRepository.DeactivateFinishedProductCatalogAsync), method);
            var id = (long)args[1]!;
            items = items.Select(x => x.Id == id ? x with { IsActive = false } : x).ToList();
            return Task.CompletedTask;
        };
        var vm = new FinishedProductCatalogViewModel(repository,
            new AuthenticatedSession(1, "tester", "Tester", "ADMIN", 1, "CN", "Chi nhánh"),
            new Authorization(), symbols, _ => null, _ => true);
        await vm.LoadAsync(2);
        Assert.Equal(2, vm.Selected?.Id);
        vm.Query = "nhẫn";
        Assert.Equal(1, Assert.Single(vm.Rows).Id);
        vm.DeleteCommand.Execute(null);
        Assert.True(vm.HasChanges);
        Assert.False(vm.ChooseCommand.CanExecute(null));
        Assert.Null(vm.ChosenItem);
        vm.Query = "L";
        vm.ChooseCommand.Execute(null);
        Assert.Equal(2, vm.ChosenItem?.Id);
    }

    [Fact]
    public async Task Read_only_permissions_disable_catalog_mutations()
    {
        var repository = DispatchProxy.Create<IWarehouseRepository, RepositoryProxy>();
        ((RepositoryProxy)(object)repository).Call = (_, _) =>
            Task.FromResult<IReadOnlyList<FinishedProductCatalogItem>>([new(1, "N", "Nhẫn", true)]);
        var vm = new FinishedProductCatalogViewModel(repository,
            new AuthenticatedSession(1, "tester", "Tester", "ADMIN", 1, "CN", "Chi nhánh"),
            new Authorization(readOnly: true), false, _ => throw new Exception("Editor must not open"), _ => true);
        await vm.LoadAsync(1);
        Assert.False(vm.AddCommand.CanExecute(null));
        Assert.False(vm.EditCommand.CanExecute(null));
        Assert.False(vm.DeleteCommand.CanExecute(null));
        Assert.True(vm.ChooseCommand.CanExecute(null));
    }

    public class RepositoryProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }

    private sealed class Authorization(bool readOnly = false) : IAuthorizationService
    {
        public PermissionSnapshot Current { get; } = new(!readOnly,
            [new GroupPermissionRow(1, "INV_FINISHED_RECEIPT", true, false, false, false, false)]);
        public event EventHandler? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Clear() { }
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default)
        {
            if (requirements.Any(x => !Current.Allows(x.Function, x.Action))) throw new UnauthorizedAccessException();
            return Task.CompletedTask;
        }
    }
}