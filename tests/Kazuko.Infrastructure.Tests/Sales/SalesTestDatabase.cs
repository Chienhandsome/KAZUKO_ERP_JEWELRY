using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Tests.Sales;

internal sealed class SalesTestDatabase : IDisposable
{
    private const string PasswordHash = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string PasswordSalt = "00000000000000000000000000000000";

    private readonly ConnectionProfile _originalProfile;
    private readonly string _temporaryDirectory;

    public SalesTestDatabase()
    {
        var source = ResolveVerifiedDatabasePath();
        _originalProfile = FirebirdDatabase.Profile;
        _temporaryDirectory = Path.Combine(
            ResolveWorktreeRoot(),
            ".superpowers",
            "sdd",
            "2026-10-05-sales-checkout",
            "task3-test-dbs",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_temporaryDirectory);

        var databasePath = Path.Combine(_temporaryDirectory, "data.fdb");
        File.Copy(source, databasePath, overwrite: false);
        FirebirdDatabase.ApplyProfileForCurrentProcess(new ConnectionProfile(
            _originalProfile.Host,
            _originalProfile.Port,
            databasePath,
            _originalProfile.UserName,
            string.IsNullOrWhiteSpace(_originalProfile.Password) ? "masterkey" : _originalProfile.Password,
            UseEmbedded: false));

        SeedAsync().GetAwaiter().GetResult();
    }

    public long CompanyId { get; private set; }
    public long OtherCompanyId { get; private set; }
    public long BranchId { get; private set; }
    public long OtherBranchId { get; private set; }
    public long OtherCompanyBranchId { get; private set; }
    public long SalesFloorLocationId { get; private set; }
    public long OldGoldLocationId { get; private set; }
    public long OldGoldMaterialId { get; private set; }
    public long NamedCustomerId { get; private set; }
    public long SalespersonId { get; private set; }
    public long LimitedUserId { get; private set; }
    public long AdminUserId { get; private set; }
    public long MiddleCheckoutId { get; private set; }
    public long OtherBranchCheckoutId { get; private set; }
    public long OtherCompanyCheckoutId { get; private set; }

    public AuthenticatedSession BranchSession(long userId, string claimedRoleCode = "SALE") =>
        new(userId, "task3-user", "Task 3 User", claimedRoleCode, BranchId, "T3_BRA", "Task 3 Branch A");

