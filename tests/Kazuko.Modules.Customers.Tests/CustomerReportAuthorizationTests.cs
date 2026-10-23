using Kazuko.Core.Customers;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.UserManagement;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CustomerReportAuthorizationTests
{
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    public async Task Report_guard_allows_admin_or_report_viewer_and_rejects_missing_or_locked_grants(
        bool admin, bool reportView, bool locked, bool expectedAllowed)
    {
        var session = CustomerTestContext.Session();
        var rights = new PermissionSnapshot(admin,
        [
            new(1, "CAT_CUSTOMERS", true, true, true, true, false),
            new(1, "REP_VIEW", reportView, false, false, false, locked)
        ]);
        var target = new CustomerRepositoryFake();
        var repo = AuthorizedServiceProxy<ICustomerRepository>.Create(target, new AuthorizationFake(rights), session);
        if (expectedAllowed)
        {
            Assert.Single(await repo.GetTopCustomersAsync(DateTime.Today, DateTime.Today, 10));
            Assert.NotEmpty(await repo.GetSalesSummaryAsync(DateTime.Today, DateTime.Today));
            Assert.Equal(1, target.TopCalls);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.GetTopCustomersAsync(DateTime.Today, DateTime.Today, 10));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.GetSalesSummaryAsync(DateTime.Today, DateTime.Today));
            Assert.Equal(0, target.TopCalls);
        }
        Assert.False(BackendAccess.Allows("ICustomerRepository", "UnmappedMethodAsync", [], rights));
    }

    [Theory]
    [InlineData("CAT_CUSTOMERS", true)]
    [InlineData("SALES_RETAIL", true)]
    [InlineData("UNRELATED", false)]
    public async Task Identity_lookup_requires_customer_read_access_and_the_current_session(string screen, bool allowed)
    {
        var session = CustomerTestContext.Session();
        var rights = new PermissionSnapshot(false, [new(1, screen, true, false, false, false, false)]);
        var repo = AuthorizedServiceProxy<ICustomerRepository>.Create(new CustomerRepositoryFake(), new AuthorizationFake(rights), session);
        if (allowed) Assert.Empty(await repo.FindIdentityMatchesAsync("012345678901", null, null, session.Current!));
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.FindIdentityMatchesAsync("012345678901", null, null, session.Current!));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repo.FindIdentityMatchesAsync("012345678901", null, null, session.Current! with { BranchId = 999 }));
    }

    private sealed class AuthorizationFake(PermissionSnapshot rights) : IAuthorizationService
    {
        public PermissionSnapshot Current => rights;
        public event EventHandler? Changed { add { } remove { } }
        public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Clear() { }
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
