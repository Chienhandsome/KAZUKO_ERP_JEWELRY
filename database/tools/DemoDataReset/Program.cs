using System.Globalization;
using FirebirdSql.Data.FirebirdClient;

// Demo wipe + seed for live data.fdb (schema kept, admin password kept).
// Usage: DemoDataReset <databasePath> <clientLibraryPath>

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: DemoDataReset <databasePath> <fbclient.dll>");
    return 1;
}

var databasePath = Path.GetFullPath(args[0]);
var clientLibrary = Path.GetFullPath(args[1]);
if (!File.Exists(databasePath) || !File.Exists(clientLibrary))
{
    Console.Error.WriteLine("Database or client library not found.");
    return 2;
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
    var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SCHEMA_MIGRATIONS",
        "COMPANIES",
        "BRANCHES",
        "LOCATIONS",
        "PERMISSIONS",
        "ROLES",
        "ROLE_PERMISSIONS",
        "USERS",
        "USER_SCOPES",
        "SYSTEM_SETTINGS"
    };

    var tables = await ListUserTablesAsync(connection, transaction);
    var wipeTargets = tables.Where(t => !keep.Contains(t)).ToList();
    Console.WriteLine($"Wiping {wipeTargets.Count} tables (keeping bootstrap)...");
    await TryNullOptionalFksAsync(connection, transaction, wipeTargets);
    await WipeTablesAsync(connection, transaction, tables, keep);

    var companyId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'");
    var branchId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM BRANCHES WHERE CODE = 'CN_TRUNG_TAM'");
    var finishedLocId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM LOCATIONS WHERE CODE = 'KHO_THANH_PHAM'");
    var salesLocId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM LOCATIONS WHERE CODE = 'KHO_BAN_HANG'");
    var rawLocId = await ScalarAsync(connection, transaction,
        "SELECT FIRST 1 ID FROM LOCATIONS WHERE INVENTORY_SCOPE = 'RAW_MATERIAL' AND IS_ACTIVE = TRUE ORDER BY ID");
    var adminId = await ScalarAsync(connection, transaction,
        "SELECT ID FROM USERS WHERE UPPER(USERNAME) = 'ADMIN'");

    Console.WriteLine("Seeding master demo data...");
    await SeedAsync(connection, transaction, companyId, branchId, finishedLocId, salesLocId, rawLocId, adminId);
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}

Console.WriteLine("Done. Login admin (password unchanged). Seed: customers, suppliers, gold prices, materials, finished products, cash fund, bank account.");
return 0;

static async Task<List<string>> ListUserTablesAsync(FbConnection connection, FbTransaction transaction)
{
    var list = new List<string>();
    await using var cmd = new FbCommand("""
        SELECT TRIM(RDB$RELATION_NAME)
        FROM RDB$RELATIONS
        WHERE RDB$SYSTEM_FLAG = 0
          AND RDB$VIEW_BLR IS NULL
        ORDER BY 1
        """, connection, transaction);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        list.Add(reader.GetString(0));
    return list;
}

static async Task WipeTablesAsync(
    FbConnection connection,
    FbTransaction transaction,
    IReadOnlyList<string> tables,
    HashSet<string> keep)
{
    var targets = tables.Where(t => !keep.Contains(t)).ToList();
    string? lastError = null;
    for (var pass = 1; pass <= 80; pass++)
    {
        var deletedAny = false;
        var failures = 0;
        foreach (var table in targets)
        {
            try
            {
                await using var cmd = new FbCommand($"DELETE FROM \"{table}\"", connection, transaction);
                var n = await cmd.ExecuteNonQueryAsync();
                if (n > 0)
                    deletedAny = true;
            }
            catch (FbException ex)
            {
                failures++;
                lastError = $"{table}: {ex.Message}";
            }
        }

        if (!deletedAny && failures == 0)
            break;

        if (!deletedAny)
        {
            // Stuck on FK cycles: null optional FKs then keep sweeping.
            await TryNullOptionalFksAsync(connection, transaction, targets);
            if (pass > 40)
                break;
        }
    }

    var remaining = new List<string>();
    foreach (var table in targets)
    {
        try
        {
            await using var cmd = new FbCommand($"SELECT COUNT(*) FROM \"{table}\"", connection, transaction);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            if (count > 0)
                remaining.Add($"{table}={count}");
        }
        catch (FbException)
        {
            // ignore missing/permission
        }
    }

    if (remaining.Count > 0)
        throw new InvalidOperationException(
            $"Wipe incomplete: {string.Join(", ", remaining)}. Last error: {lastError}");
}

