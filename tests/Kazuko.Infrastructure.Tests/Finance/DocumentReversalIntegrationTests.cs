using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Warehouse;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

[Collection(nameof(FirebirdTestCollection))]
public sealed class DocumentReversalIntegrationTests : IDisposable
{
    private readonly string? _tempDatabasePath;
    private readonly ConnectionProfile? _originalProfile;
    private readonly bool _databaseReady;
    private readonly FirebirdMaterialReceiptService _receipts = new();
    private readonly FirebirdDocumentReversalService _reversals = new();
    private readonly FirebirdPartyCreditService _credits = new();

    public DocumentReversalIntegrationTests()
    {
        var source = ResolvePath();
        if (source is null || !File.Exists(source))
            return;

        _originalProfile = FirebirdDatabase.Profile;
        _tempDatabasePath = Path.Combine(Path.GetTempPath(), "KazukoReversalTests", Guid.NewGuid().ToString("N"), "data.fdb");
        Directory.CreateDirectory(Path.GetDirectoryName(_tempDatabasePath)!);
        File.Copy(source, _tempDatabasePath, overwrite: true);
        FirebirdDatabase.ApplyProfileForCurrentProcess(new ConnectionProfile(
            _originalProfile.Host, _originalProfile.Port, _tempDatabasePath,
            _originalProfile.UserName,
            string.IsNullOrWhiteSpace(_originalProfile.Password) ? "masterkey" : _originalProfile.Password,
            UseEmbedded: true));
        _databaseReady = HasReversalSchema();
    }

    public void Dispose()
    {
        if (_originalProfile is not null)
            FirebirdDatabase.ApplyProfileForCurrentProcess(_originalProfile);
        FbConnection.ClearAllPools();
        if (_tempDatabasePath is null) return;
        try
        {
            var dir = Path.GetDirectoryName(_tempDatabasePath);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    [Fact]
    public async Task ReverseReceipt_IsIdempotent_SecondCallFails()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var (locationId, materialId, fundId, _) = await MastersAsync(session);
        var supplierId = await SupplierIdAsync();

        var posted = await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today, locationId, StockSourceTypes.Supplier, supplierId, null, null,
                [new MaterialReceiptLineDraft(materialId, 1m, 1_000_000m, "VND", 1m, null)],
                1_000_000m,
                [new PaymentAllocationDraft(PaymentMethods.Cash, 1_000_000m, fundId, null, null)]),
            session);

        var first = await _reversals.ReverseStockDocumentAsync(posted.DocumentId, "test reverse", session);
        Assert.False(first.AlreadyReversed);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _reversals.ReverseStockDocumentAsync(posted.DocumentId, "again", session));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand("""
            SELECT COUNT(*) FROM DOCUMENT_REVERSALS
            WHERE SOURCE_DOCUMENT_TYPE = 'STOCK_DOCUMENT' AND SOURCE_DOCUMENT_ID = @ID
            """, connection);
        cmd.Parameters.Add("@ID", FbDbType.BigInt).Value = posted.DocumentId;
        Assert.Equal(1, Convert.ToInt32(await cmd.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task PartyCredit_DoesNotCreateCashVoucher()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        await EnsureBankSupplierAsync(session);
        var before = await CountVouchersAsync();
        var supplierId = await SupplierIdAsync();

        var credit = await _credits.CreateAsync(
            new PartyCreditDraft(
                SettlementPartyTypes.Supplier,
                supplierId,
                PartyCreditKinds.SupplierReturnHold,
                2_000_000m,
                "STOCK_DOCUMENT",
                1,
                "NCC giữ tiền cấn lần sau"),
            session);

        Assert.True(credit.CreditId > 0);
        Assert.Equal(before, await CountVouchersAsync());
    }

    private static bool HasReversalSchema()
    {
        try
        {
            using var connection = FirebirdDatabase.CreateConnection();
            connection.Open();
            using var command = new FbCommand(
                "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'DOCUMENT_REVERSALS'", connection);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
        catch { return false; }
    }

    private static string? ResolvePath()
    {
        foreach (var p in new[]
                 {
                     @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_38.fdb",
                     @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_37.fdb"
                 })
            if (File.Exists(p)) return p;
        try
        {
            if (File.Exists(FirebirdDatabase.DatabasePath))
                return FirebirdDatabase.DatabasePath;
        }
        catch { }
        return null;
    }

    private static async Task EnsureBankSupplierAsync(AuthenticatedSession session)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var fbTx = (FbTransaction)tx;
        await using (var cmd = new FbCommand("""
            INSERT INTO BANK_ACCOUNTS (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
            SELECT C.ID, @BID, 'NH01', 'Vietcombank', '001122', TRUE FROM COMPANIES C
            WHERE C.CODE = 'KAZUKO'
              AND NOT EXISTS (SELECT 1 FROM BANK_ACCOUNTS B WHERE B.CODE = 'NH01' AND B.COMPANY_ID = C.ID)
            """, connection, fbTx))
        {
            cmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new FbCommand("""
            INSERT INTO SUPPLIERS (COMPANY_ID, CREATED_BRANCH_ID, SUPPLIER_GROUP_ID, CODE, NAME, IS_ACTIVE, CREATED_AT)
            SELECT C.ID, @BID, G.ID, 'NCC_TEST', 'NCC Test', TRUE, CURRENT_TIMESTAMP
            FROM COMPANIES C
            JOIN SUPPLIER_GROUPS G ON G.COMPANY_ID = C.ID AND G.IS_DEFAULT = TRUE
            WHERE C.CODE = 'KAZUKO'
              AND NOT EXISTS (SELECT 1 FROM SUPPLIERS S WHERE S.CODE = 'NCC_TEST' AND S.COMPANY_ID = C.ID)
            """, connection, fbTx))
        {
            cmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
    }

    private static async Task<(long LocationId, long MaterialId, long FundId, long BankId)> MastersAsync(AuthenticatedSession session)
    {
        await EnsureBankSupplierAsync(session);
        var locations = await new FirebirdWarehouseRepository().GetLocationsAsync(session);
        var materials = await new FirebirdWarehouseRepository().SearchMaterialsAsync(string.Empty, "GOLD", session);
        var funds = await new FirebirdCashFundRepository().GetActiveFundsAsync(session);
        var banks = await new FirebirdBankAccountRepository().GetActiveAccountsAsync(session);
        return (locations.First().Id, materials.First().Id, funds.First().Id, banks.First().Id);
    }

    private static async Task<long> SupplierIdAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand("SELECT FIRST 1 ID FROM SUPPLIERS WHERE CODE = 'NCC_TEST'", connection);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> CountVouchersAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand("SELECT COUNT(*) FROM CASH_VOUCHERS", connection);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<AuthenticatedSession> ResolveSessionAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FbCommand("""
            SELECT FIRST 1 B.ID, B.CODE, B.NAME FROM BRANCHES B WHERE B.IS_ACTIVE = TRUE ORDER BY B.ID
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new AuthenticatedSession(1, "test_admin", "Test Administrator", "ADMIN",
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }
}
