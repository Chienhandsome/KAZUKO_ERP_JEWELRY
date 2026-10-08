using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Sales;

public sealed class FirebirdSalesQueryRepository : ISalesQueryRepository
{
    private const int CustomerLimit = 50;
    private const int LookupLimit = 100;
    private const int ProductSearchLimit = 20;

    /// <summary>
    /// Thành phẩm bán được: tại kho bán hàng hoặc kho thành phẩm, còn tồn/trưng bày,
    /// và có giá bán lẻ (RETAIL_GOLD_PRICE_ID hoặc khớp GOLD_PRICES.CODE = GOLD_PURITY).
    /// </summary>
    private const string SellableProductFrom = """
        FROM FINISHED_PRODUCTS F
        JOIN LOCATIONS L ON L.ID = F.LOCATION_ID
                        AND L.BRANCH_ID = @BRANCH_ID
                        AND L.INVENTORY_SCOPE IN ('SALES_FLOOR', 'FINISHED_GOODS')
                        AND L.IS_ACTIVE = TRUE
        JOIN GOLD_PRICES GP ON GP.COMPANY_ID = F.COMPANY_ID
                           AND GP.IS_ACTIVE = TRUE
                           AND GP.ID = COALESCE(
                                F.RETAIL_GOLD_PRICE_ID,
                                (SELECT MIN(P.ID)
                                 FROM GOLD_PRICES P
                                 WHERE P.COMPANY_ID = F.COMPANY_ID
                                   AND P.IS_ACTIVE = TRUE
                                   AND UPPER(TRIM(P.CODE)) = UPPER(TRIM(COALESCE(F.GOLD_PURITY, '')))))
        """;