static async Task TryNullOptionalFksAsync(
    FbConnection connection, FbTransaction transaction, IReadOnlyList<string> targets)
{
    var targetSet = new HashSet<string>(targets, StringComparer.OrdinalIgnoreCase);
    var columns = new List<(string Table, string Column)>();

    await using (var cmd = new FbCommand("""
        SELECT TRIM(RC.RDB$RELATION_NAME) AS CHILD_TABLE,
               TRIM(ISEG.RDB$FIELD_NAME) AS CHILD_COL
        FROM RDB$RELATION_CONSTRAINTS RC
        JOIN RDB$REF_CONSTRAINTS REF ON REF.RDB$CONSTRAINT_NAME = RC.RDB$CONSTRAINT_NAME
        JOIN RDB$INDEX_SEGMENTS ISEG ON ISEG.RDB$INDEX_NAME = RC.RDB$INDEX_NAME
        JOIN RDB$RELATION_FIELDS RF
          ON TRIM(RF.RDB$RELATION_NAME) = TRIM(RC.RDB$RELATION_NAME)
         AND TRIM(RF.RDB$FIELD_NAME) = TRIM(ISEG.RDB$FIELD_NAME)
        WHERE RC.RDB$CONSTRAINT_TYPE = 'FOREIGN KEY'
          AND RF.RDB$NULL_FLAG IS NULL
        """, connection, transaction))
    await using (var reader = await cmd.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var table = reader.GetString(0);
            var column = reader.GetString(1);
            if (targetSet.Contains(table))
                columns.Add((table, column));
        }
    }

    foreach (var (table, column) in columns.Distinct())
    {
        try
        {
            await using var upd = new FbCommand(
                $"UPDATE \"{table}\" SET \"{column}\" = NULL WHERE \"{column}\" IS NOT NULL",
                connection, transaction);
            await upd.ExecuteNonQueryAsync();
        }
        catch (FbException)
        {
            // ignore non-nullable / trigger / multi-col FK edge cases
        }
    }
}

