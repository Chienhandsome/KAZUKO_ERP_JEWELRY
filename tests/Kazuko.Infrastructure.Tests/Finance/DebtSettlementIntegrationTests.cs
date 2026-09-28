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
public sealed class DebtSettlementIntegrationTests : IDisposable
{
    private readonly string? _tempDatabasePath;
    private readonly ConnectionProfile? _originalProfile;
    private readonly bool _databaseReady;
    private readonly FirebirdMaterialReceiptService _receipts = new();
    private readonly FirebirdDebtWorkbenchRepository _workbench = new();
    private readonly FirebirdPayableDebtRepository _debts = new();

    public DebtSettlementIntegrationTests()
    {
        var source = ResolveMigratedDatabasePath();
        if (source is null || !File.Exists(source))
            return;

        _originalProfile = FirebirdDatabase.Profile;
        _tempDatabasePath = Path.Combine(
            Path.GetTempPath(),
            "KazukoDebtSettlementTests",
            Guid.NewGuid().ToString("N"),
            "data.fdb");
        Directory.CreateDirectory(Path.GetDirectoryName(_tempDatabasePath)!);
        File.Copy(source, _tempDatabasePath, overwrite: true);

        FirebirdDatabase.ApplyProfileForCurrentProcess(new ConnectionProfile(
            _originalProfile.Host,
            _originalProfile.Port,
            _tempDatabasePath,
            _originalProfile.UserName,
            string.IsNullOrWhiteSpace(_originalProfile.Password) ? "masterkey" : _originalProfile.Password,
            UseEmbedded: true));

        _databaseReady = HasSettlementSchema();
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
            // best effort
        }
    }

    [Fact]
    public async Task PaySupplierFifo_SplitMethods_CreatesOneVoucherPerAllocation()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var (locationId, materialId, fundId, bankId) = await ResolveMastersAsync(session);
        var supplierId = await FirstSupplierIdAsync();

        // Two debts for same supplier
        await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today, locationId, StockSourceTypes.Supplier, supplierId, null, null,
                [new MaterialReceiptLineDraft(materialId, 1m, 4_000_000m, "VND", 1m, null)],
                0m, Array.Empty<PaymentAllocationDraft>()),
            session);
        await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today, locationId, StockSourceTypes.Supplier, supplierId, null, null,
                [new MaterialReceiptLineDraft(materialId, 1m, 6_000_000m, "VND", 1m, null)],
                0m, Array.Empty<PaymentAllocationDraft>()),
            session);

        var beforeVouchers = await CountCashVouchersAsync();
        await _workbench.PaySupplierFifoAsync(
            supplierId,
            [
                new PaymentAllocationDraft(PaymentMethods.Cash, 5_000_000m, fundId, null, null),
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 5_000_000m, null, bankId, "CK-FIFO")
            ],
            "fifo split",
            session);

        var afterVouchers = await CountCashVouchersAsync();
        Assert.Equal(beforeVouchers + 2, afterVouchers);

        var settlements = await CountSettlementsAsync(SettlementTypes.Payment);
        Assert.True(settlements >= 1);

        var open = await _debts.SearchAsync(
            new PayableDebtSearchFilter(string.Empty, "OPEN", PayablePartyTypes.Supplier),
            session);
        Assert.DoesNotContain(open, d => d.RemainingAmountVnd > 0 &&
            string.Equals(d.PartyType, PayablePartyTypes.Supplier, StringComparison.Ordinal));
    }

    [Fact]
    public async Task PaySupplierFifo_RejectsOverpay()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var (locationId, materialId, fundId, _) = await ResolveMastersAsync(session);
        var supplierId = await FirstSupplierIdAsync();

        await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today, locationId, StockSourceTypes.Supplier, supplierId, null, null,
                [new MaterialReceiptLineDraft(materialId, 1m, 1_000_000m, "VND", 1m, null)],
                0m, Array.Empty<PaymentAllocationDraft>()),
            session);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _workbench.PaySupplierFifoAsync(
            supplierId,
            [new PaymentAllocationDraft(PaymentMethods.Cash, 2_000_000m, fundId, null, null)],
            null,
            session));
    }

    private static bool HasSettlementSchema()
    {
        try
        {
            using var connection = FirebirdDatabase.CreateConnection();
            connection.Open();
            using var command = new FbCommand(
                "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'FINANCIAL_SETTLEMENTS'",
                connection);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveMigratedDatabasePath()
    {
        foreach (var candidate in new[]
                 {
                     @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_37.fdb",
                     @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_36.fdb",
                     @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_35.fdb",
                     @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_34.fdb"
                 })
        {
            if (File.Exists(candidate))
                return candidate;
        }

        try
        {
            if (File.Exists(FirebirdDatabase.DatabasePath))
                return FirebirdDatabase.DatabasePath;
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private static async Task EnsureBankAndSupplierAsync(AuthenticatedSession session)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var fbTx = (FbTransaction)tx;
        await using (var cmd = new FbCommand("""
            INSERT INTO BANK_ACCOUNTS (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
            SELECT C.ID, @BID, 'NH01', 'Vietcombank', '001122', TRUE
            FROM COMPANIES C
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

    private static async Task<(long LocationId, long MaterialId, long FundId, long BankId)> ResolveMastersAsync(
        AuthenticatedSession session)
    {
        await EnsureBankAndSupplierAsync(session);
        var locations = await new FirebirdWarehouseRepository().GetLocationsAsync(session);
        var materials = await new FirebirdWarehouseRepository().SearchMaterialsAsync(string.Empty, "GOLD", session);
        var funds = await new FirebirdCashFundRepository().GetActiveFundsAsync(session);
        var banks = await new FirebirdBankAccountRepository().GetActiveAccountsAsync(session);
        return (locations.First().Id, materials.First().Id, funds.First().Id, banks.First().Id);
    }

    private static async Task<long> FirstSupplierIdAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand(
            "SELECT FIRST 1 ID FROM SUPPLIERS WHERE CODE = 'NCC_TEST'", connection);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> CountCashVouchersAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand("SELECT COUNT(*) FROM CASH_VOUCHERS WHERE STATUS_CODE = 'POSTED'", connection);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task<int> CountSettlementsAsync(string type)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand(
            "SELECT COUNT(*) FROM FINANCIAL_SETTLEMENTS WHERE SETTLEMENT_TYPE = @T AND STATUS_CODE = 'POSTED'",
            connection);
        cmd.Parameters.Add("@T", FbDbType.VarChar, 20).Value = type;
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

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
        Assert.True(await reader.ReadAsync());
        return new AuthenticatedSession(
            1, "test_admin", "Test Administrator", "ADMIN",
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2));
    }
}
