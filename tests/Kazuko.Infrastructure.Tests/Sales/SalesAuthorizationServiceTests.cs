using Kazuko.Infrastructure.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SalesAuthorizationServiceTests : IDisposable
{
    private readonly SalesTestDatabase _database = new();
    private readonly FirebirdSalesAuthorizationService _service = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task GetPermissionsAsync_DatabaseAdminGetsAllSixPermissionsDespiteClaimedRole()
    {
        var permissions = await _service.GetPermissionsAsync(
            _database.BranchSession(_database.AdminUserId, claimedRoleCode: "SALE"));

        Assert.Equal(
            new[] { true, true, true, true, true, true, true },
            new[]
            {
                permissions.CanPost,
                permissions.CanOverridePrice,
                permissions.CanApproveDebt,
                permissions.CanChangeSalesperson,
                permissions.CanReadHistory,
                permissions.CanPrintReceipt,
                permissions.CanPostWholesale
            });
    }

    [Fact]
    public async Task GetPermissionsAsync_NormalRoleUsesOnlyEffectiveDatabaseGrants()
    {
        var permissions = await _service.GetPermissionsAsync(
            _database.BranchSession(_database.LimitedUserId, claimedRoleCode: "ADMIN"));

        Assert.True(permissions.CanPost);
        Assert.False(permissions.CanOverridePrice);
        Assert.False(permissions.CanApproveDebt);
        Assert.False(permissions.CanChangeSalesperson);
        Assert.True(permissions.CanReadHistory);
        Assert.False(permissions.CanPrintReceipt);
        Assert.False(permissions.CanPostWholesale);
    }

    [Fact]
    public async Task GetPermissionsAsync_ActiveOtherCompanyAdminClaimGetsNoPermissionsInCurrentCompanyBranch()
    {
        var permissions = await _service.GetPermissionsAsync(
            _database.BranchSession(_database.OtherCompanyUserId, claimedRoleCode: "ADMIN"));

        Assert.Equal(
            new[] { false, false, false, false, false, false, false },
            new[]
            {
                permissions.CanPost,
                permissions.CanOverridePrice,
                permissions.CanApproveDebt,
                permissions.CanChangeSalesperson,
                permissions.CanReadHistory,
                permissions.CanPrintReceipt,
                permissions.CanPostWholesale
            });
    }
}