static async Task SeedAsync(
    FbConnection connection,
    FbTransaction transaction,
    long companyId,
    long branchId,
    long finishedLocId,
    long salesLocId,
    long rawLocId,
    long adminId)
{
    // Customer groups
    var groupRegular = await InsertIdAsync(connection, transaction, """
        INSERT INTO CUSTOMER_GROUPS (COMPANY_ID, CODE, NAME, IS_ACTIVE, IS_DEFAULT)
        VALUES (@CID, 'KHACH_THUONG', 'Khách thường', TRUE, TRUE)
        RETURNING ID
        """, ("@CID", companyId));
    _ = await InsertIdAsync(connection, transaction, """
        INSERT INTO CUSTOMER_GROUPS (COMPANY_ID, CODE, NAME, IS_ACTIVE, IS_DEFAULT)
        VALUES (@CID, 'KHACH_VIP', 'Khách VIP', TRUE, FALSE)
        RETURNING ID
        """, ("@CID", companyId));

    // Customers
    await InsertCustomerAsync(connection, transaction, companyId, branchId, groupRegular, adminId,
        "KH-VL", "Khách vãng lai", null, walkInCode: true);
    await InsertCustomerAsync(connection, transaction, companyId, branchId, groupRegular, adminId,
        "KH000001", "Nguyễn Văn An", "0901000001");
    await InsertCustomerAsync(connection, transaction, companyId, branchId, groupRegular, adminId,
        "KH000002", "Trần Thị Bình", "0901000002");
    await InsertCustomerAsync(connection, transaction, companyId, branchId, groupRegular, adminId,
        "KH000003", "Lê Minh Cường", "0901000003");
    await InsertCustomerAsync(connection, transaction, companyId, branchId, groupRegular, adminId,
        "KH000004", "Phạm Thu Dung", "0901000004");

    // Supplier groups + suppliers
    var grpVang = await InsertIdAsync(connection, transaction, """
        INSERT INTO SUPPLIER_GROUPS (COMPANY_ID, CODE, NAME, DESCRIPTION, IS_ACTIVE, IS_DEFAULT, DISPLAY_ORDER)
        VALUES (@CID, 'VANG', 'Vàng', 'Nhà cung cấp vàng nguyên liệu và vàng thành phẩm', TRUE, TRUE, 1)
        RETURNING ID
        """, ("@CID", companyId));
    var grpDaQuy = await InsertIdAsync(connection, transaction, """
        INSERT INTO SUPPLIER_GROUPS (COMPANY_ID, CODE, NAME, DESCRIPTION, IS_ACTIVE, IS_DEFAULT, DISPLAY_ORDER)
        VALUES (@CID, 'DA_QUY', 'Đá quý', 'Nhà cung cấp kim cương và đá quý các loại', TRUE, FALSE, 2)
        RETURNING ID
        """, ("@CID", companyId));
    _ = await InsertIdAsync(connection, transaction, """
        INSERT INTO SUPPLIER_GROUPS (COMPANY_ID, CODE, NAME, DESCRIPTION, IS_ACTIVE, IS_DEFAULT, DISPLAY_ORDER)
        VALUES (@CID, 'KIM_LOAI', 'Kim loại', 'Nhà cung cấp bạc, bạch kim và hợp kim phụ trợ', TRUE, FALSE, 3)
        RETURNING ID
        """, ("@CID", companyId));
    _ = await InsertIdAsync(connection, transaction, """
        INSERT INTO SUPPLIER_GROUPS (COMPANY_ID, CODE, NAME, DESCRIPTION, IS_ACTIVE, IS_DEFAULT, DISPLAY_ORDER)
        VALUES (@CID, 'KHAC', 'Khác', 'Nhà cung cấp vật tư, phụ kiện và dịch vụ khác', TRUE, FALSE, 4)
        RETURNING ID
        """, ("@CID", companyId));

    await InsertSupplierAsync(connection, transaction, companyId, branchId, grpVang, adminId,
        "NCC000001", "Công ty Vàng SJC Demo", "0902000001", "Hà Nội", "0102000001");
    await InsertSupplierAsync(connection, transaction, companyId, branchId, grpVang, adminId,
        "NCC000002", "Tiệm Vàng Kim Bảo", "0902000002", "TP.HCM", "0302000002");
    await InsertSupplierAsync(connection, transaction, companyId, branchId, grpDaQuy, adminId,
        "NCC000003", "Đá quý An Phú", "0902000003", "Đà Nẵng", "0402000003");

    // Gold prices
    var p585 = await InsertGoldPriceAsync(connection, transaction, companyId, "585", "Vàng 585 (14K)", 0.585m, 7_800_000m, 8_100_000m, 10);
    var p750 = await InsertGoldPriceAsync(connection, transaction, companyId, "750", "Vàng 750 (18K)", 0.750m, 10_200_000m, 10_500_000m, 20);
    var p999 = await InsertGoldPriceAsync(connection, transaction, companyId, "999", "Vàng 999 (24K nữ trang)", 0.999m, 13_800_000m, 14_100_000m, 30);
    var p9999 = await InsertGoldPriceAsync(connection, transaction, companyId, "9999", "Vàng 9999 (24K)", 0.9999m, 14_300_000m, 14_600_000m, 40);

    // Warehouse issue prices (required when posting FG issue / retail sale)
    await ExecuteAsync(connection, transaction, """
        INSERT INTO WAREHOUSE_GOLD_PRICES
            (COMPANY_ID, GOLD_PRICE_ID, GOLD_TYPE_CODE, GOLD_TYPE_NAME, PURITY_RATE,
             ISSUE_PRICE_PER_CHI, IS_ACTIVE, ROW_VERSION, UPDATED_AT)
        SELECT G.COMPANY_ID, G.ID, G.CODE, G.NAME, G.PURITY_RATE,
               G.BUY_PRICE_PER_CHI, TRUE, 0, CURRENT_TIMESTAMP
        FROM GOLD_PRICES G
        WHERE G.COMPANY_ID = @CID
          AND NOT EXISTS (
              SELECT 1 FROM WAREHOUSE_GOLD_PRICES W
              WHERE W.COMPANY_ID = G.COMPANY_ID AND W.GOLD_TYPE_CODE = G.CODE)
        """, ("@CID", companyId));

    // Old-gold materials
    await InsertMaterialAsync(connection, transaction, companyId, "GOLD_585", "Vàng cũ 585", "585", p585);
    await InsertMaterialAsync(connection, transaction, companyId, "GOLD_750", "Vàng cũ 750", "750", p750);
    await InsertMaterialAsync(connection, transaction, companyId, "GOLD_999", "Vàng cũ 999", "999", p999);
    await InsertMaterialAsync(connection, transaction, companyId, "GOLD_9999", "Vàng cũ 9999", "9999", p9999);

    // Finished products (sellable)
    var catalog = new (string Code, string Name, string Purity, long PriceId, decimal Total, decimal Gold, decimal Stone, decimal Labor, decimal StoneAmt)[]
    {
        ("TP-2026-000001", "Nhẫn trơn 9999", "9999", p9999, 1.00m, 1.00m, 0m, 50_000m, 0m),
        ("TP-2026-000002", "Nhẫn trơn 9999", "9999", p9999, 1.20m, 1.20m, 0m, 50_000m, 0m),
        ("TP-2026-000003", "Dây chuyền 9999", "9999", p9999, 2.50m, 2.50m, 0m, 120_000m, 0m),
        ("TP-2026-000004", "Bông tai 750", "750", p750, 1.50m, 1.20m, 0.30m, 180_000m, 200_000m),
        ("TP-2026-000005", "Lắc tay 750", "750", p750, 3.00m, 2.80m, 0.20m, 220_000m, 150_000m),
        ("TP-2026-000006", "Nhẫn đá 750", "750", p750, 2.00m, 1.60m, 0.40m, 250_000m, 500_000m),
        ("TP-2026-000007", "Vòng 999", "999", p999, 4.00m, 4.00m, 0m, 80_000m, 0m),
        ("TP-2026-000008", "Mặt dây 999", "999", p999, 1.80m, 1.80m, 0m, 90_000m, 0m),
        ("TP-2026-000009", "Nhẫn 585", "585", p585, 2.20m, 2.00m, 0.20m, 150_000m, 100_000m),
        ("TP-2026-000010", "Bông 585", "585", p585, 1.10m, 1.00m, 0.10m, 130_000m, 80_000m),
    };

    foreach (var item in catalog)
    {
        await InsertFinishedProductAsync(
            connection, transaction, companyId, branchId, finishedLocId, adminId, item);
    }

    // Cash fund + bank
    await ExecuteAsync(connection, transaction, """
        INSERT INTO CASH_FUNDS (COMPANY_ID, BRANCH_ID, CODE, NAME, IS_ACTIVE, IS_DEFAULT)
        VALUES (@CID, @BID, 'QTM', 'Quỹ tiền mặt chính', TRUE, TRUE)
        """, ("@CID", companyId), ("@BID", branchId));

    await ExecuteAsync(connection, transaction, """
        INSERT INTO BANK_ACCOUNTS (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
        VALUES (@CID, @BID, 'TK_CHINH', 'Tài khoản chính', '0001-DEMO', TRUE)
        """, ("@CID", companyId), ("@BID", branchId));

    // Document sequences soft reset if table exists / has rows already wiped
    _ = rawLocId;
    _ = salesLocId;
}

