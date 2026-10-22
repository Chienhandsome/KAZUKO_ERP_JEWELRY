using System.Data.Common;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Sales;

// Dapper migration: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record (vd. C.ID AS Id).
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

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<SalesCustomerOption>();

        var rows = await connection.QueryAsync<SalesCustomerOption>(
            new CommandDefinition($"""
                SELECT C.ID AS Id, C.CODE AS Code, C.NAME AS Name, C.PHONE AS Phone,
                       C.IS_WALK_IN AS IsWalkIn
                FROM CUSTOMERS C
                WHERE C.COMPANY_ID = @COMPANY_ID
                  AND C.IS_ACTIVE = TRUE
                  AND (@HAS_SEARCH = 0
                       OR CAST(C.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR C.NAME CONTAINING @SEARCH
                       OR COALESCE(C.PHONE, '') CONTAINING @SEARCH)
                ORDER BY C.IS_WALK_IN DESC, CASE WHEN C.IS_WALK_IN THEN C.ID ELSE 0 END, C.NAME, C.CODE, C.ID
                ROWS {CustomerLimit}
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    HAS_SEARCH = search.Length == 0 ? 0 : 1,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SalespersonOption>> GetSalespeopleAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<SalespersonOption>();

        var rows = await connection.QueryAsync<SalespersonOption>(
            new CommandDefinition($"""
                SELECT U.ID AS Id, U.USERNAME AS Username, U.DISPLAY_NAME AS DisplayName
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
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
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

        return await connection.QuerySingleOrDefaultAsync<SalesProductOption>(
            new CommandDefinition($"""
                SELECT FIRST 1
                       {ProductSelectList}
                {SellableProductFrom}
                WHERE F.COMPANY_ID = @COMPANY_ID
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
                  AND (UPPER(TRIM(F.CODE)) = @CODE
                       OR UPPER(TRIM(COALESCE(F.BARCODE_VALUE, ''))) = @CODE
                       OR UPPER(TRIM(COALESCE(F.QR_VALUE, ''))) = @CODE)
                ORDER BY F.ID
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    CODE = code
                },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<SalesProductOption>> SearchAvailableProductsAsync(
        string searchText,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var search = (searchText ?? string.Empty).Trim();
        if (search.Length == 0)
            return Array.Empty<SalesProductOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<SalesProductOption>();

        var rows = await connection.QueryAsync<SalesProductOption>(
            new CommandDefinition($"""
                SELECT FIRST {ProductSearchLimit}
                       {ProductSelectList}
                {SellableProductFrom}
                WHERE F.COMPANY_ID = @COMPANY_ID
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
                  AND (F.CODE CONTAINING @SEARCH
                       OR F.PRODUCT_NAME CONTAINING @SEARCH)
                ORDER BY F.CODE, F.ID
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    /// <summary>
    /// Danh sách cột SELECT cho SalesProductOption — alias khớp tên constructor param
    /// của record (Dapper map theo tên, case-insensitive).
    /// </summary>
    private const string ProductSelectList = """
        F.ID AS FinishedProductId, F.CODE AS ProductCode, F.PRODUCT_NAME AS ProductName,
        GP.ID AS RetailGoldPriceId, GP.NAME AS RetailGoldPriceName,
        L.ID AS SourceLocationId, COALESCE(F.GOLD_PURITY, '') AS GoldPurity, F.ROW_VERSION AS RowVersion,
        F.TOTAL_WEIGHT AS TotalWeightChi, F.STONE_WEIGHT AS StoneWeightChi, F.GOLD_WEIGHT AS GoldWeightChi,
        GP.SELL_PRICE_PER_CHI AS SourceGoldPricePerChi, F.LABOR_COST AS SourceLaborAmountVnd,
        F.NON_GOLD_MATERIAL_COST AS SourceStoneAmountVnd
        """;

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

        return await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("""
                SELECT COUNT(*)
                FROM FINISHED_PRODUCTS F
                WHERE F.COMPANY_ID = @COMPANY_ID
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
                  AND (F.CODE CONTAINING @SEARCH
                       OR F.PRODUCT_NAME CONTAINING @SEARCH)
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<OldGoldMaterialOption>> GetOldGoldMaterialsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<OldGoldMaterialOption>();

        var rows = await connection.QueryAsync<OldGoldMaterialOption>(
            new CommandDefinition($"""
                SELECT M.ID AS Id, M.CODE AS Code, M.NAME AS Name, GP.ID AS OldGoldPriceId,
                       GP.PURITY_RATE * 100 AS ValuePercent, GP.BUY_PRICE_PER_CHI AS SourceBuyPricePerChi
                FROM MATERIALS M
                JOIN GOLD_PRICES GP ON GP.ID = M.OLD_GOLD_PRICE_ID
                                   AND GP.COMPANY_ID = M.COMPANY_ID
                                   AND GP.IS_ACTIVE = TRUE
                WHERE M.COMPANY_ID = @COMPANY_ID
                  AND M.MATERIAL_TYPE = 'GOLD'
                  AND M.IS_ACTIVE = TRUE
                ORDER BY M.NAME, M.CODE, M.ID
                ROWS {LookupLimit}
                """,
                new { COMPANY_ID = companyId.Value },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SalesLocationOption>> GetOldGoldLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<SalesLocationOption>();

        var rows = await connection.QueryAsync<SalesLocationOption>(
            new CommandDefinition($"""
                SELECT L.ID AS Id, L.CODE AS Code, L.NAME AS Name,
                       CASE WHEN UPPER(L.CODE) CONTAINING 'OLD_GOLD'
                                  OR UPPER(L.CODE) CONTAINING 'THU_HOI'
                            THEN TRUE ELSE FALSE END AS IsDefault
                FROM LOCATIONS L
                JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @COMPANY_ID
                WHERE L.BRANCH_ID = @BRANCH_ID
                  AND L.IS_ACTIVE = TRUE
                  AND L.INVENTORY_SCOPE = 'RAW_MATERIAL'
                ORDER BY 4 DESC, L.NAME, L.CODE, L.ID
                ROWS {LookupLimit}
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
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

        var header = await connection.QuerySingleOrDefaultAsync<CheckoutHeader>(
            new CommandDefinition("""
                SELECT SC.ID AS Id, SC.CODE AS Code, SC.CHECKOUT_TYPE AS CheckoutType,
                       SC.DOCUMENT_DATE AS DocumentDate,
                       SC.CUSTOMER_ID AS CustomerId, C.NAME AS CustomerName, C.PHONE AS CustomerPhone,
                       SC.SALESPERSON_ID AS SalespersonId, SP.DISPLAY_NAME AS SalespersonName,
                       SC.CREATED_BY AS CreatedBy, CB.DISPLAY_NAME AS CreatedByDisplayName,
                       SC.SOURCE_LOCATION_ID AS SourceLocationId,
                       SC.OLD_GOLD_DESTINATION_LOCATION_ID AS OldGoldDestinationLocationId,
                       SC.SALE_GROSS_VND AS SaleGrossVnd, SC.OLD_GOLD_TOTAL_VND AS OldGoldTotalVnd,
                       SC.DISCOUNT_AMOUNT_VND AS DiscountAmountVnd, SC.NET_AMOUNT_VND AS NetAmountVnd,
                       COALESCE((SELECT SCO.AMOUNT_VND FROM SALES_CHECKOUT_OFFSETS SCO
                                 WHERE SCO.CHECKOUT_ID = SC.ID), 0) AS OffsetAmountVnd,
                       COALESCE(RD.ORIGINAL_AMOUNT_VND, 0) AS ReceivableAmountVnd,
                       SC.NOTES AS Notes, SC.SALES_ORDER_ID AS SalesOrderId,
                       SC.FINANCIAL_SETTLEMENT_ID AS FinancialSettlementId
                FROM SALES_CHECKOUTS SC
                JOIN CUSTOMERS C ON C.ID = SC.CUSTOMER_ID AND C.COMPANY_ID = SC.COMPANY_ID
                JOIN USERS SP ON SP.ID = SC.SALESPERSON_ID
                JOIN USERS CB ON CB.ID = SC.CREATED_BY
                LEFT JOIN RECEIVABLE_DEBTS RD ON RD.ID = SC.RECEIVABLE_DEBT_ID
                WHERE SC.ID = @ID
                  AND SC.COMPANY_ID = @COMPANY_ID
                  AND SC.BRANCH_ID = @BRANCH_ID
                  AND SC.STATUS_CODE = 'POSTED'
                """,
                new
                {
                    ID = checkoutId,
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId
                },
                cancellationToken: cancellationToken));
        if (header is null)
            return null;

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

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<SalesCheckoutListItem>();

        var limit = filter.Limit <= 0 ? 100 : Math.Min(filter.Limit, 500);
        var search = (filter.SearchText ?? string.Empty).Trim();
        var pattern = search.Length > 0 ? "%" + search.ToUpperInvariant() + "%" : null;

        var rows = await connection.QueryAsync<SalesCheckoutListItem>(
            new CommandDefinition($"""
                SELECT FIRST {limit}
                       SC.ID AS CheckoutId, SC.CODE AS DocumentCode,
                       SC.CHECKOUT_TYPE AS CheckoutType, SC.DOCUMENT_DATE AS DocumentDate,
                       C.NAME AS CustomerName, C.PHONE AS CustomerPhone,
                       SP.DISPLAY_NAME AS SalespersonName, SC.NET_AMOUNT_VND AS NetAmountVnd
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
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    FROM_DATE = (object?)filter.FromDate?.Date ?? DBNull.Value,
                    TO_DATE_EXCLUSIVE = (object?)filter.ToDate?.Date.AddDays(1) ?? DBNull.Value,
                    PATTERN = (object?)pattern ?? DBNull.Value
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
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

        var documentDate = await connection.QuerySingleOrDefaultAsync<DateTime?>(
            new CommandDefinition("""
                SELECT DOCUMENT_DATE
                FROM SALES_CHECKOUTS
                WHERE ID = @ID
                  AND COMPANY_ID = @COMPANY_ID
                  AND BRANCH_ID = @BRANCH_ID
                  AND STATUS_CODE = 'POSTED'
                """,
                new
                {
                    ID = checkoutId,
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId
                },
                cancellationToken: cancellationToken));
        if (documentDate is null)
            return null;

        var comparison = next
            ? "(DOCUMENT_DATE > @DOCUMENT_DATE OR (DOCUMENT_DATE = @DOCUMENT_DATE AND ID > @ID))"
            : "(DOCUMENT_DATE < @DOCUMENT_DATE OR (DOCUMENT_DATE = @DOCUMENT_DATE AND ID < @ID))";
        var order = next ? "DOCUMENT_DATE, ID" : "DOCUMENT_DATE DESC, ID DESC";
        return await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition($"""
                SELECT FIRST 1 ID
                FROM SALES_CHECKOUTS
                WHERE COMPANY_ID = @COMPANY_ID
                  AND BRANCH_ID = @BRANCH_ID
                  AND STATUS_CODE = 'POSTED'
                  AND {comparison}
                ORDER BY {order}
                """,
                new
                {
                    ID = checkoutId,
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    DOCUMENT_DATE = documentDate.Value.Date
                },
                cancellationToken: cancellationToken));
    }

    private static async Task<long?> ResolveCompanyIdAsync(
        DbConnection connection,
        long branchId,
        CancellationToken cancellationToken)
    {
        if (branchId <= 0)
            return null;

        return await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("""
                SELECT B.COMPANY_ID
                FROM BRANCHES B
                JOIN COMPANIES C ON C.ID = B.COMPANY_ID AND C.IS_ACTIVE = TRUE
                WHERE B.ID = @BRANCH_ID AND B.IS_ACTIVE = TRUE
                """,
                new { BRANCH_ID = branchId },
                cancellationToken: cancellationToken));
    }

    private static async Task<IReadOnlyList<SalesLineSnapshot>> LoadSaleLinesAsync(
        DbConnection connection,
        long salesOrderId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<SaleLineRow>(
            new CommandDefinition("""
                SELECT SOI.FINISHED_PRODUCT_ID AS FinishedProductId, FP.CODE AS ProductCode,
                       SOI.PRODUCT_NAME AS ProductName,
                       COALESCE(FP.GOLD_WEIGHT, SOI.ESTIMATED_WEIGHT) AS GoldWeightChi,
                       COALESCE(SOI.SOURCE_GOLD_PRICE_PER_CHI, SOI.GOLD_UNIT_PRICE) AS SourceGoldPricePerChi,
                       COALESCE(SOI.SOURCE_LABOR_AMOUNT_VND, SOI.LABOR_AMOUNT) AS SourceLaborAmountVnd,
                       COALESCE(SOI.SOURCE_STONE_AMOUNT_VND, SOI.STONE_AMOUNT) AS SourceStoneAmountVnd,
                       COALESCE(SOI.APPLIED_GOLD_PRICE_PER_CHI, SOI.GOLD_UNIT_PRICE) AS AppliedGoldPricePerChi,
                       COALESCE(SOI.APPLIED_LABOR_AMOUNT_VND, SOI.LABOR_AMOUNT) AS AppliedLaborAmountVnd,
                       COALESCE(SOI.APPLIED_STONE_AMOUNT_VND, SOI.STONE_AMOUNT) AS AppliedStoneAmountVnd,
                       SOI.PRICE_OVERRIDE_REASON AS PriceOverrideReason,
                       SOI.IS_PRICE_OVERRIDE AS IsPriceOverride,
                       SOI.LINE_TOTAL AS LineTotal,
                       COALESCE(FP.GOLD_PURITY, '') AS GoldPurity,
                       COALESCE(FP.STONE_WEIGHT, 0) AS StoneWeightChi,
                       COALESCE(FP.TOTAL_WEIGHT, COALESCE(FP.GOLD_WEIGHT, SOI.ESTIMATED_WEIGHT)) AS TotalWeightChi
                FROM SALES_ORDER_ITEMS SOI
                JOIN FINISHED_PRODUCTS FP ON FP.ID = SOI.FINISHED_PRODUCT_ID
                WHERE SOI.ORDER_ID = @ORDER_ID
                ORDER BY SOI.LINE_NUMBER, SOI.ID
                """,
                new { ORDER_ID = salesOrderId },
                cancellationToken: cancellationToken));

        var result = new List<SalesLineSnapshot>();
        foreach (var row in rows)
        {
            var totalWeight = row.TotalWeightChi;
            if (totalWeight <= 0)
                totalWeight = row.GoldWeightChi + row.StoneWeightChi;
            result.Add(new SalesLineSnapshot(
                row.FinishedProductId,
                row.ProductCode,
                row.ProductName,
                row.GoldWeightChi,
                row.SourceGoldPricePerChi,
                row.SourceLaborAmountVnd,
                row.SourceStoneAmountVnd,
                new SalesLineDraft(
                    row.FinishedProductId,
                    row.AppliedGoldPricePerChi,
                    row.AppliedLaborAmountVnd,
                    row.AppliedStoneAmountVnd,
                    row.PriceOverrideReason),
                new SalesLineAmounts(
                    row.FinishedProductId,
                    Math.Round(row.GoldWeightChi * row.AppliedGoldPricePerChi, 2, MidpointRounding.AwayFromZero),
                    row.AppliedLaborAmountVnd,
                    row.AppliedStoneAmountVnd,
                    row.LineTotal,
                    row.IsPriceOverride),
                row.GoldPurity,
                row.StoneWeightChi,
                totalWeight));
        }

        return result;
    }

    private static async Task<IReadOnlyList<OldGoldLineSnapshot>> LoadOldGoldLinesAsync(
        DbConnection connection,
        long checkoutId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<OldGoldLineRow>(
            new CommandDefinition("""
                SELECT L.MATERIAL_ID AS MaterialId, M.NAME AS MaterialName,
                       L.SOURCE_BUY_PRICE_PER_CHI AS SourceBuyPricePerChi,
                       L.ITEM_QUANTITY AS ItemQuantity,
                       L.TOTAL_WEIGHT_CHI AS TotalWeightChi,
                       L.STONE_WEIGHT_CHI AS StoneWeightChi,
                       L.IMPURITY_WEIGHT_CHI AS ImpurityWeightChi,
                       L.VALUE_PERCENT AS ValuePercent,
                       L.APPLIED_BUY_PRICE_PER_CHI AS AppliedBuyPricePerChi,
                       L.PRICE_OVERRIDE_REASON AS PriceOverrideReason,
                       L.NOTES AS Notes,
                       L.GOLD_WEIGHT_CHI AS GoldWeightChi,
                       L.CONVERTED_WEIGHT_CHI AS ConvertedWeightChi,
                       L.AMOUNT_VND AS AmountVnd,
                       COALESCE(NULLIF(TRIM(M.CODE), ''), M.NAME) AS MaterialCode
                FROM SALES_OLD_GOLD_LINES L
                JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
                WHERE L.CHECKOUT_ID = @CHECKOUT_ID
                ORDER BY L.LINE_NUMBER, L.ID
                """,
                new { CHECKOUT_ID = checkoutId },
                cancellationToken: cancellationToken));

        var result = new List<OldGoldLineSnapshot>();
        foreach (var row in rows)
        {
            result.Add(new OldGoldLineSnapshot(
                row.MaterialId,
                row.MaterialName,
                row.SourceBuyPricePerChi,
                new OldGoldLineDraft(
                    row.MaterialId,
                    row.ItemQuantity,
                    row.TotalWeightChi,
                    row.StoneWeightChi,
                    row.ImpurityWeightChi,
                    row.ValuePercent,
                    row.AppliedBuyPricePerChi,
                    row.PriceOverrideReason,
                    row.Notes),
                new OldGoldLineAmounts(
                    row.GoldWeightChi,
                    row.ConvertedWeightChi,
                    row.AmountVnd),
                row.MaterialCode));
        }

        return result;
    }

    private static async Task<IReadOnlyList<PaymentAllocationDraft>> LoadAllocationsAsync(
        DbConnection connection,
        long settlementId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<PaymentAllocationDraft>(
            new CommandDefinition("""
                SELECT PAYMENT_METHOD AS PaymentMethod, AMOUNT_VND AS AmountVnd,
                       CASH_FUND_ID AS CashFundId, BANK_ACCOUNT_ID AS BankAccountId,
                       REFERENCE_NUMBER AS ReferenceNumber
                FROM FINANCIAL_SETTLEMENT_ALLOCATIONS
                WHERE SETTLEMENT_ID = @SETTLEMENT_ID
                ORDER BY LINE_NUMBER, ID
                """,
                new { SETTLEMENT_ID = settlementId },
                cancellationToken: cancellationToken));
        return rows.AsList();
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

    private sealed record SaleLineRow(
        long FinishedProductId,
        string ProductCode,
        string ProductName,
        decimal GoldWeightChi,
        decimal SourceGoldPricePerChi,
        decimal SourceLaborAmountVnd,
        decimal SourceStoneAmountVnd,
        decimal AppliedGoldPricePerChi,
        decimal AppliedLaborAmountVnd,
        decimal AppliedStoneAmountVnd,
        string? PriceOverrideReason,
        bool IsPriceOverride,
        decimal LineTotal,
        string GoldPurity,
        decimal StoneWeightChi,
        decimal TotalWeightChi);

    private sealed record OldGoldLineRow(
        long MaterialId,
        string MaterialName,
        decimal SourceBuyPricePerChi,
        decimal ItemQuantity,
        decimal TotalWeightChi,
        decimal StoneWeightChi,
        decimal ImpurityWeightChi,
        decimal ValuePercent,
        decimal AppliedBuyPricePerChi,
        string? PriceOverrideReason,
        string? Notes,
        decimal GoldWeightChi,
        decimal ConvertedWeightChi,
        decimal AmountVnd,
        string MaterialCode);
}
