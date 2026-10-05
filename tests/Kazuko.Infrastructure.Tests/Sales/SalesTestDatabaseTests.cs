using FirebirdSql.Data.FirebirdClient;
using Kazuko.Infrastructure.Data;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SalesTestDatabaseTests
{
    [Fact]
    public void Constructor_failure_restores_profile_clears_pools_and_deletes_copy()
    {
        var originalProfile = FirebirdDatabase.Profile;
        string? copiedDatabasePath = null;

        var error = Assert.Throws<InvalidOperationException>(() =>
            new SalesTestDatabase(path =>
            {
                copiedDatabasePath = path;
                throw new InvalidOperationException("Injected setup failure");
            }));

        Assert.Equal("Injected setup failure", error.Message);
        Assert.Equal(originalProfile, FirebirdDatabase.Profile);
        Assert.NotNull(copiedDatabasePath);
        Assert.False(File.Exists(copiedDatabasePath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(copiedDatabasePath)));

        // Pool clearing is observable because the copied database can be deleted on Windows.
        FbConnection.ClearAllPools();
    }
}
