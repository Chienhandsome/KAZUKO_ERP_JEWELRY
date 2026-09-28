using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Warehouse;

[Collection(nameof(FirebirdTestCollection))]
public sealed class WarehouseRepositoryTests : IDisposable
{
    private readonly string? _tempDatabasePath;
    private readonly ConnectionProfile? _originalProfile;
    private readonly FirebirdWarehouseRepository _repository = new();
    private readonly bool _databaseReady;

    public WarehouseRepositoryTests()
    {
        if (!IsSourceDatabaseConfigured())
            return;

        _originalProfile = FirebirdDatabase.Profile;
        _tempDatabasePath = Path.Combine(
            Path.GetTempPath(),
            "KazukoWarehouseTests",
            Guid.NewGuid().ToString("N"),
            "data.fdb");
        Directory.CreateDirectory(Path.GetDirectoryName(_tempDatabasePath)!);
        File.Copy(FirebirdDatabase.DatabasePath, _tempDatabasePath, overwrite: true);

        FirebirdDatabase.ApplyProfileForCurrentProcess(new ConnectionProfile(
            _originalProfile.Host,
            _originalProfile.Port,
            _tempDatabasePath,
            _originalProfile.UserName,
            _originalProfile.Password,
            _originalProfile.UseEmbedded));

        _databaseReady = true;
    }

    public void Dispose()
    {
        if (_originalProfile is not null)
            FirebirdDatabase.ApplyProfileForCurrentProcess(_originalProfile);

        FbConnection.ClearAllPools();

        if (_tempDatabasePath is null)
            return;

        try
        {
            var dir = Path.GetDirectoryName(_tempDatabasePath);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup of temp DB copy.
        }
    }

    [Fact]
    public void WarehouseLocationOption_ToString_ReturnsDisplayName()
    {
        var location = new WarehouseLocationOption(1, 1, "KHO1", "Kho chính", "WAREHOUSE");
        Assert.Equal("KHO1 - Kho chính", location.ToString());
    }