static Task InsertCustomerAsync(
    FbConnection connection, FbTransaction transaction,
    long companyId, long branchId, long groupId, long userId,
    string code, string name, string? phone, bool walkInCode = false)
{
    // CODE used by sales walk-in detection: KH-VL / WALK_IN / KHACH_LE
    _ = walkInCode;
    return ExecuteAsync(connection, transaction, """
        INSERT INTO CUSTOMERS
            (COMPANY_ID, CREATED_BRANCH_ID, CUSTOMER_GROUP_ID, CODE, CUSTOMER_TYPE,
             NAME, PHONE, IS_ACTIVE, CREATED_BY, UPDATED_BY, UPDATED_AT, JOIN_DATE)
        VALUES
            (@CID, @BID, @GID, @CODE, 'INDIVIDUAL',
             @NAME, @PHONE, TRUE, @UID, @UID, CURRENT_TIMESTAMP, CURRENT_DATE)
        """,
        ("@CID", companyId), ("@BID", branchId), ("@GID", groupId),
        ("@CODE", code), ("@NAME", name),
        ("@PHONE", (object?)phone ?? DBNull.Value), ("@UID", userId));
}

static Task InsertSupplierAsync(
    FbConnection connection, FbTransaction transaction,
    long companyId, long branchId, long groupId, long userId,
    string code, string name, string phone, string region, string taxCode) =>
    ExecuteAsync(connection, transaction, """
        INSERT INTO SUPPLIERS
            (COMPANY_ID, CREATED_BRANCH_ID, SUPPLIER_GROUP_ID, CODE, NAME,
             PHONE, REGION, TAX_CODE, IS_ACTIVE, CREATED_BY, UPDATED_BY, UPDATED_AT)
        VALUES
            (@CID, @BID, @GID, @CODE, @NAME,
             @PHONE, @REGION, @TAX, TRUE, @UID, @UID, CURRENT_TIMESTAMP)
        """,
        ("@CID", companyId), ("@BID", branchId), ("@GID", groupId),
        ("@CODE", code), ("@NAME", name), ("@PHONE", phone),
        ("@REGION", region), ("@TAX", taxCode), ("@UID", userId));

