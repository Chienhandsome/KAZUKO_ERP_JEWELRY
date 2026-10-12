using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

// Dapper migration: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thêm alias cột
// để Dapper map theo TÊN (case-insensitive) vào constructor param của record đích.
// Boilerplate (FbCommand + reader loop + map theo vị trí cột) được thay bằng
// QueryAsync / QuerySingleOrDefaultAsync với CommandDefinition.
//
// Lưu ý riêng module kho (logic tồn kho vàng nhạy cảm):
// - IsLowStock: logic C# cũ (quantity <= minimum) được đưa vào SQL bằng
//   CASE WHEN ... THEN TRUE ELSE FALSE để Dapper map BOOLEAN -> bool.
// - FinishedProductStockItem / SaleItemStockItem: UnitCode "món" và Quantity
//   (0m khi SOLD/SCRAPPED, ngược lại 1m) vốn hard-code ở C# được thể hiện bằng
//   hằng/CASE trong SELECT để giữ nguyên ngữ nghĩa khi map qua constructor.
// - LineCount (tổng 2 COUNT(*)) được CAST AS INTEGER vì record khai báo int.
public sealed class FirebirdWarehouseRepository : IWarehouseRepository
{
    public async Task<IReadOnlyList<FinishedProductGroupOption>> GetFinishedProductGroupsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<FinishedProductGroupOption>(
            new CommandDefinition("""
                SELECT G.ID AS Id, G.CODE AS Code, G.NAME AS Name
                FROM FINISHED_PRODUCT_GROUPS G
                WHERE G.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND G.IS_ACTIVE = TRUE
                ORDER BY G.DISPLAY_ORDER, G.NAME
                """, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<WarehouseLocationOption>> GetLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<WarehouseLocationOption>(
            new CommandDefinition("""
                SELECT ID AS Id, BRANCH_ID AS BranchId, CODE AS Code, NAME AS Name,
                       LOCATION_TYPE AS LocationType, INVENTORY_SCOPE AS InventoryScope
                FROM LOCATIONS
                WHERE BRANCH_ID = @BRANCH_ID
                  AND IS_ACTIVE = TRUE
                  AND LOCATION_TYPE IN ('WAREHOUSE', 'COUNTER')
                ORDER BY CASE WHEN LOCATION_TYPE = 'WAREHOUSE' THEN 0 ELSE 1 END, NAME
                """,
                new { BRANCH_ID = session.BranchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MaterialOption>> SearchMaterialsAsync(
        string searchText,
        string materialType,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var search = (searchText ?? string.Empty).Trim();
        var type = (materialType ?? string.Empty).Trim().ToUpperInvariant();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MaterialOption>(
            new CommandDefinition("""
                SELECT M.ID AS Id, M.CODE AS Code, M.NAME AS Name, M.MATERIAL_TYPE AS MaterialType,
                       COALESCE(M.PURITY_CODE, '') AS PurityCode, M.UNIT_CODE AS UnitCode, M.IS_ACTIVE AS IsActive,
                       COALESCE(G.BUY_PRICE_PER_CHI, 0) AS ReferenceBuyPricePerChi,
                       M.STONE_KIND AS StoneKind, M.STONE_SHAPE AS StoneShape,
                       M.CARAT_PER_PIECE AS CaratPerPiece, M.SIZE_MM AS SizeMm
                FROM MATERIALS M
                LEFT JOIN GOLD_PRICES G ON G.ID = COALESCE(
                    M.OLD_GOLD_PRICE_ID,
                    (SELECT FIRST 1 P.ID FROM GOLD_PRICES P
                      WHERE P.GOLD_TYPE_ID = M.GOLD_TYPE_ID AND P.IS_ACTIVE = TRUE),
                    (SELECT FIRST 1 P2.ID FROM GOLD_PRICES P2
                      WHERE P2.COMPANY_ID = M.COMPANY_ID
                        AND UPPER(TRIM(P2.CODE)) = UPPER(TRIM(COALESCE(M.PURITY_CODE, '')))
                        AND P2.IS_ACTIVE = TRUE))
                WHERE M.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND M.IS_ACTIVE = TRUE
                  AND (@HAS_TYPE = 0 OR M.MATERIAL_TYPE = @TYPE)
                  AND (@HAS_SEARCH = 0
                       OR CAST(M.CODE AS VARCHAR(100) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR M.NAME CONTAINING @SEARCH)
                ORDER BY M.MATERIAL_TYPE, M.NAME, M.CODE
                """,
                new
                {
                    HAS_TYPE = type.Length > 0 ? 1 : 0,
                    TYPE = type,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MaterialBalanceItem>> SearchMaterialBalancesAsync(
        MaterialBalanceSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var type = (filter.MaterialType ?? string.Empty).Trim().ToUpperInvariant();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<MaterialBalanceItem>(
            new CommandDefinition("""
                SELECT M.ID AS MaterialId, M.CODE AS MaterialCode, M.NAME AS MaterialName,
                       M.MATERIAL_TYPE AS MaterialType, COALESCE(M.PURITY_CODE, '') AS PurityCode,
                       M.UNIT_CODE AS UnitCode,
                       L.ID AS LocationId, L.CODE AS LocationCode, L.NAME AS LocationName,
                       B.QUANTITY AS Quantity, B.AVERAGE_COST AS AverageCost,
                       B.QUANTITY * B.AVERAGE_COST AS StockValue, M.MIN_STOCK AS MinimumStock,
                       CASE WHEN B.QUANTITY <= M.MIN_STOCK THEN TRUE ELSE FALSE END AS IsLowStock
                FROM STOCK_BALANCES B
                JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
                JOIN LOCATIONS L ON L.ID = B.LOCATION_ID
                WHERE L.BRANCH_ID = @BRANCH_ID
                  AND M.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@HAS_LOCATION = 0 OR L.ID = @LOCATION_ID)
                  AND (@HAS_TYPE = 0 OR M.MATERIAL_TYPE = @TYPE)
                  AND (@LOW_ONLY = 0 OR B.QUANTITY <= M.MIN_STOCK)
                  AND (@HAS_SEARCH = 0
                       OR CAST(M.CODE AS VARCHAR(100) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR M.NAME CONTAINING @SEARCH)
                ORDER BY L.NAME, M.MATERIAL_TYPE, M.NAME
                """,
                new
                {
                    BRANCH_ID = session.BranchId,
                    HAS_LOCATION = filter.LocationId > 0 ? 1 : 0,
                    LOCATION_ID = filter.LocationId,
                    HAS_TYPE = type.Length > 0 ? 1 : 0,
                    TYPE = type,
                    LOW_ONLY = filter.LowStockOnly ? 1 : 0,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<FinishedProductStockItem>> SearchFinishedProductStockAsync(
        FinishedProductSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var purity = (filter.GoldPurity ?? string.Empty).Trim().ToUpperInvariant();
        var group = (filter.ProductGroupCode ?? string.Empty).Trim().ToUpperInvariant();
        var status = (filter.StatusCode ?? string.Empty).Trim().ToUpperInvariant();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<FinishedProductStockItem>(
            new CommandDefinition("""
                SELECT F.ID AS Id, F.CODE AS Code, F.PRODUCT_NAME AS ProductName,
                       COALESCE(G.CODE, '') AS ProductGroupCode, COALESCE(G.NAME, '') AS ProductGroupName,
                       COALESCE(F.GOLD_PURITY, '') AS GoldPurity,
                       CAST('món' AS VARCHAR(20) CHARACTER SET UTF8) AS UnitCode,
                       CASE WHEN F.STATUS_CODE IN ('SOLD', 'SCRAPPED')
                            THEN CAST(0 AS DECIMAL(18, 2))
                            ELSE CAST(1 AS DECIMAL(18, 2)) END AS Quantity,
                       CASE WHEN F.RETAIL_PRICE > 0 THEN F.RETAIL_PRICE ELSE F.TOTAL_COST END AS StockValue,
                       F.LOCATION_ID AS LocationId, COALESCE(L.NAME, '') AS LocationName,
                       F.STATUS_CODE AS StatusCode
                FROM FINISHED_PRODUCTS F
                LEFT JOIN LOCATIONS L ON L.ID = F.LOCATION_ID
                LEFT JOIN FINISHED_PRODUCT_GROUPS G ON G.ID = F.FINISHED_PRODUCT_GROUP_ID AND G.COMPANY_ID = F.COMPANY_ID
                WHERE F.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'RESERVED')
                  AND L.INVENTORY_SCOPE = 'FINISHED_GOODS'
                  AND (@HAS_STATUS = 0 OR F.STATUS_CODE = @STATUS)
                  AND (@HAS_GROUP = 0 OR G.CODE = @GROUP_CODE)
                  AND (@HAS_PURITY = 0 OR UPPER(COALESCE(F.GOLD_PURITY, '')) = @PURITY)
                  AND (@HAS_SEARCH = 0
                       OR CAST(F.CODE AS VARCHAR(80) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR F.PRODUCT_NAME CONTAINING @SEARCH)
                ORDER BY F.PRODUCT_NAME, F.CODE
                """,
                new
                {
                    BRANCH_ID = session.BranchId,
                    HAS_STATUS = status.Length > 0 ? 1 : 0,
                    STATUS = status,
                    HAS_GROUP = group.Length > 0 ? 1 : 0,
                    GROUP_CODE = group,
                    HAS_PURITY = purity.Length > 0 ? 1 : 0,
                    PURITY = purity,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SaleItemStockItem>> SearchSaleItemStockAsync(
        SaleItemSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var purity = (filter.GoldPurity ?? string.Empty).Trim().ToUpperInvariant();
        var group = (filter.ProductGroupCode ?? string.Empty).Trim().ToUpperInvariant();
        var status = (filter.StatusCode ?? string.Empty).Trim().ToUpperInvariant();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SaleItemStockItem>(
            new CommandDefinition("""
                SELECT F.ID AS Id, F.CODE AS Code, F.PRODUCT_NAME AS ProductName,
                       COALESCE(G.CODE, '') AS ProductGroupCode, COALESCE(G.NAME, '') AS ProductGroupName,
                       COALESCE(F.GOLD_PURITY, '') AS GoldPurity,
                       CAST('món' AS VARCHAR(20) CHARACTER SET UTF8) AS UnitCode,
                       CAST(1 AS DECIMAL(18, 2)) AS Quantity,
                       CASE WHEN F.RETAIL_PRICE > 0 THEN F.RETAIL_PRICE ELSE F.TOTAL_COST END AS StockValue,
                       F.LOCATION_ID AS LocationId, COALESCE(L.NAME, '') AS LocationName,
                       F.STATUS_CODE AS StatusCode
                FROM FINISHED_PRODUCTS F
                LEFT JOIN LOCATIONS L ON L.ID = F.LOCATION_ID
                LEFT JOIN FINISHED_PRODUCT_GROUPS G ON G.ID = F.FINISHED_PRODUCT_GROUP_ID AND G.COMPANY_ID = F.COMPANY_ID
                WHERE F.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE = 'DISPLAYED'
                  AND L.INVENTORY_SCOPE = 'SALES_FLOOR'
                  AND (@HAS_STATUS = 0 OR F.STATUS_CODE = @STATUS)
                  AND (@HAS_GROUP = 0 OR G.CODE = @GROUP_CODE)
                  AND (@HAS_PURITY = 0 OR UPPER(COALESCE(F.GOLD_PURITY, '')) = @PURITY)
                  AND (@HAS_SEARCH = 0
                       OR CAST(F.CODE AS VARCHAR(80) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR F.PRODUCT_NAME CONTAINING @SEARCH)
                ORDER BY F.PRODUCT_NAME, F.CODE
                """,
                new
                {
                    BRANCH_ID = session.BranchId,
                    HAS_STATUS = status.Length > 0 ? 1 : 0,
                    STATUS = status,
                    HAS_GROUP = group.Length > 0 ? 1 : 0,
                    GROUP_CODE = group,
                    HAS_PURITY = purity.Length > 0 ? 1 : 0,
                    PURITY = purity,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<WarehouseSalesOrderOption>> SearchIssueSalesOrdersAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<WarehouseSalesOrderOption>(
            new CommandDefinition("""
                SELECT O.ID AS Id, O.CODE AS Code, C.NAME AS CustomerName,
                       O.STATUS_CODE AS StatusCode, COALESCE(O.ORDER_TYPE, '') AS OrderType
                FROM SALES_ORDERS O
                JOIN CUSTOMERS C ON C.ID = O.CUSTOMER_ID
                WHERE O.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND O.BRANCH_ID = @BRANCH_ID
                  AND O.STATUS_CODE IN ('CONFIRMED', 'DEPOSITED', 'IN_PRODUCTION', 'READY', 'PARTIALLY_DELIVERED', 'COMPLETED')
                  AND O.FINISHED_GOODS_ISSUE_DOCUMENT_ID IS NULL
                ORDER BY O.ORDER_DATE DESC, O.ID DESC
                """,
                new { BRANCH_ID = session.BranchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<FinishedProductStockItem>> SearchIssueCandidatesAsync(
        long salesOrderId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);
        if (salesOrderId <= 0)
            return Array.Empty<FinishedProductStockItem>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<FinishedProductStockItem>(
            new CommandDefinition("""
                SELECT F.ID AS Id, F.CODE AS Code, F.PRODUCT_NAME AS ProductName,
                       COALESCE(G.CODE, '') AS ProductGroupCode, COALESCE(G.NAME, '') AS ProductGroupName,
                       COALESCE(F.GOLD_PURITY, '') AS GoldPurity,
                       CAST('món' AS VARCHAR(20) CHARACTER SET UTF8) AS UnitCode,
                       CASE WHEN F.STATUS_CODE IN ('SOLD', 'SCRAPPED')
                            THEN CAST(0 AS DECIMAL(18, 2))
                            ELSE CAST(1 AS DECIMAL(18, 2)) END AS Quantity,
                       CASE WHEN F.RETAIL_PRICE > 0 THEN F.RETAIL_PRICE ELSE F.TOTAL_COST END AS StockValue,
                       F.LOCATION_ID AS LocationId, COALESCE(L.NAME, '') AS LocationName,
                       F.STATUS_CODE AS StatusCode
                FROM FINISHED_PRODUCTS F
                LEFT JOIN LOCATIONS L ON L.ID = F.LOCATION_ID
                LEFT JOIN FINISHED_PRODUCT_GROUPS G ON G.ID = F.FINISHED_PRODUCT_GROUP_ID AND G.COMPANY_ID = F.COMPANY_ID
                WHERE F.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.SALES_ORDER_ID = @ORDER_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'RESERVED', 'DISPLAYED')
                ORDER BY F.PRODUCT_NAME, F.CODE
                """,
                new { BRANCH_ID = session.BranchId, ORDER_ID = salesOrderId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<StockDocumentSummary>> SearchDocumentsAsync(
        StockDocumentSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var status = (filter.StatusCode ?? string.Empty).Trim().ToUpperInvariant();
        var types = (filter.DocumentType ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.Trim().ToUpperInvariant())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // DocumentType may be a single code or comma-separated (e.g. ISSUE,PRODUCTION_MATERIAL_ISSUE on Issue tab).
        var typeClause = types.Length == 0
            ? "1=1"
            : types.Length == 1
                ? "D.DOCUMENT_TYPE = @TYPE0"
                : "D.DOCUMENT_TYPE IN (" + string.Join(", ", types.Select((_, i) => "@TYPE" + i)) + ")";

        var parameters = new DynamicParameters();
        parameters.Add("BRANCH_ID", session.BranchId);
        for (var i = 0; i < types.Length; i++)
            parameters.Add("TYPE" + i, types[i]);
        parameters.Add("HAS_STATUS", status.Length > 0 ? 1 : 0);
        parameters.Add("STATUS", status);
        parameters.Add("HAS_FROM", filter.FromDate.HasValue ? 1 : 0);
        parameters.Add("FROM_DATE", filter.FromDate?.Date ?? DateTime.Today);
        parameters.Add("HAS_TO", filter.ToDate.HasValue ? 1 : 0);
        parameters.Add("TO_DATE", filter.ToDate?.Date ?? DateTime.Today);
        parameters.Add("HAS_SEARCH", search.Length > 0 ? 1 : 0);
        parameters.Add("SEARCH", search);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<StockDocumentSummary>(
            new CommandDefinition($"""
                SELECT D.ID AS Id, D.CODE AS Code, D.DOCUMENT_DATE AS DocumentDate,
                       D.DOCUMENT_TYPE AS DocumentType, D.STATUS_CODE AS StatusCode,
                       COALESCE(S.NAME, '') AS SourceLocationName,
                       COALESCE(T.NAME, '') AS DestinationLocationName,
                       COALESCE(D.REFERENCE_NO, '') AS ReferenceNo,
                       COALESCE(NULLIF(D.COUNTERPARTY_NAME, ''), SUP.NAME, CUS.NAME, '') AS CounterpartyName,
                       D.SUPPLIER_ID AS SupplierId,
                       CAST((SELECT COUNT(*) FROM STOCK_DOCUMENT_LINES X WHERE X.DOCUMENT_ID = D.ID)
                         + (SELECT COUNT(*) FROM FINISHED_PRODUCT_DOCUMENT_LINES FX WHERE FX.DOCUMENT_ID = D.ID)
                         AS INTEGER) AS LineCount,
                       COALESCE((SELECT SUM(X.QUANTITY) FROM STOCK_DOCUMENT_LINES X WHERE X.DOCUMENT_ID = D.ID), 0)
                         + (SELECT COUNT(*) FROM FINISHED_PRODUCT_DOCUMENT_LINES FX WHERE FX.DOCUMENT_ID = D.ID)
                         AS TotalQuantity,
                       COALESCE((SELECT SUM(X.LINE_AMOUNT) FROM STOCK_DOCUMENT_LINES X WHERE X.DOCUMENT_ID = D.ID), 0)
                         + COALESCE((SELECT SUM(FX.UNIT_COST) FROM FINISHED_PRODUCT_DOCUMENT_LINES FX WHERE FX.DOCUMENT_ID = D.ID), 0)
                         AS TotalAmount,
                       COALESCE(CU.DISPLAY_NAME, '') AS CreatedByName, D.CREATED_AT AS CreatedAt,
                       COALESCE(PU.DISPLAY_NAME, '') AS PostedByName, D.POSTED_AT AS PostedAt
                FROM STOCK_DOCUMENTS D
                LEFT JOIN LOCATIONS S ON S.ID = D.SOURCE_LOCATION_ID
                LEFT JOIN LOCATIONS T ON T.ID = D.DESTINATION_LOCATION_ID
                LEFT JOIN SUPPLIERS SUP ON SUP.ID = D.SUPPLIER_ID
                LEFT JOIN CUSTOMERS CUS ON CUS.ID = D.CUSTOMER_ID
                LEFT JOIN USERS CU ON CU.ID = D.CREATED_BY
                LEFT JOIN USERS PU ON PU.ID = D.POSTED_BY
                WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND D.BRANCH_ID = @BRANCH_ID
                  AND ({typeClause})
                  AND (@HAS_STATUS = 0 OR D.STATUS_CODE = @STATUS)
                  AND (@HAS_FROM = 0 OR D.DOCUMENT_DATE >= @FROM_DATE)
                  AND (@HAS_TO = 0 OR D.DOCUMENT_DATE <= @TO_DATE)
                  AND (@HAS_SEARCH = 0
                       OR CAST(D.CODE AS VARCHAR(80) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR CAST(COALESCE(D.REFERENCE_NO, '') AS VARCHAR(200) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR COALESCE(NULLIF(D.COUNTERPARTY_NAME, ''), SUP.NAME, CUS.NAME, '') CONTAINING @SEARCH)
                ORDER BY D.DOCUMENT_DATE DESC, D.ID DESC
                """, parameters, cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<StockDocumentDetail?> GetDocumentAsync(
        long documentId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<StockDocumentDetail>(
            new CommandDefinition("""
                SELECT D.ID AS Id, D.CODE AS Code, D.DOCUMENT_DATE AS DocumentDate,
                       D.DOCUMENT_TYPE AS DocumentType, D.STATUS_CODE AS StatusCode,
                       D.SOURCE_LOCATION_ID AS SourceLocationId,
                       D.DESTINATION_LOCATION_ID AS DestinationLocationId,
                       D.PRODUCTION_ORDER_ID AS ProductionOrderId, D.SUPPLIER_ID AS SupplierId,
                       COALESCE(D.PURPOSE_CODE, '') AS PurposeCode, COALESCE(D.REFERENCE_NO, '') AS ReferenceNo,
                       COALESCE(NULLIF(D.COUNTERPARTY_NAME, ''), SUP.NAME, CUS.NAME, '') AS CounterpartyName,
                       COALESCE(D.NOTES, '') AS Notes,
                       COALESCE(CU.DISPLAY_NAME, '') AS CreatedByName, D.CREATED_AT AS CreatedAt,
                       COALESCE(PU.DISPLAY_NAME, '') AS PostedByName, D.POSTED_AT AS PostedAt,
                       COALESCE(D.CANCELLATION_REASON, '') AS CancellationReason,
                       COALESCE(D.SOURCE_TYPE, '') AS SourceType
                FROM STOCK_DOCUMENTS D
                LEFT JOIN SUPPLIERS SUP ON SUP.ID = D.SUPPLIER_ID
                LEFT JOIN CUSTOMERS CUS ON CUS.ID = D.CUSTOMER_ID
                LEFT JOIN USERS CU ON CU.ID = D.CREATED_BY
                LEFT JOIN USERS PU ON PU.ID = D.POSTED_BY
                WHERE D.ID = @ID
                  AND D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND D.BRANCH_ID = @BRANCH_ID
                """,
                new { ID = documentId, BRANCH_ID = session.BranchId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<StockDocumentLine>> GetDocumentLinesAsync(
        long documentId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var materialLines = await connection.QueryAsync<StockDocumentLine>(
            new CommandDefinition("""
                SELECT L.ID AS Id, L.LINE_NUMBER AS LineNumber, L.MATERIAL_ID AS MaterialId,
                       L.PRODUCTION_MATERIAL_PLAN_ID AS ProductionMaterialPlanId,
                       M.CODE AS MaterialCode, M.NAME AS MaterialName, M.MATERIAL_TYPE AS MaterialType,
                       COALESCE(M.PURITY_CODE, '') AS PurityCode, M.UNIT_CODE AS UnitCode,
                       L.QUANTITY AS Quantity, L.UNIT_COST AS UnitCost, L.LINE_AMOUNT AS LineAmount,
                       COALESCE(L.LOT_NO, '') AS LotNo, COALESCE(L.NOTES, '') AS Notes
                FROM STOCK_DOCUMENT_LINES L
                JOIN STOCK_DOCUMENTS D ON D.ID = L.DOCUMENT_ID
                JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
                WHERE L.DOCUMENT_ID = @ID
                  AND D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND D.BRANCH_ID = @BRANCH_ID
                ORDER BY L.LINE_NUMBER
                """,
                new { ID = documentId, BRANCH_ID = session.BranchId },
                cancellationToken: cancellationToken));

        var finishedProductLines = await connection.QueryAsync<StockDocumentLine>(
            new CommandDefinition("""
                SELECT L.ID AS Id, L.LINE_NUMBER AS LineNumber,
                       CAST(0 AS BIGINT) AS MaterialId,
                       CAST(NULL AS BIGINT) AS ProductionMaterialPlanId,
                       F.CODE AS MaterialCode, F.PRODUCT_NAME AS MaterialName,
                       'FINISHED_PRODUCT' AS MaterialType,
                       COALESCE(F.GOLD_PURITY, '') AS PurityCode,
                       CAST('món' AS VARCHAR(20) CHARACTER SET UTF8) AS UnitCode,
                       CAST(1 AS DECIMAL(18, 2)) AS Quantity,
                       L.UNIT_COST AS UnitCost, L.UNIT_COST AS LineAmount,
                       '' AS LotNo, COALESCE(L.NOTES, '') AS Notes
                FROM FINISHED_PRODUCT_DOCUMENT_LINES L
                JOIN STOCK_DOCUMENTS D ON D.ID = L.DOCUMENT_ID
                JOIN FINISHED_PRODUCTS F ON F.ID = L.FINISHED_PRODUCT_ID
                WHERE L.DOCUMENT_ID = @ID
                  AND D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND D.BRANCH_ID = @BRANCH_ID
                ORDER BY L.LINE_NUMBER
                """,
                new { ID = documentId, BRANCH_ID = session.BranchId },
                cancellationToken: cancellationToken));

        return materialLines.Concat(finishedProductLines).OrderBy(x => x.LineNumber).ToList();
    }

    public async Task<IReadOnlyList<StockMovementItem>> SearchMovementsAsync(
        StockMovementSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var search = (filter.SearchText ?? string.Empty).Trim();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<StockMovementItem>(
            new CommandDefinition("""
                SELECT X.ID AS Id, X.OCCURRED_AT AS OccurredAt, D.CODE AS DocumentCode,
                       D.DOCUMENT_TYPE AS DocumentType, M.CODE AS MaterialCode, M.NAME AS MaterialName,
                       L.NAME AS LocationName, X.MOVEMENT_TYPE AS MovementType,
                       X.QUANTITY_CHANGE AS QuantityChange, X.UNIT_COST AS UnitCost,
                       X.VALUE_CHANGE AS ValueChange, COALESCE(U.DISPLAY_NAME, '') AS CreatedByName
                FROM STOCK_MOVEMENTS X
                JOIN STOCK_DOCUMENTS D ON D.ID = X.DOCUMENT_ID
                JOIN MATERIALS M ON M.ID = X.MATERIAL_ID
                JOIN LOCATIONS L ON L.ID = X.LOCATION_ID
                LEFT JOIN USERS U ON U.ID = X.CREATED_BY
                WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND D.BRANCH_ID = @BRANCH_ID
                  AND M.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@HAS_MATERIAL = 0 OR X.MATERIAL_ID = @MATERIAL_ID)
                  AND (@HAS_LOCATION = 0 OR X.LOCATION_ID = @LOCATION_ID)
                  AND (@HAS_SEARCH = 0
                       OR CAST(D.CODE AS VARCHAR(80) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR CAST(M.CODE AS VARCHAR(100) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR M.NAME CONTAINING @SEARCH)
                ORDER BY X.OCCURRED_AT DESC, X.ID DESC
                """,
                new
                {
                    BRANCH_ID = session.BranchId,
                    HAS_MATERIAL = filter.MaterialId > 0 ? 1 : 0,
                    MATERIAL_ID = filter.MaterialId,
                    HAS_LOCATION = filter.LocationId > 0 ? 1 : 0,
                    LOCATION_ID = filter.LocationId,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static void EnsureBranch(AuthenticatedSession session)
    {
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");
    }
}
