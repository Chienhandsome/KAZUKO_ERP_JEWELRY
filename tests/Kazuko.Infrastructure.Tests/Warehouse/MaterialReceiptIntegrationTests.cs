using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Warehouse;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Warehouse;

[Collection(nameof(FirebirdTestCollection))]
public sealed class MaterialReceiptIntegrationTests : IDisposable
{
    private readonly string? _tempDatabasePath;
    private readonly ConnectionProfile? _originalProfile;
    private readonly bool _databaseReady;
    private readonly FirebirdMaterialReceiptService _receipts = new();
    private readonly FirebirdPayableDebtRepository _debts = new();
    private readonly FirebirdOutsourcedCraftService _craft = new();
    private readonly FirebirdExternalWorkerRepository _workers = new();
    private readonly FirebirdFinishedGoodsIssueService _fg = new();

    public MaterialReceiptIntegrationTests()
    {
        var source = ResolveMigratedDatabasePath();
        if (source is null || !File.Exists(source))
            return;

        _originalProfile = FirebirdDatabase.Profile;
        _tempDatabasePath = Path.Combine(
            Path.GetTempPath(),
            "KazukoWarehousePostingTests",
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
            // best effort
        }
    }

    [Fact]
    public async Task Receipt_FullCash_CreatesNoDebt()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        await EnsureBankAndSupplierAsync(session);
        var (locationId, materialId, fundId, _) = await ResolveMastersAsync(session);

        var result = await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today,
                locationId,
                StockSourceTypes.Supplier,
                await FirstSupplierIdAsync(),
                null,
                null,
                [new MaterialReceiptLineDraft(materialId, 1m, 1_000_000m, "VND", 1m, null)],
                1_000_000m,
                [new PaymentAllocationDraft(PaymentMethods.Cash, 1_000_000m, fundId, null, null)]),
            session);

        Assert.Equal(0m, result.DebtAmountVnd);
        Assert.Null(result.PayableDebtId);
    }

    [Fact]
    public async Task Receipt_SplitPayment_AndPartialDebt_ThenPayDebt()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var (locationId, materialId, fundId, bankId) = await ResolveMastersAsync(session);
        var supplierId = await FirstSupplierIdAsync();

        var result = await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today,
                locationId,
                StockSourceTypes.Supplier,
                supplierId,
                null,
                null,
                [new MaterialReceiptLineDraft(materialId, 1m, 10_000_000m, "VND", 1m, null)],
                5_000_000m,
                [
                    new PaymentAllocationDraft(PaymentMethods.Cash, 2_500_000m, fundId, null, null),
                    new PaymentAllocationDraft(PaymentMethods.BankTransfer, 2_500_000m, null, bankId, "CK-1")
                ]),
            session);

        Assert.Equal(5_000_000m, result.DebtAmountVnd);
        Assert.NotNull(result.PayableDebtId);

        await _debts.PayAsync(
            new PayableDebtPaymentRequest(
                result.PayableDebtId!.Value,
                [
                    new PaymentAllocationDraft(PaymentMethods.Cash, 2_000_000m, fundId, null, null),
                    new PaymentAllocationDraft(PaymentMethods.BankTransfer, 3_000_000m, null, bankId, "CK-2")
                ],
                "pay remaining"),
            session);

        var debts = await _debts.SearchAsync(
            new PayableDebtSearchFilter(result.DocumentCode, "PAID", string.Empty),
            session);
        // Search by debt code after pay - status PAID
        var open = await _debts.SearchAsync(
            new PayableDebtSearchFilter(string.Empty, "OPEN", string.Empty),
            session);
        Assert.DoesNotContain(open, d => d.Id == result.PayableDebtId);
    }

    [Fact]
    public async Task Receipt_RejectsAllocationGreaterThanPaid()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var (locationId, materialId, fundId, _) = await ResolveMastersAsync(session);

        var supplierId = await FirstSupplierIdAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today,
                locationId,
                StockSourceTypes.Supplier,
                supplierId,
                null,
                null,
                [new MaterialReceiptLineDraft(materialId, 1m, 1_000_000m, "VND", 1m, null)],
                500_000m,
                [new PaymentAllocationDraft(PaymentMethods.Cash, 600_000m, fundId, null, null)]),
            session));
    }

    [Fact]
    public async Task OutsourcedIssue_DoesNotCreatePayable()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var (locationId, materialId, _, _) = await ResolveMastersAsync(session);
        // seed stock first
        var fundId = (await new FirebirdCashFundRepository().GetActiveFundsAsync(session)).First().Id;
        await _receipts.CreateDraftAndPostAsync(
            new MaterialReceiptDraft(
                DateTime.Today, locationId, StockSourceTypes.Supplier, await FirstSupplierIdAsync(), null, null,
                [new MaterialReceiptLineDraft(materialId, 5m, 100_000m, "VND", 1m, null)],
                500_000m,
                [new PaymentAllocationDraft(PaymentMethods.Cash, 500_000m, fundId, null, null)]),
            session);

        var worker = await _workers.SaveAsync(
            new ExternalWorkerSaveRequest(null, "THO01", "Thợ Test", "090", "HN", "", "", "", true),
            session);

        var beforeDebts = (await _debts.SearchAsync(new PayableDebtSearchFilter(string.Empty, string.Empty, string.Empty), session)).Count;
        await _craft.CreateAndPostIssueAsync(
            new OutsourcedCraftIssueDraft(
                DateTime.Today, locationId, worker.Id, DateTime.Today.AddDays(7),
                "Giao", "Nhận", null, "Làm nhẫn", null,
                [new OutsourcedCraftIssueLineDraft(materialId, 1m, null)]),
            session);
        var afterDebts = (await _debts.SearchAsync(new PayableDebtSearchFilter(string.Empty, string.Empty, string.Empty), session)).Count;
        Assert.Equal(beforeDebts, afterDebts);
    }

    [Fact]
    public async Task FinishedGoodsIssue_RejectsDraftOrder()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var locationId = (await new FirebirdWarehouseRepository().GetLocationsAsync(session)).First().Id;

        var draftOrderId = await InsertDraftSalesOrderAsync(session);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _fg.CreateAndPostAsync(
            new FinishedGoodsIssueDraft(
                draftOrderId,
                DateTime.Today,
                locationId,
                "A",
                "B",
                null,
                [new FinishedGoodsIssueLineDraft(1)]),
            session));
    }

    private static string? ResolveMigratedDatabasePath()
    {
        var migrated = Path.Combine(
            @"D:\Working\KAZUKO_ERP_JEWELRY\data\backups\mig_test_34.fdb");
        if (File.Exists(migrated))
            return migrated;

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

    private static async Task<long> InsertDraftSalesOrderAsync(AuthenticatedSession session)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var cmd = new FbCommand("""
            INSERT INTO SALES_ORDERS
                (COMPANY_ID, BRANCH_ID, CODE, ORDER_TYPE, CUSTOMER_ID, ORDER_DATE, STATUS_CODE,
                 SUBTOTAL, DISCOUNT_AMOUNT, TAX_AMOUNT, GRAND_TOTAL, CREATED_AT)
            VALUES
                ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BID, @CODE, 'CUSTOM',
                 (SELECT FIRST 1 ID FROM CUSTOMERS), CURRENT_DATE, 'DRAFT',
                 0, 0, 0, 0, CURRENT_TIMESTAMP)
            RETURNING ID
            """, connection);
        cmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        cmd.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = "DH-TEST-" + Guid.NewGuid().ToString("N")[..8];
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
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
            1,
            "test_admin",
            "Test Administrator",
            "ADMIN",
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2));
    }
}