    [Fact]
    public async Task GetLocationsAsync_ThrowsWhenSessionIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.GetLocationsAsync(null!));
    }

    [Fact]
    public async Task GetLocationsAsync_ThrowsWhenBranchIdInvalid()
    {
        var session = CreateSession(branchId: 0);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.GetLocationsAsync(session));
        Assert.Contains("chi nhánh", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_GetLocations_AreBranchScoped()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var locations = await _repository.GetLocationsAsync(session);
        Assert.All(locations, location => Assert.Equal(session.BranchId, location.BranchId));
    }

    [Fact]
    public async Task Database_SearchBalances_RespectsCompanyAndBranch()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var balances = await _repository.SearchMaterialBalancesAsync(
            new MaterialBalanceSearchFilter(string.Empty, string.Empty, 0, false),
            session);

        Assert.NotNull(balances);
        foreach (var balance in balances)
        {
            var locationBranch = await ScalarLongAsync(
                "SELECT BRANCH_ID FROM LOCATIONS WHERE ID=@ID",
                ("@ID", balance.LocationId));
            Assert.Equal(session.BranchId, locationBranch);

            var companyCode = await ScalarStringAsync(
                """
                SELECT C.CODE
                FROM MATERIALS M
                JOIN COMPANIES C ON C.ID = M.COMPANY_ID
                WHERE M.ID=@ID
                """,
                ("@ID", balance.MaterialId));
            Assert.Equal("KAZUKO", companyCode);
        }
    }

    [Fact]
    public async Task Database_SearchBalances_FiltersByTypeAndLowStock()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var gold = await _repository.SearchMaterialBalancesAsync(
            new MaterialBalanceSearchFilter(string.Empty, "GOLD", 0, false),
            session);
        Assert.All(gold, item => Assert.Equal("GOLD", item.MaterialType));

        var low = await _repository.SearchMaterialBalancesAsync(
            new MaterialBalanceSearchFilter(string.Empty, string.Empty, 0, true),
            session);
        Assert.All(low, item => Assert.True(item.IsLowStock));
    }

    [Fact]
    public async Task Database_SearchDocuments_AreBranchAndCompanyScoped()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var documents = await _repository.SearchDocumentsAsync(
            new StockDocumentSearchFilter(string.Empty, string.Empty, string.Empty, null, null),
            session);

        foreach (var document in documents)
        {
            var branchId = await ScalarLongAsync(
                "SELECT BRANCH_ID FROM STOCK_DOCUMENTS WHERE ID=@ID",
                ("@ID", document.Id));
            Assert.Equal(session.BranchId, branchId);

            var companyCode = await ScalarStringAsync(
                """
                SELECT C.CODE
                FROM STOCK_DOCUMENTS D
                JOIN COMPANIES C ON C.ID = D.COMPANY_ID
                WHERE D.ID=@ID
                """,
                ("@ID", document.Id));
            Assert.Equal("KAZUKO", companyCode);
        }
    }

    [Fact]
    public async Task Database_GetDocument_ReturnsNullForOtherBranch()
    {
        if (!_databaseReady)
            return;

        var foreignId = await FindForeignBranchDocumentIdAsync();
        if (foreignId is null)
            return;

        var session = await ResolveSessionAsync();
        var detail = await _repository.GetDocumentAsync(foreignId.Value, session);
        Assert.Null(detail);

        var lines = await _repository.GetDocumentLinesAsync(foreignId.Value, session);
        Assert.Empty(lines);
    }

    [Fact]
    public async Task Database_GetDocument_ReturnsOwnedDocument()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var documents = await _repository.SearchDocumentsAsync(
            new StockDocumentSearchFilter(string.Empty, string.Empty, string.Empty, null, null),
            session);
        if (documents.Count == 0)
            return;

        var first = documents[0];
        var detail = await _repository.GetDocumentAsync(first.Id, session);
        Assert.NotNull(detail);
        Assert.Equal(first.Code, detail!.Code);

        var lines = await _repository.GetDocumentLinesAsync(first.Id, session);
        Assert.Equal(first.LineCount, lines.Count);
    }

    [Fact]
    public async Task Database_SearchMovements_AreBranchScoped()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var movements = await _repository.SearchMovementsAsync(
            new StockMovementSearchFilter(0, 0, string.Empty),
            session);

        foreach (var movement in movements)
        {
            var branchId = await ScalarLongAsync(
                """
                SELECT D.BRANCH_ID
                FROM STOCK_MOVEMENTS X
                JOIN STOCK_DOCUMENTS D ON D.ID = X.DOCUMENT_ID
                WHERE X.ID=@ID
                """,
                ("@ID", movement.Id));
            Assert.Equal(session.BranchId, branchId);
        }
    }

    [Fact]
    public async Task Database_SearchDocuments_FiltersByType()
    {
        if (!_databaseReady)
            return;

        var session = await ResolveSessionAsync();
        var receipts = await _repository.SearchDocumentsAsync(
            new StockDocumentSearchFilter(string.Empty, "RECEIPT", string.Empty, null, null),
            session);
        Assert.All(receipts, item => Assert.Equal("RECEIPT", item.DocumentType));
    }

    private static AuthenticatedSession CreateSession(long branchId = 1) =>
        new(
            UserId: 1,
            Username: "test_admin",
            DisplayName: "Test Administrator",
            RoleCode: "ADMIN",
            BranchId: branchId,
            BranchCode: "CN1",
            BranchName: "Chi nhánh 1");

    private static async Task<AuthenticatedSession> ResolveSessionAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FbCommand("""
            SELECT FIRST 1 B.ID, B.CODE, B.NAME
            FROM BRANCHES B
            WHERE B.IS_ACTIVE = TRUE
            ORDER BY B.ID
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return CreateSession();

        return new AuthenticatedSession(
            1,
            "test_admin",
            "Test Administrator",
            "ADMIN",
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2));
    }

    private static async Task<long?> FindForeignBranchDocumentIdAsync()
    {
        var session = await ResolveSessionAsync();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FbCommand("""
            SELECT FIRST 1 ID
            FROM STOCK_DOCUMENTS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND BRANCH_ID <> @BRANCH_ID
            ORDER BY ID
            """, connection);
        command.Parameters.AddWithValue("@BRANCH_ID", session.BranchId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static async Task<long> ScalarLongAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FbCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string> ScalarStringAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FbCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return Convert.ToString(await command.ExecuteScalarAsync()) ?? string.Empty;
    }

    private static bool IsSourceDatabaseConfigured()
    {
        try
        {
            var profile = FirebirdDatabase.Profile;
            if (string.IsNullOrWhiteSpace(profile.Password) || !File.Exists(FirebirdDatabase.DatabasePath))
                return false;

            // Prefer a readable source so we can copy; locked exclusive open is treated as unavailable.
            var probePath = Path.Combine(Path.GetTempPath(), "KazukoWarehouseProbe-" + Guid.NewGuid().ToString("N") + ".fdb");
            try
            {
                File.Copy(FirebirdDatabase.DatabasePath, probePath, overwrite: true);
                return true;
            }
            finally
            {
                try { File.Delete(probePath); } catch { /* ignore */ }
            }
        }
        catch
        {
            return false;
        }
    }
}
