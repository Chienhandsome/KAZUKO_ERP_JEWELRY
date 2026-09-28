using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

[Collection(nameof(FirebirdTestCollection))]
public sealed class FinancialVoucherIntegrationTests : IDisposable
{
    private readonly string? _tempDatabasePath;
    private readonly ConnectionProfile? _originalProfile;
    private readonly bool _databaseReady;
    private readonly FirebirdFinancialVoucherPostingService _vouchers = new();
    private readonly FirebirdCashFundRepository _funds = new();
    private readonly FirebirdBankAccountRepository _banks = new();

    public FinancialVoucherIntegrationTests()
    {
        var source = ResolveMigratedDatabasePath();
        if (source is null || !File.Exists(source))
            return;

        _originalProfile = FirebirdDatabase.Profile;
        _tempDatabasePath = Path.Combine(
            Path.GetTempPath(),
            "KazukoFinancialVoucherTests",
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

        _databaseReady = HasIdempotencySchema();
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
    public async Task PostReceipt_Cash_CreatesInVoucher()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var fundId = (await _funds.GetActiveFundsAsync(session)).First().Id;

        var result = await _vouchers.PostReceiptAsync(
            new FinancialVoucherPostRequest(
                new PaymentAllocationDraft(PaymentMethods.Cash, 1_000_000m, fundId, null, null),
                "TEST",
                1,
                "TEST_ALLOC",
                NextAllocId()),
            session);

        Assert.False(result.AlreadyExisted);
        Assert.Equal(FinancialVoucherTypes.In, result.VoucherType);
        Assert.StartsWith("PT-", result.Code);
    }

    [Fact]
    public async Task PostReceipt_BankTransfer_CreatesInVoucher()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        await EnsureBankAsync(session);
        var bankId = (await _banks.GetActiveAccountsAsync(session)).First().Id;

        var result = await _vouchers.PostReceiptAsync(
            new FinancialVoucherPostRequest(
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 2_000_000m, null, bankId, "CK-R"),
                "TEST",
                2,
                "TEST_ALLOC",
                NextAllocId()),
            session);

        Assert.Equal(FinancialVoucherTypes.In, result.VoucherType);
    }

    [Fact]
    public async Task PostPayment_CashAndBank_CreateOutVouchers()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        await EnsureBankAsync(session);
        var fundId = (await _funds.GetActiveFundsAsync(session)).First().Id;
        var bankId = (await _banks.GetActiveAccountsAsync(session)).First().Id;

        var cash = await _vouchers.PostPaymentAsync(
            new FinancialVoucherPostRequest(
                new PaymentAllocationDraft(PaymentMethods.Cash, 3_000_000m, fundId, null, null),
                "TEST",
                3,
                "TEST_ALLOC",
                NextAllocId()),
            session);
        var bank = await _vouchers.PostPaymentAsync(
            new FinancialVoucherPostRequest(
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 4_000_000m, null, bankId, "CK-P"),
                "TEST",
                3,
                "TEST_ALLOC",
                NextAllocId()),
            session);

        Assert.Equal(FinancialVoucherTypes.Out, cash.VoucherType);
        Assert.Equal(FinancialVoucherTypes.Out, bank.VoucherType);
        Assert.StartsWith("PC-", cash.Code);
    }

    [Fact]
    public async Task PostPayment_InvalidFund_IsRejected()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _vouchers.PostPaymentAsync(
            new FinancialVoucherPostRequest(
                new PaymentAllocationDraft(PaymentMethods.Cash, 1_000_000m, 999_999_999, null, null),
                "TEST",
                4,
                "TEST_ALLOC",
                NextAllocId()),
            session));
    }

    [Fact]
    public async Task PostReceipt_SameIdempotencyKey_DoesNotDuplicate()
    {
        if (!_databaseReady) return;
        var session = await ResolveSessionAsync();
        var fundId = (await _funds.GetActiveFundsAsync(session)).First().Id;
        var allocId = NextAllocId();
        var request = new FinancialVoucherPostRequest(
            new PaymentAllocationDraft(PaymentMethods.Cash, 500_000m, fundId, null, null),
            "TEST",
            5,
            "TEST_ALLOC",
            allocId);

        var first = await _vouchers.PostReceiptAsync(request, session);
        var second = await _vouchers.PostReceiptAsync(request, session);

        Assert.False(first.AlreadyExisted);
        Assert.True(second.AlreadyExisted);
        Assert.Equal(first.VoucherId, second.VoucherId);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM CASH_VOUCHERS
            WHERE SOURCE_ALLOCATION_TYPE = 'TEST_ALLOC' AND SOURCE_ALLOCATION_ID = @AID
            """, connection);
        command.Parameters.Add("@AID", FbDbType.BigInt).Value = allocId;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    private static long NextAllocId() =>
        Math.Abs(BitConverter.ToInt64(Guid.NewGuid().ToByteArray(), 0) % 900_000_000_000L) + 1;

    private static bool HasIdempotencySchema()
    {
        try
        {
            using var connection = FirebirdDatabase.CreateConnection();
            connection.Open();
            using var command = new FbCommand("""
                SELECT COUNT(*) FROM RDB$RELATION_FIELDS
                WHERE RDB$RELATION_NAME = 'CASH_VOUCHERS'
                  AND RDB$FIELD_NAME = 'SOURCE_ALLOCATION_TYPE'
                """, connection);
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

    private static async Task EnsureBankAsync(AuthenticatedSession session)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        var fbTx = (FbTransaction)tx;
        await using var cmd = new FbCommand("""
            INSERT INTO BANK_ACCOUNTS (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
            SELECT C.ID, @BID, 'NH01', 'Vietcombank', '001122', TRUE
            FROM COMPANIES C
            WHERE C.CODE = 'KAZUKO'
              AND NOT EXISTS (SELECT 1 FROM BANK_ACCOUNTS B WHERE B.CODE = 'NH01' AND B.COMPANY_ID = C.ID)
            """, connection, fbTx);
        cmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
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
        if (!await reader.ReadAsync())
            throw new InvalidOperationException("Thiếu branch để chạy test.");
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