    public void Dispose()
    {
        FirebirdDatabase.ApplyProfileForCurrentProcess(_originalProfile);
        FbConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_temporaryDirectory))
                Directory.Delete(_temporaryDirectory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup after all Firebird handles have been disposed.
        }
    }

    private async Task SeedAsync()
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync();

        try
        {
            await AssertSchemaVersionAsync(connection, transaction);

            CompanyId = await ScalarAsync(connection, transaction,
                "SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'");
            OtherCompanyId = await InsertIdAsync(connection, transaction, """
                INSERT INTO COMPANIES (CODE, NAME, IS_ACTIVE)
                VALUES ('T3_CO2', 'Task 3 Company 2', TRUE)
                RETURNING ID
                """);

            BranchId = await InsertIdAsync(connection, transaction, """
                INSERT INTO BRANCHES (COMPANY_ID, CODE, NAME, IS_ACTIVE)
                VALUES (@CID, 'T3_BRA', 'Task 3 Branch A', TRUE)
                RETURNING ID
                """, ("@CID", CompanyId));
            OtherBranchId = await InsertIdAsync(connection, transaction, """
                INSERT INTO BRANCHES (COMPANY_ID, CODE, NAME, IS_ACTIVE)
                VALUES (@CID, 'T3_BRB', 'Task 3 Branch B', TRUE)
                RETURNING ID
                """, ("@CID", CompanyId));
            OtherCompanyBranchId = await InsertIdAsync(connection, transaction, """
                INSERT INTO BRANCHES (COMPANY_ID, CODE, NAME, IS_ACTIVE)
                VALUES (@CID, 'T3_BRC', 'Task 3 Branch C', TRUE)
                RETURNING ID
                """, ("@CID", OtherCompanyId));

            var limitedRoleId = await InsertIdAsync(connection, transaction, """
                INSERT INTO ROLES (CODE, NAME, IS_SYSTEM, IS_ACTIVE)
                VALUES ('T3_LIMITED', 'Task 3 Limited', FALSE, TRUE)
                RETURNING ID
                """);
            var adminRoleId = await ScalarAsync(connection, transaction,
                "SELECT ID FROM ROLES WHERE CODE = 'ADMIN'");

            SalespersonId = await InsertUserAsync(connection, transaction, CompanyId, BranchId,
                limitedRoleId, "t3-salesperson", "Task 3 Salesperson", true);
            LimitedUserId = await InsertUserAsync(connection, transaction, CompanyId, BranchId,
                limitedRoleId, "t3-limited", "Task 3 Limited User", true);
            AdminUserId = await InsertUserAsync(connection, transaction, CompanyId, BranchId,
                adminRoleId, "t3-admin", "Task 3 Admin User", true);
            _ = await InsertUserAsync(connection, transaction, CompanyId, OtherBranchId,
                limitedRoleId, "t3-other-branch", "Task 3 Other Branch User", true);
            _ = await InsertUserAsync(connection, transaction, OtherCompanyId, OtherCompanyBranchId,
                limitedRoleId, "t3-other-company", "Task 3 Other Company User", true);
            _ = await InsertUserAsync(connection, transaction, CompanyId, BranchId,
                limitedRoleId, "t3-inactive", "Task 3 Inactive User", false);

            await ExecuteAsync(connection, transaction, """
                INSERT INTO ROLE_PERMISSIONS (ROLE_ID, PERMISSION_ID, IS_ALLOWED)
                SELECT @RID, P.ID,
                       CASE WHEN P.CODE IN ('sales.checkout.post', 'sales.history.read')
                            THEN TRUE ELSE FALSE END
                FROM PERMISSIONS P
                WHERE P.CODE IN (
                    'sales.checkout.post', 'sales.price.override', 'sales.history.read')
                """, ("@RID", limitedRoleId));

            var groupId = await InsertCustomerGroupAsync(connection, transaction, CompanyId, "T3_GROUP_A");
            var otherGroupId = await InsertCustomerGroupAsync(connection, transaction, OtherCompanyId, "T3_GROUP_B");
            NamedCustomerId = await InsertCustomerAsync(connection, transaction, CompanyId, BranchId,
                groupId, "T3_NAMED", "Task 3 Named Customer", "0903000001", true);
            _ = await InsertCustomerAsync(connection, transaction, CompanyId, BranchId,
                groupId, "WALK_IN", "Task 3 Walk-in Customer", "0903000004", true);
            _ = await InsertCustomerAsync(connection, transaction, CompanyId, BranchId,
                groupId, "T3_INACTIVE", "Task 3 Inactive Customer", "0903000002", false);
            _ = await InsertCustomerAsync(connection, transaction, OtherCompanyId, OtherCompanyBranchId,
                otherGroupId, "T3_OTHER_CO", "Task 3 Other Company Customer", "0903000003", true);
            for (var index = 0; index < 55; index++)
            {
                _ = await InsertCustomerAsync(connection, transaction, CompanyId, BranchId,
                    groupId, $"T3_LIMIT_{index:000}", $"Task 3 Limited {index:000}", $"0913{index:000000}", true);
            }

            SalesFloorLocationId = await InsertLocationAsync(connection, transaction, BranchId,
                "T3_SALES_A", "Task 3 Sales Floor A", "SALES_FLOOR", true);
            OldGoldLocationId = await InsertLocationAsync(connection, transaction, BranchId,
                "T3_OLD_GOLD_DEFAULT", "Task 3 Old Gold", "RAW_MATERIAL", true);
            var nonSalesLocationId = await InsertLocationAsync(connection, transaction, BranchId,
                "T3_RAW_A", "Task 3 Raw A", "RAW_MATERIAL", true);
            _ = await InsertLocationAsync(connection, transaction, BranchId,
                "T3_INACTIVE_LOC", "Task 3 Inactive Location", "RAW_MATERIAL", false);
            var otherBranchSalesFloor = await InsertLocationAsync(connection, transaction, OtherBranchId,
                "T3_SALES_B", "Task 3 Sales Floor B", "SALES_FLOOR", true);
            var otherCompanySalesFloor = await InsertLocationAsync(connection, transaction, OtherCompanyBranchId,
                "T3_SALES_C", "Task 3 Sales Floor C", "SALES_FLOOR", true);

            OldGoldMaterialId = await InsertMaterialAsync(connection, transaction, CompanyId,
                "T3_GOLD_750", "Task 3 Gold 750", "T3_750", true);
            _ = await InsertMaterialAsync(connection, transaction, CompanyId,
                "T3_GOLD_INACTIVE", "Task 3 Inactive Gold", "T3_INACTIVE", false);
            _ = await InsertMaterialAsync(connection, transaction, OtherCompanyId,
                "T3_GOLD_750", "Task 3 Other Gold 750", "T3_750", true);
            await InsertGoldPriceAsync(connection, transaction, CompanyId, "T3_750", "Task 3 Price 750", 0.75m, 77m, 111m, true);
            await InsertGoldPriceAsync(connection, transaction, OtherCompanyId, "T3_750", "Task 3 Other Price 750", 0.75m, 88m, 222m, true);

            var displayedProductId = await InsertProductAsync(connection, transaction, CompanyId, BranchId,
                SalesFloorLocationId, "T3_PRODUCT_DISPLAYED", "Task 3 Displayed", "DISPLAYED", 9);
            _ = await InsertProductAsync(connection, transaction, CompanyId, BranchId,
                SalesFloorLocationId, "T3_PRODUCT_INSTOCK", "Task 3 In Stock", "IN_STOCK", 10);
            _ = await InsertProductAsync(connection, transaction, CompanyId, BranchId,
                SalesFloorLocationId, "T3_PRODUCT_18K", "Task 3 18K", "DISPLAYED", 15, "18K");
            _ = await InsertProductAsync(connection, transaction, CompanyId, BranchId,
                SalesFloorLocationId, "T3_PRODUCT_SOLD", "Task 3 Sold", "SOLD", 11);
            _ = await InsertProductAsync(connection, transaction, CompanyId, BranchId,
                nonSalesLocationId, "T3_PRODUCT_RAW", "Task 3 Raw", "IN_STOCK", 12);
            _ = await InsertProductAsync(connection, transaction, CompanyId, OtherBranchId,
                otherBranchSalesFloor, "T3_PRODUCT_BRANCH_B", "Task 3 Branch B Product", "DISPLAYED", 13);
            _ = await InsertProductAsync(connection, transaction, OtherCompanyId, OtherCompanyBranchId,
                otherCompanySalesFloor, "T3_PRODUCT_COMPANY_B", "Task 3 Company B Product", "DISPLAYED", 14);

            var salesOrderId = await InsertIdAsync(connection, transaction, """
                INSERT INTO SALES_ORDERS
                    (COMPANY_ID, BRANCH_ID, CODE, ORDER_TYPE, CUSTOMER_ID, ORDER_DATE,
                     SALES_PERSON_ID, STATUS_CODE, SUBTOTAL, DISCOUNT_AMOUNT, TAX_AMOUNT,
                     GRAND_TOTAL, CREATED_BY, UPDATED_BY)
                VALUES
                    (@CID, @BID, 'T3-SO-001', 'RETAIL', @CUSTOMER, DATE '2026-10-02',
                     @SALESPERSON, 'COMPLETED', 353, 3, 0, 350, @CREATED_BY, @CREATED_BY)
                RETURNING ID
                """,
                ("@CID", CompanyId), ("@BID", BranchId), ("@CUSTOMER", NamedCustomerId),
                ("@SALESPERSON", SalespersonId), ("@CREATED_BY", LimitedUserId));
            await ExecuteAsync(connection, transaction, """
                INSERT INTO SALES_ORDER_ITEMS
                    (ORDER_ID, LINE_NUMBER, PRODUCT_NAME, GOLD_PURITY, QUANTITY, ESTIMATED_WEIGHT,
                     GOLD_UNIT_PRICE, LABOR_AMOUNT, STONE_AMOUNT, LINE_SUBTOTAL, LINE_TOTAL,
                     FINISHED_PRODUCT_ID, SOURCE_GOLD_PRICE_PER_CHI, APPLIED_GOLD_PRICE_PER_CHI,
                     SOURCE_LABOR_AMOUNT_VND, APPLIED_LABOR_AMOUNT_VND,
                     SOURCE_STONE_AMOUNT_VND, APPLIED_STONE_AMOUNT_VND,
                     IS_PRICE_OVERRIDE, PRICE_OVERRIDE_REASON)
                VALUES
                    (@ORDER_ID, 1, 'Task 3 Displayed', 'T3_750', 1, 2.5,
                     100, 20, 10, 280, 280,
                     @PRODUCT_ID, 111, 100, 25, 20, 8, 10, TRUE, 'Task 3 override')
                """, ("@ORDER_ID", salesOrderId), ("@PRODUCT_ID", displayedProductId));

            _ = await InsertCheckoutAsync(connection, transaction, CompanyId, BranchId,
                "T3-CHK-001", new DateTime(2026, 10, 1), NamedCustomerId, SalespersonId,
                SalesFloorLocationId, LimitedUserId, null);
            MiddleCheckoutId = await InsertCheckoutAsync(connection, transaction, CompanyId, BranchId,
                "T3-CHK-002", new DateTime(2026, 10, 2), NamedCustomerId, SalespersonId,
                SalesFloorLocationId, LimitedUserId, salesOrderId);
            _ = await InsertCheckoutAsync(connection, transaction, CompanyId, BranchId,
                "T3-CHK-003", new DateTime(2026, 10, 3), NamedCustomerId, SalespersonId,
                SalesFloorLocationId, LimitedUserId, null);
            OtherBranchCheckoutId = await InsertCheckoutAsync(connection, transaction, CompanyId, OtherBranchId,
                "T3-CHK-B01", new DateTime(2026, 10, 4), NamedCustomerId, SalespersonId,
                otherBranchSalesFloor, LimitedUserId, null);

            var otherCustomerId = await ScalarAsync(connection, transaction,
                "SELECT ID FROM CUSTOMERS WHERE COMPANY_ID = @CID AND CODE = 'T3_OTHER_CO'",
                ("@CID", OtherCompanyId));
            var otherUserId = await ScalarAsync(connection, transaction,
                "SELECT ID FROM USERS WHERE USERNAME = 't3-other-company'");
            OtherCompanyCheckoutId = await InsertCheckoutAsync(connection, transaction, OtherCompanyId,
                OtherCompanyBranchId, "T3-CHK-C01", new DateTime(2026, 10, 5), otherCustomerId,
                otherUserId, otherCompanySalesFloor, otherUserId, null);

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static async Task AssertSchemaVersionAsync(FbConnection connection, FbTransaction transaction)
    {
        var version = await ScalarAsync(connection, transaction,
            "SELECT MAX(VERSION_NO) FROM SCHEMA_MIGRATIONS");
        if (version != 51)
            throw new InvalidOperationException($"Task 3 requires schema version 51, found {version}.");
    }

    private static async Task<long> InsertUserAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long roleId,
        string username,
        string displayName,
        bool active) =>
        await InsertIdAsync(connection, transaction, """
            INSERT INTO USERS
                (USERNAME, PASSWORD_HASH, PASSWORD_SALT, PASSWORD_ITERATIONS, DISPLAY_NAME,
                 ROLE_CODE, BRANCH_CODE, BRANCH_NAME, IS_ACTIVE, COMPANY_ID, ROLE_ID, DEFAULT_BRANCH_ID)
            SELECT @USERNAME, @HASH, @SALT, 100000, @DISPLAY_NAME,
                   R.CODE, B.CODE, B.NAME, @ACTIVE, @CID, @RID, @BID
            FROM ROLES R
            JOIN BRANCHES B ON B.ID = @BID
            WHERE R.ID = @RID
            RETURNING ID
            """,
            ("@USERNAME", username), ("@HASH", PasswordHash), ("@SALT", PasswordSalt),
            ("@DISPLAY_NAME", displayName), ("@ACTIVE", active), ("@CID", companyId),
            ("@RID", roleId), ("@BID", branchId));

    private static Task<long> InsertCustomerGroupAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string code) =>
        InsertIdAsync(connection, transaction, """
            INSERT INTO CUSTOMER_GROUPS
                (COMPANY_ID, CODE, NAME, MIN_POINTS, DISCOUNT_RATE, IS_DEFAULT, IS_ACTIVE)
            VALUES (@CID, @CODE, @CODE, 0, 0, FALSE, TRUE)
            RETURNING ID
            """, ("@CID", companyId), ("@CODE", code));

    private static Task<long> InsertCustomerAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long groupId,
        string code,
        string name,
        string phone,
        bool active) =>
        InsertIdAsync(connection, transaction, """
            INSERT INTO CUSTOMERS
                (COMPANY_ID, CREATED_BRANCH_ID, CUSTOMER_GROUP_ID, CODE, CUSTOMER_TYPE,
                 NAME, PHONE, OPENING_POINTS, IS_ACTIVE)
            VALUES (@CID, @BID, @GID, @CODE, 'INDIVIDUAL', @NAME, @PHONE, 0, @ACTIVE)
            RETURNING ID
            """, ("@CID", companyId), ("@BID", branchId), ("@GID", groupId),
            ("@CODE", code), ("@NAME", name), ("@PHONE", phone), ("@ACTIVE", active));

    private static Task<long> InsertLocationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long branchId,
        string code,
        string name,
        string scope,
        bool active) =>
        InsertIdAsync(connection, transaction, """
            INSERT INTO LOCATIONS (BRANCH_ID, CODE, NAME, LOCATION_TYPE, INVENTORY_SCOPE, IS_ACTIVE)
            VALUES (@BID, @CODE, @NAME, 'WAREHOUSE', @SCOPE, @ACTIVE)
            RETURNING ID
            """, ("@BID", branchId), ("@CODE", code), ("@NAME", name),
            ("@SCOPE", scope), ("@ACTIVE", active));

    private static Task<long> InsertMaterialAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string code,
        string name,
        string purity,
        bool active) =>
        InsertIdAsync(connection, transaction, """
            INSERT INTO MATERIALS
                (COMPANY_ID, CODE, NAME, MATERIAL_TYPE, PURITY_CODE, UNIT_CODE,
                 REFERENCE_COST, IS_ACTIVE, TRACKING_MODE, MIN_STOCK)
            VALUES (@CID, @CODE, @NAME, 'GOLD', @PURITY, 'CHI', 0, @ACTIVE, 'WEIGHT', 0)
            RETURNING ID
            """, ("@CID", companyId), ("@CODE", code), ("@NAME", name),
            ("@PURITY", purity), ("@ACTIVE", active));

    private static Task InsertGoldPriceAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string code,
        string name,
        decimal purity,
        decimal buy,
        decimal sell,
        bool active) =>
        ExecuteAsync(connection, transaction, """
            INSERT INTO GOLD_PRICES
                (COMPANY_ID, CODE, NAME, PURITY_RATE, BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
                 BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM, CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE)
            VALUES (@CID, @CODE, @NAME, @PURITY, @BUY, @SELL, 0, 0, 'VND', 900, @ACTIVE)
            """, ("@CID", companyId), ("@CODE", code), ("@NAME", name),
            ("@PURITY", purity), ("@BUY", buy), ("@SELL", sell), ("@ACTIVE", active));

    private static Task<long> InsertProductAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long locationId,
        string code,
        string name,
        string status,
        long rowVersion,
        string purity = "T3_750") =>
        InsertIdAsync(connection, transaction, """
            INSERT INTO FINISHED_PRODUCTS
                (COMPANY_ID, BRANCH_ID, CODE, BARCODE_VALUE, QR_VALUE, PRODUCT_NAME,
                 GOLD_PURITY, TOTAL_WEIGHT, GOLD_WEIGHT, STONE_WEIGHT, LABOR_COST,
                 MATERIAL_COST, NON_GOLD_MATERIAL_COST, TOTAL_COST, LOCATION_ID,
                 STATUS_CODE, ROW_VERSION)
            VALUES
                (@CID, @BID, @CODE, @BARCODE, @QR, @NAME,
                 @PURITY, 3.25, 2.50, 0.75, 25,
                 277.50, 8, 310.50, @LOCATION, @STATUS, @ROW_VERSION)
            RETURNING ID
            """, ("@CID", companyId), ("@BID", branchId), ("@CODE", code),
            ("@BARCODE", $"BAR-{code}"), ("@QR", $"QR-{code}"), ("@NAME", name),
            ("@PURITY", purity), ("@LOCATION", locationId), ("@STATUS", status),
            ("@ROW_VERSION", rowVersion));

    private static Task<long> InsertCheckoutAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        string code,
        DateTime documentDate,
        long customerId,
        long salespersonId,
        long sourceLocationId,
        long createdBy,
        long? salesOrderId) =>
        InsertIdAsync(connection, transaction, """
            INSERT INTO SALES_CHECKOUTS
                (COMPANY_ID, BRANCH_ID, CODE, CHECKOUT_TYPE, DOCUMENT_DATE, CUSTOMER_ID,
                 SALESPERSON_ID, SOURCE_LOCATION_ID, SALE_GROSS_VND, OLD_GOLD_TOTAL_VND,
                 DISCOUNT_AMOUNT_VND, NET_AMOUNT_VND, SALES_ORDER_ID, CLIENT_REQUEST_ID,
                 REQUEST_HASH, STATUS_CODE, NOTES, CREATED_BY)
            VALUES
                (@CID, @BID, @CODE, 'SALE', @DOCUMENT_DATE, @CUSTOMER,
                 @SALESPERSON, @LOCATION, 353, 0, 3, 350, @ORDER_ID, @REQUEST_ID,
                 @HASH, 'POSTED', 'Task 3 checkout', @CREATED_BY)
            RETURNING ID
            """, ("@CID", companyId), ("@BID", branchId), ("@CODE", code),
            ("@DOCUMENT_DATE", documentDate), ("@CUSTOMER", customerId),
            ("@SALESPERSON", salespersonId), ("@LOCATION", sourceLocationId),
            ("@ORDER_ID", salesOrderId), ("@REQUEST_ID", Guid.NewGuid().ToString()),
            ("@HASH", new string('A', 64)), ("@CREATED_BY", createdBy));

    private static async Task<long> ScalarAsync(
        FbConnection connection,
        FbTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static Task<long> InsertIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters) =>
        ScalarAsync(connection, transaction, sql, parameters);

    private static async Task ExecuteAsync(
        FbConnection connection,
        FbTransaction transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = CreateCommand(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static FbCommand CreateCommand(
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

    private static string ResolveVerifiedDatabasePath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "data", "backups", "sales_checkout_verify.fdb");
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "Task 3 integration tests require the schema-v51 verified database copy at data/backups/sales_checkout_verify.fdb.");
    }

    private static string ResolveWorktreeRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "KAZUKO_ERP_JEWELRY.sln")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Cannot locate the sales-checkout worktree root.");
    }
}