static Task<long> InsertGoldPriceAsync(
    FbConnection connection, FbTransaction transaction, long companyId,
    string code, string name, decimal purity, decimal buy, decimal sell, int order) =>
    InsertIdAsync(connection, transaction, """
        INSERT INTO GOLD_PRICES
            (COMPANY_ID, CODE, NAME, PURITY_RATE, BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
             BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM, CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE)
        VALUES (@CID, @CODE, @NAME, @PURITY, @BUY, @SELL, 0, 0, 'VND', @ORD, TRUE)
        RETURNING ID
        """, ("@CID", companyId), ("@CODE", code), ("@NAME", name),
        ("@PURITY", purity), ("@BUY", buy), ("@SELL", sell), ("@ORD", order));

static Task InsertMaterialAsync(
    FbConnection connection, FbTransaction transaction, long companyId,
    string code, string name, string purity, long oldGoldPriceId) =>
    ExecuteAsync(connection, transaction, """
        INSERT INTO MATERIALS
            (COMPANY_ID, CODE, NAME, MATERIAL_TYPE, PURITY_CODE, UNIT_CODE,
             REFERENCE_COST, IS_ACTIVE, TRACKING_MODE, MIN_STOCK, OLD_GOLD_PRICE_ID)
        VALUES (@CID, @CODE, @NAME, 'GOLD', @PURITY, 'CHI', 0, TRUE, 'WEIGHT', 0, @PRICE)
        """, ("@CID", companyId), ("@CODE", code), ("@NAME", name),
        ("@PURITY", purity), ("@PRICE", oldGoldPriceId));

