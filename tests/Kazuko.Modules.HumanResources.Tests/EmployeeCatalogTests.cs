using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.UserManagement;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class EmployeeCatalogTests
{
    [Fact]
    public async Task Production_picker_filters_to_active_working_employees()
    {
        var employee = new EmployeeListItem(1, "NV01", "Nguyễn An", null, "", null, "0901", "", null,
            EmployeeStatusCodes.Working, true);
        var repository = DispatchProxy.Create<IHumanResourcesRepository, EmployeeRepository>();
        ((EmployeeRepository)(object)repository).Employees =
        [
            employee,
            employee with { Id = 2, Code = "NV02", StatusCode = EmployeeStatusCodes.OnLeave },
            employee with { Id = 3, Code = "NV03", IsActive = false }
        ];
        var session = new CurrentSessionAccessor();
        session.Set(new AuthenticatedSession(1, "tester", "Tester", "ADMIN", 1, "CN01", "Chi nhánh"));
        var viewModel = new EmployeeCatalogViewModel(repository, session, new Administrator(),
            _ => null, selectEmployee: true, _ => true);
        await viewModel.LoadAsync(2);

        Assert.Equal(1, Assert.Single(viewModel.Rows).Id);
        viewModel.Query = "NV02";
        Assert.Empty(viewModel.Rows);
        Assert.False(viewModel.ChooseCommand.CanExecute(null));
        viewModel.Query = "0901";
        viewModel.ChooseCommand.Execute(null);
        Assert.Equal(1, viewModel.ChosenEmployee?.Id);
    }

    public class EmployeeRepository : DispatchProxy
    {
        public IReadOnlyList<EmployeeListItem> Employees { get; set; } = [];
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IHumanResourcesRepository.SearchAsync)
                ? Task.FromResult(Employees)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class Administrator : IAuthorizationService
    {
        public PermissionSnapshot Current { get; } = new(true, []);
        public event EventHandler? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Clear() { }
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
