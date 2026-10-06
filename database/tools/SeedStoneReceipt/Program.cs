using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

// One-shot: post a sample STONE material receipt via FirebirdMaterialReceiptService.
// Usage (from repo root):
//   dotnet run --project database/tools/SeedStoneReceipt -- [optional-path-to-data.fdb]

var repoRoot = FindRepoRoot();
var dbPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.Combine(repoRoot, "data", "DATA.FDB");
if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Database not found: {dbPath}");
    return 1;
}

var bundledFb = Path.Combine(repoRoot, "firebird", "Firebird-5.0.4.1812-0-windows-x64");
if (Directory.Exists(bundledFb)
    && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_HOME")))
{
    Environment.SetEnvironmentVariable("KAZUKO_FIREBIRD_HOME", bundledFb);
}

var password = Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_PASSWORD");
if (string.IsNullOrWhiteSpace(password))
    password = "masterkey";
var user = Environment.GetEnvironmentVariable("KAZUKO_FIREBIRD_USER");
if (string.IsNullOrWhiteSpace(user))
    user = "SYSDBA";

FirebirdDatabase.ApplyProfileForCurrentProcess(new ConnectionProfile(
    "localhost",
    3050,
    dbPath,
    user,
    password,
    UseEmbedded: false));

Console.WriteLine($"Using database: {dbPath}");

await using var connection = FirebirdDatabase.CreateConnection();
await connection.OpenAsync();

var session = await LoadAdminSessionAsync(connection);
var locationId = await ScalarAsync(connection, """
    SELECT FIRST 1 ID FROM LOCATIONS
    WHERE CODE = 'KHO_TRUNG_TAM' AND IS_ACTIVE = TRUE AND INVENTORY_SCOPE = 'RAW_MATERIAL'
    """);
var supplierId = await ScalarAsync(connection, """
    SELECT FIRST 1 ID FROM SUPPLIERS WHERE IS_ACTIVE = TRUE ORDER BY ID
    """);
var fundId = await ScalarAsync(connection, """
    SELECT FIRST 1 ID FROM CASH_FUNDS
    WHERE BRANCH_ID = @BID AND IS_ACTIVE = TRUE
    ORDER BY CASE WHEN IS_DEFAULT = TRUE THEN 0 ELSE 1 END, ID
    """, ("@BID", session.BranchId));

if (locationId <= 0 || supplierId <= 0 || fundId <= 0)
{
    Console.Error.WriteLine("Missing NVL location, supplier, or cash fund.");
    return 2;
}

var stoneLines = new (string Code, decimal Qty)[]
{
    ("STONE_KC_RD_050", 5m),
    ("STONE_KC_RD_100", 2m),
    ("STONE_CZ_RD_2MM", 200m),
    ("STONE_CZ_RD_3MM", 150m),
    ("STONE_RUBY_OV_080", 8m),
    ("STONE_PEARL_RD_7MM", 20m),
};

var lines = new List<MaterialReceiptLineDraft>();
foreach (var (code, qty) in stoneLines)
{
    await using var cmd = new FbCommand("""
        SELECT ID, REFERENCE_COST FROM MATERIALS
        WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
          AND CODE = @CODE AND MATERIAL_TYPE = 'STONE' AND IS_ACTIVE = TRUE
        """, connection);
    cmd.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        Console.Error.WriteLine($"Stone material missing: {code}");
        return 3;
    }

    var materialId = reader.GetInt64(0);
    var unitPrice = reader.GetDecimal(1);
    if (unitPrice <= 0)
        unitPrice = 1_000m;
    lines.Add(new MaterialReceiptLineDraft(materialId, qty, unitPrice, "VND", 1m, null));
    Console.WriteLine($"  Line {code}: qty={qty:N0} @ {unitPrice:N0}");
}

var total = lines.Sum(l => Math.Round(l.Quantity * l.UnitPrice * l.ExchangeRate, 2, MidpointRounding.AwayFromZero));
var receipts = new FirebirdMaterialReceiptService();
var result = await receipts.CreateDraftAndPostAsync(
    new MaterialReceiptDraft(
        DateTime.Today,
        locationId,
        StockSourceTypes.Supplier,
        supplierId,
        null,
        "Seed đá NVL mẫu (KC/CZ/Ruby/Ngọc trai)",
        lines,
        total,
        [new PaymentAllocationDraft(PaymentMethods.Cash, total, fundId, null, "SEED-STONE")]),
    session);

Console.WriteLine($"Posted {result.DocumentCode}: total={result.TotalAmountVnd:N0}, paid={result.PaidAmountVnd:N0}, debt={result.DebtAmountVnd:N0}");

await using (var balCmd = new FbCommand("""
    SELECT M.CODE, B.QUANTITY, B.AVERAGE_COST
    FROM STOCK_BALANCES B
    JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
    WHERE B.LOCATION_ID = @LOC AND M.MATERIAL_TYPE = 'STONE'
    ORDER BY M.CODE
    """, connection))
{
    balCmd.Parameters.Add("@LOC", FbDbType.BigInt).Value = locationId;
    await using var reader = await balCmd.ExecuteReaderAsync();
    Console.WriteLine("STONE balances at KHO_TRUNG_TAM:");
    while (await reader.ReadAsync())
        Console.WriteLine($"  {reader.GetString(0)}: {reader.GetDecimal(1):N0} viên @ {reader.GetDecimal(2):N0}");
}

return 0;

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
    {
        if (File.Exists(Path.Combine(dir.FullName, "KAZUKO_ERP_JEWELRY.sln"))
            || Directory.Exists(Path.Combine(dir.FullName, "database", "migrations")))
            return dir.FullName;
    }

    return Directory.GetCurrentDirectory();
}

static async Task<AuthenticatedSession> LoadAdminSessionAsync(FbConnection connection)
{
    await using var cmd = new FbCommand("""
        SELECT FIRST 1 U.ID, U.USERNAME, COALESCE(U.DISPLAY_NAME, U.USERNAME),
               COALESCE(R.CODE, 'ADMIN'), B.ID, B.CODE, B.NAME
        FROM USERS U
        LEFT JOIN ROLES R ON R.ID = U.ROLE_ID
        CROSS JOIN BRANCHES B
        WHERE UPPER(U.USERNAME) = 'ADMIN'
          AND B.CODE = 'CN_TRUNG_TAM'
          AND B.IS_ACTIVE = TRUE
        """, connection);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        throw new InvalidOperationException("Không tìm thấy ADMIN / chi nhánh CN_TRUNG_TAM.");

    return new AuthenticatedSession(
        reader.GetInt64(0),
        reader.GetString(1),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetInt64(4),
        reader.GetString(5),
        reader.GetString(6));
}

static async Task<long> ScalarAsync(
    FbConnection connection,
    string sql,
    params (string Name, object Value)[] parameters)
{
    await using var cmd = new FbCommand(sql, connection);
    foreach (var (name, value) in parameters)
        cmd.Parameters.Add(name, value is long ? FbDbType.BigInt : FbDbType.VarChar).Value = value;
    var result = await cmd.ExecuteScalarAsync();
    return result is null or DBNull ? 0 : Convert.ToInt64(result);
}