static Task InsertFinishedProductAsync(
    FbConnection connection, FbTransaction transaction,
    long companyId, long branchId, long locationId, long userId,
    (string Code, string Name, string Purity, long PriceId, decimal Total, decimal Gold, decimal Stone, decimal Labor, decimal StoneAmt) item)
{
    var material = item.Gold * 100_000m; // placeholder cost
    var totalCost = material + item.Labor + item.StoneAmt;
    return ExecuteAsync(connection, transaction, """
        INSERT INTO FINISHED_PRODUCTS
            (COMPANY_ID, BRANCH_ID, CODE, BARCODE_VALUE, QR_VALUE, PRODUCT_NAME,
             GOLD_PURITY, TOTAL_WEIGHT, GOLD_WEIGHT, STONE_WEIGHT, LABOR_COST,
             MATERIAL_COST, NON_GOLD_MATERIAL_COST, TOTAL_COST, LOCATION_ID,
             STATUS_CODE, ROW_VERSION, RETAIL_GOLD_PRICE_ID, CREATED_BY, CREATED_AT)
        VALUES
            (@CID, @BID, @CODE, @CODE, @CODE, @NAME,
             @PURITY, @TOTAL_W, @GOLD_W, @STONE_W, @LABOR,
             @MAT, @STONE_AMT, @TOTAL_COST, @LOC,
             'IN_STOCK', 1, @PRICE, @UID, CURRENT_TIMESTAMP)
        """,
        ("@CID", companyId), ("@BID", branchId), ("@CODE", item.Code), ("@NAME", item.Name),
        ("@PURITY", item.Purity), ("@TOTAL_W", item.Total), ("@GOLD_W", item.Gold), ("@STONE_W", item.Stone),
        ("@LABOR", item.Labor), ("@MAT", material), ("@STONE_AMT", item.StoneAmt), ("@TOTAL_COST", totalCost),
        ("@LOC", locationId), ("@PRICE", item.PriceId), ("@UID", userId));
}

static async Task<long> InsertIdAsync(
    FbConnection connection, FbTransaction transaction, string sql,
    params (string Name, object Value)[] parameters)
{
    await using var cmd = new FbCommand(sql, connection, transaction);
    foreach (var (name, value) in parameters)
    {
        var p = cmd.Parameters.Add(name, MapType(value));
        p.Value = value ?? DBNull.Value;
    }
    return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
}

static async Task ExecuteAsync(
    FbConnection connection, FbTransaction transaction, string sql,
    params (string Name, object Value)[] parameters)
{
    await using var cmd = new FbCommand(sql, connection, transaction);
    foreach (var (name, value) in parameters)
    {
        var p = cmd.Parameters.Add(name, MapType(value));
        p.Value = value ?? DBNull.Value;
    }
    await cmd.ExecuteNonQueryAsync();
}

static async Task<long> ScalarAsync(FbConnection connection, FbTransaction transaction, string sql)
{
    await using var cmd = new FbCommand(sql, connection, transaction);
    var value = await cmd.ExecuteScalarAsync();
    if (value is null or DBNull)
        throw new InvalidOperationException($"Expected scalar for: {sql}");
    return Convert.ToInt64(value, CultureInfo.InvariantCulture);
}

static FbDbType MapType(object? value) => value switch
{
    null or DBNull => FbDbType.VarChar,
    string => FbDbType.VarChar,
    int or long => FbDbType.BigInt,
    decimal => FbDbType.Decimal,
    bool => FbDbType.Boolean,
    DateTime => FbDbType.TimeStamp,
    _ => FbDbType.VarChar
};