    public async Task<IReadOnlyList<SalesCustomerOption>> SearchCustomersAsync(
        string searchText,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var search = (searchText ?? string.Empty).Trim();
        var result = new List<SalesCustomerOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return result;

        await using var command = new FbCommand($"""
            SELECT C.ID, C.CODE, C.NAME, C.PHONE,
                   CASE WHEN UPPER(TRIM(C.CODE)) IN ('KH-VL', 'WALK_IN', 'KHACH_LE')
                        THEN TRUE ELSE FALSE END
            FROM CUSTOMERS C
            WHERE C.COMPANY_ID = @COMPANY_ID
              AND C.IS_ACTIVE = TRUE
              AND (@HAS_SEARCH = 0
                   OR CAST(C.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR C.NAME CONTAINING @SEARCH
                   OR COALESCE(C.PHONE, '') CONTAINING @SEARCH)
            ORDER BY C.NAME, C.CODE, C.ID
            ROWS {CustomerLimit}
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length == 0 ? 0 : 1;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SalesCustomerOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetBoolean(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<SalespersonOption>> GetSalespeopleAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = new List<SalespersonOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return result;

        await using var command = new FbCommand($"""
            SELECT U.ID, U.USERNAME, U.DISPLAY_NAME
            FROM USERS U
            JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
            WHERE U.COMPANY_ID = @COMPANY_ID
              AND U.IS_ACTIVE = TRUE
              AND (U.DEFAULT_BRANCH_ID = @BRANCH_ID
                   OR EXISTS (
                       SELECT 1
                       FROM USER_SCOPES S
                       WHERE S.USER_ID = U.ID
                         AND S.SCOPE_TYPE = 'BRANCH'
                         AND S.SCOPE_ID = @BRANCH_ID
                         AND S.CAN_ACCESS = TRUE))
            ORDER BY U.DISPLAY_NAME, U.USERNAME, U.ID
            ROWS {LookupLimit}
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SalespersonOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return result;
    }

    public async Task<SalesProductOption?> FindAvailableProductAsync(
        string productCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var code = (productCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return null;

        await using var command = new FbCommand($"""
            SELECT FIRST 1
                   F.ID, F.CODE, F.PRODUCT_NAME,
                   GP.ID, GP.NAME, L.ID, COALESCE(F.GOLD_PURITY, ''), F.ROW_VERSION,
                   F.TOTAL_WEIGHT, F.STONE_WEIGHT, F.GOLD_WEIGHT,
                   GP.SELL_PRICE_PER_CHI, F.LABOR_COST, F.NON_GOLD_MATERIAL_COST
            {SellableProductFrom}
            WHERE F.COMPANY_ID = @COMPANY_ID
              AND F.BRANCH_ID = @BRANCH_ID
              AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
              AND (UPPER(TRIM(F.CODE)) = @CODE
                   OR UPPER(TRIM(COALESCE(F.BARCODE_VALUE, ''))) = @CODE
                   OR UPPER(TRIM(COALESCE(F.QR_VALUE, ''))) = @CODE)
            ORDER BY F.ID
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@CODE", FbDbType.VarChar, 500).Value = code;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadProductOption(reader);
    }

    public async Task<IReadOnlyList<SalesProductOption>> SearchAvailableProductsAsync(
        string searchText,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var search = (searchText ?? string.Empty).Trim();
        var result = new List<SalesProductOption>();
        if (search.Length == 0)
            return result;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return result;

        await using var command = new FbCommand($"""
            SELECT FIRST {ProductSearchLimit}
                   F.ID, F.CODE, F.PRODUCT_NAME,
                   GP.ID, GP.NAME, L.ID, COALESCE(F.GOLD_PURITY, ''), F.ROW_VERSION,
                   F.TOTAL_WEIGHT, F.STONE_WEIGHT, F.GOLD_WEIGHT,
                   GP.SELL_PRICE_PER_CHI, F.LABOR_COST, F.NON_GOLD_MATERIAL_COST
            {SellableProductFrom}
            WHERE F.COMPANY_ID = @COMPANY_ID
              AND F.BRANCH_ID = @BRANCH_ID
              AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
              AND (F.CODE CONTAINING @SEARCH
                   OR F.PRODUCT_NAME CONTAINING @SEARCH)
            ORDER BY F.CODE, F.ID
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 500).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadProductOption(reader));

        return result;
    }

    public async Task<int> CountMatchingFinishedProductsAsync(
        string searchText,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var search = (searchText ?? string.Empty).Trim();
        if (search.Length == 0)
            return 0;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return 0;

        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM FINISHED_PRODUCTS F
            WHERE F.COMPANY_ID = @COMPANY_ID
              AND F.BRANCH_ID = @BRANCH_ID
              AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
              AND (F.CODE CONTAINING @SEARCH
                   OR F.PRODUCT_NAME CONTAINING @SEARCH)
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 500).Value = search;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static SalesProductOption ReadProductOption(System.Data.Common.DbDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetInt64(7),
            reader.GetDecimal(8),
            reader.GetDecimal(9),
            reader.GetDecimal(10),
            reader.GetDecimal(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13));

    public async Task<IReadOnlyList<OldGoldMaterialOption>> GetOldGoldMaterialsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = new List<OldGoldMaterialOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return result;

        await using var command = new FbCommand($"""
            SELECT M.ID, M.CODE, M.NAME, GP.ID,
                   GP.PURITY_RATE * 100, GP.BUY_PRICE_PER_CHI
            FROM MATERIALS M
            JOIN GOLD_PRICES GP ON GP.ID = M.OLD_GOLD_PRICE_ID
                               AND GP.COMPANY_ID = M.COMPANY_ID
                               AND GP.IS_ACTIVE = TRUE
            WHERE M.COMPANY_ID = @COMPANY_ID
              AND M.MATERIAL_TYPE = 'GOLD'
              AND M.IS_ACTIVE = TRUE
            ORDER BY M.NAME, M.CODE, M.ID
            ROWS {LookupLimit}
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OldGoldMaterialOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5)));
        }

        return result;
    }

    public async Task<IReadOnlyList<SalesLocationOption>> GetOldGoldLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = new List<SalesLocationOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return result;

        await using var command = new FbCommand($"""
            SELECT L.ID, L.CODE, L.NAME,
                   CASE WHEN UPPER(L.CODE) CONTAINING 'OLD_GOLD'
                              OR UPPER(L.CODE) CONTAINING 'THU_HOI'
                        THEN TRUE ELSE FALSE END
            FROM LOCATIONS L
            JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @COMPANY_ID
            WHERE L.BRANCH_ID = @BRANCH_ID
              AND L.IS_ACTIVE = TRUE
              AND L.INVENTORY_SCOPE = 'RAW_MATERIAL'
            ORDER BY 4 DESC, L.NAME, L.CODE, L.ID
            ROWS {LookupLimit}
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SalesLocationOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return result;
    }

    public async Task<SalesCheckoutDetail?> GetCheckoutAsync(
        long checkoutId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (checkoutId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return null;

        CheckoutHeader? header;
        await using (var command = new FbCommand("""
            SELECT SC.ID, SC.CODE, SC.CHECKOUT_TYPE, SC.DOCUMENT_DATE,
                   SC.CUSTOMER_ID, C.NAME, C.PHONE,
                   SC.SALESPERSON_ID, SP.DISPLAY_NAME,
                   SC.CREATED_BY, CB.DISPLAY_NAME,
                   SC.SOURCE_LOCATION_ID, SC.OLD_GOLD_DESTINATION_LOCATION_ID,
                   SC.SALE_GROSS_VND, SC.OLD_GOLD_TOTAL_VND,
                   SC.DISCOUNT_AMOUNT_VND, SC.NET_AMOUNT_VND,
                   COALESCE((SELECT SCO.AMOUNT_VND FROM SALES_CHECKOUT_OFFSETS SCO
                             WHERE SCO.CHECKOUT_ID = SC.ID), 0),
                   COALESCE(RD.ORIGINAL_AMOUNT_VND, 0),
                   SC.NOTES, SC.SALES_ORDER_ID, SC.FINANCIAL_SETTLEMENT_ID
            FROM SALES_CHECKOUTS SC
            JOIN CUSTOMERS C ON C.ID = SC.CUSTOMER_ID AND C.COMPANY_ID = SC.COMPANY_ID
            JOIN USERS SP ON SP.ID = SC.SALESPERSON_ID
            JOIN USERS CB ON CB.ID = SC.CREATED_BY
            LEFT JOIN RECEIVABLE_DEBTS RD ON RD.ID = SC.RECEIVABLE_DEBT_ID
            WHERE SC.ID = @ID
              AND SC.COMPANY_ID = @COMPANY_ID
              AND SC.BRANCH_ID = @BRANCH_ID
              AND SC.STATUS_CODE = 'POSTED'
            """, connection))
        {
            command.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
            command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
            command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            header = new CheckoutHeader(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt64(7),
                reader.GetString(8),
                reader.GetInt64(9),
                reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                reader.IsDBNull(12) ? null : reader.GetInt64(12),
                reader.GetDecimal(13),
                reader.GetDecimal(14),
                reader.GetDecimal(15),
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                reader.GetDecimal(18),
                reader.IsDBNull(19) ? null : reader.GetString(19),
                reader.IsDBNull(20) ? null : reader.GetInt64(20),
                reader.IsDBNull(21) ? null : reader.GetInt64(21));
        }

        var saleLines = header.SalesOrderId is > 0
            ? await LoadSaleLinesAsync(connection, header.SalesOrderId.Value, cancellationToken)
            : Array.Empty<SalesLineSnapshot>();
        var oldGoldLines = await LoadOldGoldLinesAsync(connection, header.Id, cancellationToken);
        var allocations = header.FinancialSettlementId is > 0
            ? await LoadAllocationsAsync(connection, header.FinancialSettlementId.Value, cancellationToken)
            : Array.Empty<PaymentAllocationDraft>();

        var receipt = header.NetAmountVnd > 0 ? header.NetAmountVnd - header.ReceivableAmountVnd : 0;
        var payout = header.NetAmountVnd < 0 ? -header.NetAmountVnd : 0;
        var totals = new SalesCheckoutTotals(
            header.SaleGrossVnd,
            header.DiscountAmountVnd,
            header.SaleGrossVnd - header.DiscountAmountVnd,
            header.OldGoldTotalVnd,
            header.OffsetAmountVnd,
            header.NetAmountVnd,
            receipt,
            payout,
            header.ReceivableAmountVnd);

        return new SalesCheckoutDetail(
            header.Id,
            header.Code,
            header.CheckoutType,
            header.DocumentDate,
            header.CustomerId,
            header.CustomerName,
            header.CustomerPhone,
            header.SalespersonId,
            header.SalespersonName,
            header.CreatedBy,
            header.CreatedByDisplayName,
            header.SourceLocationId,
            header.OldGoldDestinationLocationId,
            saleLines,
            oldGoldLines,
            allocations,
            totals,
            header.Notes);
    }

    public async Task<IReadOnlyList<SalesCheckoutListItem>> SearchPostedCheckoutsAsync(
        SalesCheckoutSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);

        var result = new List<SalesCheckoutListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return result;

        var limit = filter.Limit <= 0 ? 100 : Math.Min(filter.Limit, 500);
        var search = (filter.SearchText ?? string.Empty).Trim();
        var pattern = search.Length > 0 ? "%" + search.ToUpperInvariant() + "%" : null;

        await using var command = new FbCommand($"""
            SELECT FIRST {limit}
                   SC.ID, SC.CODE, SC.CHECKOUT_TYPE, SC.DOCUMENT_DATE,
                   C.NAME, C.PHONE, SP.DISPLAY_NAME, SC.NET_AMOUNT_VND
            FROM SALES_CHECKOUTS SC
            JOIN CUSTOMERS C ON C.ID = SC.CUSTOMER_ID AND C.COMPANY_ID = SC.COMPANY_ID
            JOIN USERS SP ON SP.ID = SC.SALESPERSON_ID
            WHERE SC.COMPANY_ID = @COMPANY_ID
              AND SC.BRANCH_ID = @BRANCH_ID
              AND SC.STATUS_CODE = 'POSTED'
              AND (@FROM_DATE IS NULL OR SC.DOCUMENT_DATE >= @FROM_DATE)
              AND (@TO_DATE_EXCLUSIVE IS NULL OR SC.DOCUMENT_DATE < @TO_DATE_EXCLUSIVE)
              AND (
                    @PATTERN IS NULL
                 OR UPPER(SC.CODE) LIKE @PATTERN
                 OR UPPER(C.NAME) LIKE @PATTERN
                 OR UPPER(COALESCE(C.PHONE, '')) LIKE @PATTERN
              )
            ORDER BY SC.DOCUMENT_DATE DESC, SC.ID DESC
            """, connection);
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value =
            filter.FromDate is null ? DBNull.Value : filter.FromDate.Value.Date;
        command.Parameters.Add("@TO_DATE_EXCLUSIVE", FbDbType.Date).Value =
            filter.ToDate is null ? DBNull.Value : filter.ToDate.Value.Date.AddDays(1);
        command.Parameters.Add("@PATTERN", FbDbType.VarChar, 200).Value =
            (object?)pattern ?? DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SalesCheckoutListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDateTime(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7)));
        }

        return result;
    }

    public async Task<long?> GetAdjacentCheckoutIdAsync(
        long checkoutId,
        bool next,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (checkoutId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return null;

        DateTime documentDate;
        await using (var current = new FbCommand("""
            SELECT DOCUMENT_DATE
            FROM SALES_CHECKOUTS
            WHERE ID = @ID
              AND COMPANY_ID = @COMPANY_ID
              AND BRANCH_ID = @BRANCH_ID
              AND STATUS_CODE = 'POSTED'
            """, connection))
        {
            current.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
            current.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
            current.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
            var value = await current.ExecuteScalarAsync(cancellationToken);
            if (value is null or DBNull)
                return null;
            documentDate = Convert.ToDateTime(value);
        }

        var comparison = next
            ? "(DOCUMENT_DATE > @DOCUMENT_DATE OR (DOCUMENT_DATE = @DOCUMENT_DATE AND ID > @ID))"
            : "(DOCUMENT_DATE < @DOCUMENT_DATE OR (DOCUMENT_DATE = @DOCUMENT_DATE AND ID < @ID))";
        var order = next ? "DOCUMENT_DATE, ID" : "DOCUMENT_DATE DESC, ID DESC";
        await using var command = new FbCommand($"""
            SELECT FIRST 1 ID
            FROM SALES_CHECKOUTS
            WHERE COMPANY_ID = @COMPANY_ID
              AND BRANCH_ID = @BRANCH_ID
              AND STATUS_CODE = 'POSTED'
              AND {comparison}
            ORDER BY {order}
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
        command.Parameters.Add("@COMPANY_ID", FbDbType.BigInt).Value = companyId.Value;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@DOCUMENT_DATE", FbDbType.Date).Value = documentDate.Date;
        var adjacent = await command.ExecuteScalarAsync(cancellationToken);
        return adjacent is null or DBNull ? null : Convert.ToInt64(adjacent);
    }

    private static async Task<long?> ResolveCompanyIdAsync(
        FbConnection connection,
        long branchId,
        CancellationToken cancellationToken)
    {
        if (branchId <= 0)
            return null;

        await using var command = new FbCommand("""
            SELECT B.COMPANY_ID
            FROM BRANCHES B
            JOIN COMPANIES C ON C.ID = B.COMPANY_ID AND C.IS_ACTIVE = TRUE
            WHERE B.ID = @BRANCH_ID AND B.IS_ACTIVE = TRUE
            """, connection);
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;
        var companyId = await command.ExecuteScalarAsync(cancellationToken);
        return companyId is null or DBNull ? null : Convert.ToInt64(companyId);
    }

    private static async Task<IReadOnlyList<SalesLineSnapshot>> LoadSaleLinesAsync(
        FbConnection connection,
        long salesOrderId,
        CancellationToken cancellationToken)
    {
        var result = new List<SalesLineSnapshot>();
        await using var command = new FbCommand("""
            SELECT SOI.FINISHED_PRODUCT_ID, FP.CODE, SOI.PRODUCT_NAME,
                   COALESCE(FP.GOLD_WEIGHT, SOI.ESTIMATED_WEIGHT),
                   COALESCE(SOI.SOURCE_GOLD_PRICE_PER_CHI, SOI.GOLD_UNIT_PRICE),
                   COALESCE(SOI.SOURCE_LABOR_AMOUNT_VND, SOI.LABOR_AMOUNT),
                   COALESCE(SOI.SOURCE_STONE_AMOUNT_VND, SOI.STONE_AMOUNT),
                   COALESCE(SOI.APPLIED_GOLD_PRICE_PER_CHI, SOI.GOLD_UNIT_PRICE),
                   COALESCE(SOI.APPLIED_LABOR_AMOUNT_VND, SOI.LABOR_AMOUNT),
                   COALESCE(SOI.APPLIED_STONE_AMOUNT_VND, SOI.STONE_AMOUNT),
                   SOI.PRICE_OVERRIDE_REASON, SOI.IS_PRICE_OVERRIDE, SOI.LINE_TOTAL,
                   COALESCE(FP.GOLD_PURITY, ''),
                   COALESCE(FP.STONE_WEIGHT, 0),
                   COALESCE(FP.TOTAL_WEIGHT, COALESCE(FP.GOLD_WEIGHT, SOI.ESTIMATED_WEIGHT))
            FROM SALES_ORDER_ITEMS SOI
            JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
            WHERE SOI.ORDER_ID = @ORDER_ID
            ORDER BY SOI.LINE_NUMBER, SOI.ID
            """, connection);
        command.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = salesOrderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var finishedProductId = reader.GetInt64(0);
            var goldWeight = reader.GetDecimal(3);
            var appliedGold = reader.GetDecimal(7);
            var appliedLabor = reader.GetDecimal(8);
            var appliedStone = reader.GetDecimal(9);
            var isOverride = reader.GetBoolean(11);
            var stoneWeight = reader.GetDecimal(14);
            var totalWeight = reader.GetDecimal(15);
            if (totalWeight <= 0)
                totalWeight = goldWeight + stoneWeight;
            result.Add(new SalesLineSnapshot(
                finishedProductId,
                reader.GetString(1),
                reader.GetString(2),
                goldWeight,
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                new SalesLineDraft(
                    finishedProductId,
                    appliedGold,
                    appliedLabor,
                    appliedStone,
                    reader.IsDBNull(10) ? null : reader.GetString(10)),
                new SalesLineAmounts(
                    finishedProductId,
                    Math.Round(goldWeight * appliedGold, 2, MidpointRounding.AwayFromZero),
                    appliedLabor,
                    appliedStone,
                    reader.GetDecimal(12),
                    isOverride),
                reader.GetString(13),
                stoneWeight,
                totalWeight));
        }

        return result;
    }

    private static async Task<IReadOnlyList<OldGoldLineSnapshot>> LoadOldGoldLinesAsync(
        FbConnection connection,
        long checkoutId,
        CancellationToken cancellationToken)
    {
        var result = new List<OldGoldLineSnapshot>();
        await using var command = new FbCommand("""
            SELECT L.MATERIAL_ID, M.NAME, L.SOURCE_BUY_PRICE_PER_CHI,
                   L.ITEM_QUANTITY, L.TOTAL_WEIGHT_CHI, L.STONE_WEIGHT_CHI,
                   L.IMPURITY_WEIGHT_CHI, L.VALUE_PERCENT, L.APPLIED_BUY_PRICE_PER_CHI,
                   L.PRICE_OVERRIDE_REASON, L.NOTES,
                   L.GOLD_WEIGHT_CHI, L.CONVERTED_WEIGHT_CHI, L.AMOUNT_VND,
                   COALESCE(NULLIF(TRIM(M.CODE), ''), M.NAME)
            FROM SALES_OLD_GOLD_LINES L
            JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
            WHERE L.CHECKOUT_ID = @CHECKOUT_ID
            ORDER BY L.LINE_NUMBER, L.ID
            """, connection);
        command.Parameters.Add("@CHECKOUT_ID", FbDbType.BigInt).Value = checkoutId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new OldGoldLineSnapshot(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                new OldGoldLineDraft(
                    reader.GetInt64(0),
                    reader.GetDecimal(3),
                    reader.GetDecimal(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6),
                    reader.GetDecimal(7),
                    reader.GetDecimal(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetString(10)),
                new OldGoldLineAmounts(
                    reader.GetDecimal(11),
                    reader.GetDecimal(12),
                    reader.GetDecimal(13)),
                reader.GetString(14)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<PaymentAllocationDraft>> LoadAllocationsAsync(
        FbConnection connection,
        long settlementId,
        CancellationToken cancellationToken)
    {
        var result = new List<PaymentAllocationDraft>();
        await using var command = new FbCommand("""
            SELECT PAYMENT_METHOD, AMOUNT_VND, CASH_FUND_ID, BANK_ACCOUNT_ID, REFERENCE_NUMBER
            FROM FINANCIAL_SETTLEMENT_ALLOCATIONS
            WHERE SETTLEMENT_ID = @SETTLEMENT_ID
            ORDER BY LINE_NUMBER, ID
            """, connection);
        command.Parameters.Add("@SETTLEMENT_ID", FbDbType.BigInt).Value = settlementId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PaymentAllocationDraft(
                reader.GetString(0),
                reader.GetDecimal(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return result;
    }

    private sealed record CheckoutHeader(
        long Id,
        string Code,
        string CheckoutType,
        DateTime DocumentDate,
        long CustomerId,
        string CustomerName,
        string? CustomerPhone,
        long SalespersonId,
        string SalespersonName,
        long CreatedBy,
        string CreatedByDisplayName,
        long? SourceLocationId,
        long? OldGoldDestinationLocationId,
        decimal SaleGrossVnd,
        decimal OldGoldTotalVnd,
        decimal DiscountAmountVnd,
        decimal NetAmountVnd,
        decimal OffsetAmountVnd,
        decimal ReceivableAmountVnd,
        string? Notes,
        long? SalesOrderId,
        long? FinancialSettlementId);
}
