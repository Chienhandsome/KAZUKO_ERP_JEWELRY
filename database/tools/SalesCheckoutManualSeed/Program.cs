using System.Security.Cryptography;
using FirebirdSql.Data.FirebirdClient;

// Disposable manual-test DB seeder for sales-checkout MVP.
// Does not touch the live data\data.fdb path.

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: SalesCheckoutManualSeed <databasePath> <clientLibraryPath> [loginPassword]");
    return 1;
}

var databasePath = Path.GetFullPath(args[0]);
var clientLibrary = Path.GetFullPath(args[1]);
var loginPassword = args.Length >= 3 ? args[2] : "mvp-test";

if (!File.Exists(databasePath))
{
    Console.Error.WriteLine($"Database not found: {databasePath}");
    return 2;
}

if (!File.Exists(clientLibrary))
{
    Console.Error.WriteLine($"Firebird client not found: {clientLibrary}");
    return 3;
}

var password = Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PASSWORD");
if (string.IsNullOrWhiteSpace(password))
    password = "masterkey";

var builder = new FbConnectionStringBuilder
{
    Database = databasePath,
    UserID = Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_USER") ?? "SYSDBA",
    Password = password,
    Charset = "UTF8",
    Dialect = 3,
    Pooling = false,
    ClientLibrary = clientLibrary,
    ServerType = FbServerType.Embedded
};

await using var connection = new FbConnection(builder.ConnectionString);
await connection.OpenAsync();
await using var transaction = await connection.BeginTransactionAsync();

try
{
    var schemaVersion = await ScalarAsync(connection, transaction,
        "SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS");
    if (schemaVersion != 51)
        throw new InvalidOperationException($"Expected schema version 51, found {schemaVersion}.");

    var companyId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'");
    var branchId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM BRANCHES WHERE CODE = 'CN_TRUNG_TAM'");
    var salesFloorId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM LOCATIONS WHERE CODE = 'KHO_BAN_HANG'");
    var retailPriceId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM GOLD_PRICES WHERE CODE = '750'");
    var adminRoleId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM ROLES WHERE CODE = 'ADMIN'");

    await EnsureBankAccountAsync(connection, transaction, companyId, branchId);
    await PrepareSellableProductsAsync(connection, transaction, salesFloorId, retailPriceId);
    await ResetAdminPasswordAsync(connection, transaction, loginPassword);
    await EnsureAdminPermissionsAsync(connection, transaction, adminRoleId);

    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

Console.WriteLine("Seeded sales checkout manual-test database.");
Console.WriteLine($"Database: {databasePath}");
Console.WriteLine("Login user: admin");
Console.WriteLine($"Branch: CN_TRUNG_TAM");
Console.WriteLine("Sellable sample codes: TP-CN-2026-000003, TP-CN-2026-000004, TP-CN-2026-000005");
Console.WriteLine("Old-gold materials: GOLD_750 / GOLD_9999 (RAW_MATERIAL locations available)");
return 0;

static async Task EnsureBankAccountAsync(
    FbConnection connection,
    FbTransaction transaction,
    long companyId,
    long branchId)
{
    var existing = await ScalarAsync(connection, transaction, """
        SELECT COUNT(*) FROM BANK_ACCOUNTS
        WHERE COMPANY_ID = @CID AND BRANCH_ID = @BID AND CODE = 'MVP_BANK'
        """, ("@CID", companyId), ("@BID", branchId));
    if (existing > 0)
        return;

    await ExecuteAsync(connection, transaction, """
        INSERT INTO BANK_ACCOUNTS
            (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
        VALUES (@CID, @BID, 'MVP_BANK', 'MVP Manual Test Bank', 'MVP-001', TRUE)
        """, ("@CID", companyId), ("@BID", branchId));
}

static async Task PrepareSellableProductsAsync(
    FbConnection connection,
    FbTransaction transaction,
    long salesFloorId,
    long retailPriceId)
{
    await ExecuteAsync(connection, transaction, """
        UPDATE FINISHED_PRODUCTS
        SET LOCATION_ID = @LOC,
            RETAIL_GOLD_PRICE_ID = @PRICE,
            STATUS_CODE = 'DISPLAYED'
        WHERE CODE IN ('TP-CN-2026-000003', 'TP-CN-2026-000004', 'TP-CN-2026-000005')
        """, ("@LOC", salesFloorId), ("@PRICE", retailPriceId));
}

static async Task ResetAdminPasswordAsync(
    FbConnection connection,
    FbTransaction transaction,
    string loginPassword)
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(
        loginPassword,
        salt,
        100_000,
        HashAlgorithmName.SHA256,
        32);

    await ExecuteAsync(connection, transaction, """
        UPDATE USERS
        SET PASSWORD_HASH = @HASH,
            PASSWORD_SALT = @SALT,
            PASSWORD_ITERATIONS = 100000,
            IS_ACTIVE = TRUE
        WHERE USERNAME = 'admin'
        """,
        ("@HASH", Convert.ToHexString(hash).ToLowerInvariant()),
        ("@SALT", Convert.ToHexString(salt).ToLowerInvariant()));
}

static async Task EnsureAdminPermissionsAsync(
    FbConnection connection,
    FbTransaction transaction,
    long adminRoleId)
{
    await ExecuteAsync(connection, transaction, """
        INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, IS_ALLOWED)
        SELECT @RID, P.ID, TRUE
        FROM PERMISSIONS P
        WHERE P.CODE STARTING WITH 'sales.'
          AND NOT EXISTS (
              SELECT 1 FROM ROLE_PERMISSIONS RP
              WHERE RP.ROLE_ID = @RID AND RP.PERMISSION_ID = P.ID)
        """, ("@RID", adminRoleId));
}

static async Task<long> ScalarAsync(
    FbConnection connection,
    FbTransaction transaction,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    await using var command = CreateCommand(connection, transaction, sql, parameters);
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static async Task ExecuteAsync(
    FbConnection connection,
    FbTransaction transaction,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    await using var command = CreateCommand(connection, transaction, sql, parameters);
    await command.ExecuteNonQueryAsync();
}

static FbCommand CreateCommand(
    FbConnection connection,
    FbTransaction transaction,
    string sql,
    params (string Name, object? Value)[] parameters)
{
    var command = new FbCommand(sql, connection, transaction);
    foreach (var (name, value) in parameters)
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    return command;
}
