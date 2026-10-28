using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Production;

// Dapper migration (Phase 2): SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record (vd. PO.ID AS Id).
// `using FirebirdSql.Data.FirebirdClient` vẫn cần: helper methods giữ FbConnection
// và catch FbException khi migration 46 chưa được áp dụng.
public sealed class FirebirdProductionQueryRepository : IProductionQueryRepository
{
    // Header 24 cột của GetOrderDetailAsync: Dapper map theo tên cột -> ctor param.
    private sealed record ProductionOrderHeaderRow(
        long Id,
        string Code,
        string OrderSource,
        long? SalesOrderId,
        string? SalesOrderCode,
        string ExecutionType,
        long? ExternalWorkerId,
        string? ExternalWorkerName,
        string StatusCode,
        DateTime OrderDate,
        DateTime? DueDate,
        long SourceLocationId,
        string SourceLocationName,
        long FinishedLocationId,
        string FinishedLocationName,
        long? MaterialIssueDocumentId,
        string? MaterialIssueDocumentCode,
        DateTime? MaterialIssueDocumentDate,
        long? ReturnDocumentId,
        string? ReturnDocumentCode,
        long? FinishedReceiptDocumentId,
        string? FinishedReceiptDocumentCode,
        string? Notes,
        long RowVersion);

    // Row trung gian của GetMaterialBalanceAllocationsAsync:
    // QuantityRemaining được tính ở C# (giữ nguyên logic bản cũ).
    private sealed record ProductionMaterialBalanceAllocationRow(
        long MaterialBalanceId,
        decimal QuantityInProduct,
        decimal QuantityUsedByPassed);

    public async Task<ProductionOrderSummary> GetSummaryAsync(
        long branchId,
        DateTime? periodFrom,
        DateTime? periodTo,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            throw new InvalidOperationException("Thiếu chi nhánh.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        // M1 redesign (B2): mọi KPI đều đếm trên TOÀN BỘ, không theo kỳ Từ–Đến.
        // Tham số kỳ vẫn giữ ở signature cho tương thích interface nhưng bị bỏ qua.
        // SUM trả về BIGINT nhưng record ProductionOrderSummary khai báo int -> CAST AS INTEGER
        // (Dapper chỉ bind constructor khi kiểu khớp chính xác, không tự Convert như bản cũ).
        // SUM = NULL khi không có dòng nào -> COALESCE về 0 (giống IsDBNull ? 0 bản cũ).
        _ = periodFrom;
        _ = periodTo;
        return await connection.QuerySingleAsync<ProductionOrderSummary>(
            new CommandDefinition("""
                SELECT
                    COALESCE(CAST(SUM(CASE WHEN PO.STATUS_CODE = 'IN_PROGRESS' THEN 1 ELSE 0 END) AS INTEGER), 0) AS InProgressCount,
                    COALESCE(CAST(SUM(CASE WHEN PO.STATUS_CODE = 'COMPLETED' THEN 1 ELSE 0 END) AS INTEGER), 0) AS CompletedCount,
                    COALESCE(CAST(SUM(CASE WHEN PO.STATUS_CODE = 'CANCELLED' THEN 1 ELSE 0 END) AS INTEGER), 0) AS CancelledCount
                FROM PRODUCTION_ORDERS PO
                WHERE PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND PO.BRANCH_ID = @BRANCH_ID
                """,
                new { BRANCH_ID = branchId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<ProductionOrderListItem>> SearchOrdersAsync(
        long branchId,
        ProductionOrderListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (branchId <= 0)
            throw new InvalidOperationException("Thiếu chi nhánh.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = filter.SearchText?.Trim() ?? string.Empty;
        var status = filter.StatusCode?.Trim() ?? string.Empty;
        var source = filter.OrderSource?.Trim() ?? string.Empty;
        var execution = filter.ExecutionType?.Trim() ?? string.Empty;
        var orderCode = filter.OrderCode?.Trim() ?? string.Empty;
        var customerName = filter.CustomerName?.Trim() ?? string.Empty;
        var salesOrderCode = filter.SalesOrderCode?.Trim() ?? string.Empty;
        var workerName = filter.WorkerName?.Trim() ?? string.Empty;
        var modelName = filter.ModelName?.Trim() ?? string.Empty;

        var rows = await connection.QueryAsync<ProductionOrderListItem>(
            new CommandDefinition("""
                SELECT PO.ID AS Id, PO.CODE AS Code, PO.ORDER_SOURCE AS OrderSource,
                       PO.EXECUTION_TYPE AS ExecutionType, PO.STATUS_CODE AS StatusCode,
                       PO.ORDER_DATE AS OrderDate,
                       MI.DOCUMENT_DATE AS MaterialIssueDate,
                       PO.DUE_DATE AS DueDate,
                       SO.CODE AS SalesOrderCode,
                       C.NAME AS CustomerName,
                       CASE
                           WHEN PO.EXECUTION_TYPE = 'OUTSOURCED' THEN EW.NAME
                           ELSE (
                               SELECT LIST(TRIM(E.FULL_NAME), ', ')
                               FROM PRODUCTION_ORDER_WORKERS POW
                               JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
                               WHERE POW.PRODUCTION_ORDER_ID = PO.ID)
                       END AS WorkerDisplayName,
                       COALESCE((
                           SELECT SUBSTRING(LIST(TRIM(POL.MODEL_NAME), '; ') FROM 1 FOR 200)
                           FROM PRODUCTION_ORDER_LINES POL
                           WHERE POL.PRODUCTION_ORDER_ID = PO.ID
                       ), '') AS ModelSummary,
                       COALESCE(CAST((
                           SELECT SUM(POL.PLANNED_QUANTITY)
                           FROM PRODUCTION_ORDER_LINES POL
                           WHERE POL.PRODUCTION_ORDER_ID = PO.ID
                       ) AS INTEGER), 0) AS PlannedQuantityTotal,
                       PO.ROW_VERSION AS RowVersion,PO.FINISHED_RECEIPT_DOCUMENT_ID AS FinishedReceiptDocumentId
                FROM PRODUCTION_ORDERS PO
                LEFT JOIN SALES_ORDERS SO ON SO.ID = PO.SALES_ORDER_ID
                LEFT JOIN CUSTOMERS C ON C.ID = SO.CUSTOMER_ID AND C.COMPANY_ID = PO.COMPANY_ID
                LEFT JOIN EXTERNAL_WORKERS EW ON EW.COMPANY_ID = PO.COMPANY_ID AND EW.ID = PO.EXTERNAL_WORKER_ID
                LEFT JOIN STOCK_DOCUMENTS MI ON MI.ID = PO.MATERIAL_ISSUE_DOCUMENT_ID
                WHERE PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND PO.BRANCH_ID = @BRANCH_ID
                  AND (@INCLUDE_DRAFTS = 1 OR PO.STATUS_CODE <> 'DRAFT')
                  AND (@HAS_STATUS = 0 OR PO.STATUS_CODE = @STATUS_CODE)
                  AND (@HAS_SOURCE = 0 OR PO.ORDER_SOURCE = @ORDER_SOURCE)
                  AND (@HAS_EXEC = 0 OR PO.EXECUTION_TYPE = @EXECUTION_TYPE)
                  AND (@HAS_FROM = 0 OR PO.ORDER_DATE >= @FROM_DATE)
                  AND (@HAS_TO = 0 OR PO.ORDER_DATE < @TO_DATE_EXCL)
                  AND (@HAS_SEARCH = 0
                       OR PO.CODE CONTAINING @SEARCH
                       OR COALESCE(SO.CODE, '') CONTAINING @SEARCH
                       OR COALESCE(EW.NAME, '') CONTAINING @SEARCH
                       OR EXISTS (
                           SELECT 1 FROM PRODUCTION_ORDER_LINES POL
                           WHERE POL.PRODUCTION_ORDER_ID = PO.ID
                             AND (POL.MODEL_NAME CONTAINING @SEARCH OR COALESCE(POL.MODEL_CODE, '') CONTAINING @SEARCH))
                       OR EXISTS (
                           SELECT 1 FROM PRODUCTION_ORDER_WORKERS POW
                           JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
                           WHERE POW.PRODUCTION_ORDER_ID = PO.ID
                             AND (E.FULL_NAME CONTAINING @SEARCH OR E.CODE CONTAINING @SEARCH)))
                  AND (@HAS_ORDER_CODE = 0 OR PO.CODE CONTAINING @ORDER_CODE)
                  AND (@HAS_CUSTOMER = 0 OR C.NAME CONTAINING @CUSTOMER_NAME)
                  AND (@HAS_SO_CODE = 0 OR SO.CODE CONTAINING @SO_CODE)
                  AND (@HAS_WORKER = 0
                       OR COALESCE(EW.NAME, '') CONTAINING @WORKER_NAME
                       OR EXISTS (
                           SELECT 1 FROM PRODUCTION_ORDER_WORKERS POW
                           JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
                           WHERE POW.PRODUCTION_ORDER_ID = PO.ID
                             AND E.FULL_NAME CONTAINING @WORKER_NAME))
                  AND (@HAS_MODEL = 0
                       OR EXISTS (
                           SELECT 1 FROM PRODUCTION_ORDER_LINES POL
                           WHERE POL.PRODUCTION_ORDER_ID = PO.ID
                             AND POL.MODEL_NAME CONTAINING @MODEL_NAME))
                ORDER BY PO.ORDER_DATE DESC, PO.ID DESC
                """,
                new
                {
                    BRANCH_ID = branchId,
                    INCLUDE_DRAFTS = filter.IncludeDrafts ? 1 : 0,
                    HAS_STATUS = status.Length > 0 ? 1 : 0,
                    STATUS_CODE = status,
                    HAS_SOURCE = source.Length > 0 ? 1 : 0,
                    ORDER_SOURCE = source,
                    HAS_EXEC = execution.Length > 0 ? 1 : 0,
                    EXECUTION_TYPE = execution,
                    HAS_FROM = filter.FromDate is not null ? 1 : 0,
                    FROM_DATE = (DateTime?)filter.FromDate,
                    HAS_TO = filter.ToDate is not null ? 1 : 0,
                    TO_DATE_EXCL = (DateTime?)filter.ToDate?.Date.AddDays(1),
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search,
                    HAS_ORDER_CODE = orderCode.Length > 0 ? 1 : 0,
                    ORDER_CODE = orderCode,
                    HAS_CUSTOMER = customerName.Length > 0 ? 1 : 0,
                    CUSTOMER_NAME = customerName,
                    HAS_SO_CODE = salesOrderCode.Length > 0 ? 1 : 0,
                    SO_CODE = salesOrderCode,
                    HAS_WORKER = workerName.Length > 0 ? 1 : 0,
                    WORKER_NAME = workerName,
                    HAS_MODEL = modelName.Length > 0 ? 1 : 0,
                    MODEL_NAME = modelName
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductionFinishedReceiptOrderOption>> GetCompletedUnreceivedOrdersAsync(
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return [];

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProductionFinishedReceiptOrderOption>(
            new CommandDefinition("""
                SELECT ID AS Id, CODE AS Code
                FROM PRODUCTION_ORDERS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND BRANCH_ID = @BRANCH_ID
                  AND STATUS_CODE = 'COMPLETED'
                  AND FINISHED_RECEIPT_DOCUMENT_ID IS NULL
                ORDER BY COMPLETED_AT DESC, ID DESC
                """,
                new { BRANCH_ID = branchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<string>> GetExistingFinishedProductCodesAsync(
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return [];

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(
            new CommandDefinition("""
                SELECT CODE
                FROM FINISHED_PRODUCTS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND BRANCH_ID = @BRANCH_ID
                """,
                new { BRANCH_ID = branchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<ProductionOrderDetail?> GetOrderDetailAsync(
        long orderId,
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (orderId <= 0 || branchId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<ProductionOrderHeaderRow>(
            new CommandDefinition("""
                SELECT PO.ID AS Id, PO.CODE AS Code, PO.ORDER_SOURCE AS OrderSource,
                       PO.SALES_ORDER_ID AS SalesOrderId, SO.CODE AS SalesOrderCode,
                       PO.EXECUTION_TYPE AS ExecutionType,
                       PO.EXTERNAL_WORKER_ID AS ExternalWorkerId, EW.NAME AS ExternalWorkerName,
                       PO.STATUS_CODE AS StatusCode, PO.ORDER_DATE AS OrderDate,
                       PO.DUE_DATE AS DueDate,
                       PO.SOURCE_LOCATION_ID AS SourceLocationId, SL.NAME AS SourceLocationName,
                       PO.FINISHED_LOCATION_ID AS FinishedLocationId, FL.NAME AS FinishedLocationName,
                       PO.MATERIAL_ISSUE_DOCUMENT_ID AS MaterialIssueDocumentId, MI.CODE AS MaterialIssueDocumentCode,
                       MI.DOCUMENT_DATE AS MaterialIssueDocumentDate,
                       PO.RETURN_DOCUMENT_ID AS ReturnDocumentId, RR.CODE AS ReturnDocumentCode,
                       PO.FINISHED_RECEIPT_DOCUMENT_ID AS FinishedReceiptDocumentId, FR.CODE AS FinishedReceiptDocumentCode,
                       PO.NOTES AS Notes, PO.ROW_VERSION AS RowVersion
                FROM PRODUCTION_ORDERS PO
                LEFT JOIN SALES_ORDERS SO ON SO.ID = PO.SALES_ORDER_ID
                LEFT JOIN EXTERNAL_WORKERS EW ON EW.COMPANY_ID = PO.COMPANY_ID AND EW.ID = PO.EXTERNAL_WORKER_ID
                LEFT JOIN LOCATIONS SL ON SL.ID = PO.SOURCE_LOCATION_ID
                LEFT JOIN LOCATIONS FL ON FL.ID = PO.FINISHED_LOCATION_ID
                LEFT JOIN STOCK_DOCUMENTS MI ON MI.ID = PO.MATERIAL_ISSUE_DOCUMENT_ID
                LEFT JOIN STOCK_DOCUMENTS RR ON RR.ID = PO.RETURN_DOCUMENT_ID
                LEFT JOIN STOCK_DOCUMENTS FR ON FR.ID = PO.FINISHED_RECEIPT_DOCUMENT_ID
                WHERE PO.ID = @ID
                  AND PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND PO.BRANCH_ID = @BRANCH_ID
                """,
                new { ID = orderId, BRANCH_ID = branchId },
                cancellationToken: cancellationToken));
        if (header is null)
            return null;

        return new ProductionOrderDetail(
            header.Id,
            header.Code,
            header.OrderSource,
            header.SalesOrderId,
            header.SalesOrderCode,
            header.ExecutionType,
            header.ExternalWorkerId,
            header.ExternalWorkerName,
            header.StatusCode,
            header.OrderDate,
            header.DueDate,
            header.SourceLocationId,
            header.SourceLocationName,
            header.FinishedLocationId,
            header.FinishedLocationName,
            header.MaterialIssueDocumentId,
            header.MaterialIssueDocumentCode,
            header.MaterialIssueDocumentDate,
            header.ReturnDocumentId,
            header.ReturnDocumentCode,
            header.FinishedReceiptDocumentId,
            header.FinishedReceiptDocumentCode,
            header.Notes,
            header.RowVersion,
            await LoadLinesAsync(connection, orderId, cancellationToken),
            await LoadWorkersAsync(connection, orderId, cancellationToken),
            await LoadAttachmentsAsync(connection, orderId, cancellationToken),
            await LoadMaterialBalancesAsync(connection, orderId, cancellationToken),
            await LoadQcUnitsAsync(connection, orderId, cancellationToken));
    }

    public async Task<IReadOnlyList<ProductionSalesOrderOption>> SearchEligibleSalesOrdersAsync(
        long branchId,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return Array.Empty<ProductionSalesOrderOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = searchText?.Trim() ?? string.Empty;
        var rows = await connection.QueryAsync<ProductionSalesOrderOption>(
            new CommandDefinition("""
                SELECT O.ID AS Id, O.CODE AS Code, C.NAME AS CustomerName,
                       O.STATUS_CODE AS StatusCode, O.ORDER_DATE AS OrderDate
                FROM SALES_ORDERS O
                JOIN CUSTOMERS C ON C.ID = O.CUSTOMER_ID
                WHERE O.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND O.BRANCH_ID = @BRANCH_ID
                  AND O.ORDER_TYPE = 'CUSTOM'
                  AND O.STATUS_CODE IN ('DEPOSITED', 'CONFIRMED')
                  AND NOT EXISTS (
                      SELECT 1 FROM PRODUCTION_ORDERS PO
                      WHERE PO.SALES_ORDER_ID = O.ID)
                  AND (@HAS_SEARCH = 0
                       OR O.CODE CONTAINING @SEARCH
                       OR C.NAME CONTAINING @SEARCH)
                ORDER BY O.ORDER_DATE DESC, O.ID DESC
                ROWS 100
                """,
                new
                {
                    BRANCH_ID = branchId,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductionEmployeeOption>> SearchWorkingEmployeesAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = searchText?.Trim() ?? string.Empty;
        var rows = await connection.QueryAsync<ProductionEmployeeOption>(
            new CommandDefinition("""
                SELECT E.ID AS Id, E.CODE AS Code, E.FULL_NAME AS FullName
                FROM HR_EMPLOYEES E
                WHERE E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND E.STATUS_CODE = 'WORKING'
                  AND E.IS_ACTIVE = TRUE
                  AND (@HAS_SEARCH = 0
                       OR E.CODE CONTAINING @SEARCH
                       OR E.FULL_NAME CONTAINING @SEARCH)
                ORDER BY E.FULL_NAME, E.CODE
                ROWS 200
                """,
                new
                {
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductionExternalWorkerOption>> SearchActiveExternalWorkersAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = searchText?.Trim() ?? string.Empty;
        var rows = await connection.QueryAsync<ProductionExternalWorkerOption>(
            new CommandDefinition("""
                SELECT W.ID AS Id, W.CODE AS Code, W.NAME AS Name
                FROM EXTERNAL_WORKERS W
                WHERE W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND W.IS_ACTIVE = TRUE
                  AND (@HAS_SEARCH = 0
                       OR W.CODE CONTAINING @SEARCH
                       OR W.NAME CONTAINING @SEARCH)
                ORDER BY W.NAME, W.CODE
                ROWS 200
                """,
                new
                {
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductionLocationOption>> GetLocationsAsync(
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return Array.Empty<ProductionLocationOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ProductionLocationOption>(
            new CommandDefinition("""
                SELECT L.ID AS Id, L.CODE AS Code, L.NAME AS Name, L.INVENTORY_SCOPE AS InventoryScope
                FROM LOCATIONS L
                WHERE L.BRANCH_ID = @BRANCH_ID
                  AND L.IS_ACTIVE = TRUE
                ORDER BY L.NAME, L.CODE
                """,
                new { BRANCH_ID = branchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ProductionMaterialStockOption>> SearchMaterialStockAsync(
        long branchId,
        long locationId,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0 || locationId <= 0)
            return Array.Empty<ProductionMaterialStockOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = searchText?.Trim() ?? string.Empty;
        var rows = await connection.QueryAsync<ProductionMaterialStockOption>(
            new CommandDefinition("""
                SELECT M.ID AS MaterialId, M.CODE AS Code, M.NAME AS Name,
                       M.MATERIAL_TYPE AS MaterialType, M.UNIT_CODE AS UnitCode,
                       M.PURITY_CODE AS PurityCode,
                       B.QUANTITY AS QuantityOnHand, COALESCE(B.AVERAGE_COST, 0) AS AverageCost,
                       B.LOCATION_ID AS LocationId
                FROM STOCK_BALANCES B
                JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
                JOIN LOCATIONS L ON L.ID = B.LOCATION_ID
                WHERE M.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND L.BRANCH_ID = @BRANCH_ID
                  AND B.LOCATION_ID = @LOCATION_ID
                  AND B.QUANTITY > 0
                  AND (@HAS_SEARCH = 0
                       OR M.CODE CONTAINING @SEARCH
                       OR M.NAME CONTAINING @SEARCH)
                ORDER BY M.NAME, M.CODE
                ROWS 200
                """,
                new
                {
                    BRANCH_ID = branchId,
                    LOCATION_ID = locationId,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<IReadOnlyList<ProductionOrderLineDto>> LoadLinesAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ProductionOrderLineDto>(
            new CommandDefinition("""
                SELECT ID AS Id, LINE_NUMBER AS LineNumber, MODEL_CODE AS ModelCode,
                       MODEL_NAME AS ModelName, GOLD_TYPE_CODE AS GoldTypeCode,
                       GOLD_TYPE_NAME AS GoldTypeName,
                       PLANNED_QUANTITY AS PlannedQuantity, LABOR_COST_PER_UNIT AS LaborCostPerUnit,
                       COALESCE(ESTIMATED_WEIGHT_CHI, 0) AS EstimatedWeightChi,
                       COALESCE(GOLD_UNIT_PRICE_PER_CHI, 0) AS GoldUnitPricePerChi,
                       COALESCE(STONE_AMOUNT_PER_UNIT_VND, 0) AS StoneAmountPerUnitVnd,
                       DESCRIPTION AS Description, NOTES AS Notes
                FROM PRODUCTION_ORDER_LINES
                WHERE PRODUCTION_ORDER_ID = @ID
                ORDER BY LINE_NUMBER
                """,
                new { ID = orderId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<IReadOnlyList<ProductionOrderWorkerDto>> LoadWorkersAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ProductionOrderWorkerDto>(
            new CommandDefinition("""
                SELECT E.ID AS EmployeeId, E.CODE AS EmployeeCode, E.FULL_NAME AS FullName
                FROM PRODUCTION_ORDER_WORKERS POW
                JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
                WHERE POW.PRODUCTION_ORDER_ID = @ID
                ORDER BY E.FULL_NAME
                """,
                new { ID = orderId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<IReadOnlyList<ProductionAttachmentDto>> LoadAttachmentsAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<ProductionAttachmentDto>(
            new CommandDefinition("""
                SELECT ID AS Id, ATTACHMENT_KIND AS AttachmentKind, STORED_PATH AS StoredPath,
                       ORIGINAL_FILE_NAME AS OriginalFileName, MIME_TYPE AS MimeType,
                       CAPTION AS Caption, DISPLAY_ORDER AS DisplayOrder, LINE_NUMBER AS LineNumber
                FROM PRODUCTION_ATTACHMENTS
                WHERE PRODUCTION_ORDER_ID = @ID
                ORDER BY COALESCE(LINE_NUMBER, 0), DISPLAY_ORDER, ID
                """,
                new { ID = orderId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    private static async Task<IReadOnlyList<ProductionMaterialBalanceDto>> LoadMaterialBalancesAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await connection.QueryAsync<ProductionMaterialBalanceDto>(
                new CommandDefinition("""
                    SELECT B.ID AS Id, B.ISSUE_DOCUMENT_ID AS IssueDocumentId,
                           B.ISSUE_LINE_ID AS IssueLineId, B.MATERIAL_ID AS MaterialId,
                           M.CODE AS MaterialCode, M.NAME AS MaterialName,
                           M.MATERIAL_TYPE AS MaterialType, B.UNIT_CODE AS UnitCode,
                           B.QUANTITY_ISSUED AS QuantityIssued,
                           B.QUANTITY_IN_PRODUCT AS QuantityInProduct,
                           B.QUANTITY_RETURNED AS QuantityReturned,
                           B.QUANTITY_SCRAP AS QuantityScrap,
                           B.QUANTITY_LOSS AS QuantityLoss,
                           B.UNIT_COST_SNAPSHOT AS UnitCostSnapshot,
                           B.WAREHOUSE_GOLD_PRICE_ID AS WarehouseGoldPriceId,
                           B.WAREHOUSE_GOLD_PRICE_PER_CHI AS WarehouseGoldPricePerChi
                    FROM PRODUCTION_MATERIAL_BALANCES B
                    JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
                    WHERE B.PRODUCTION_ORDER_ID = @ID
                    ORDER BY B.ID
                    """,
                    new { ID = orderId },
                    cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (FbException)
        {
            // Migration 46 not applied yet — return empty balances.
            return Array.Empty<ProductionMaterialBalanceDto>();
        }
    }

    public async Task<IReadOnlyList<ProductionUnitComponentDto>> GetQcUnitComponentsAsync(
        long qcUnitId,
        CancellationToken cancellationToken = default)
    {
        if (qcUnitId <= 0)
            return Array.Empty<ProductionUnitComponentDto>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            var rows = await connection.QueryAsync<ProductionUnitComponentDto>(
                new CommandDefinition("""
                    SELECT ID AS Id, QC_UNIT_ID AS QcUnitId,
                           MATERIAL_BALANCE_ID AS MaterialBalanceId, MATERIAL_ID AS MaterialId,
                           COMPONENT_KIND AS ComponentKind,
                           QUANTITY_USED AS QuantityUsed, UNIT_CODE AS UnitCode,
                           UNIT_COST_SNAPSHOT AS UnitCostSnapshot, AMOUNT_VND AS AmountVnd,
                           WAREHOUSE_GOLD_PRICE_ID AS WarehouseGoldPriceId,
                           GOLD_PRICE_PER_CHI_SNAPSHOT AS GoldPricePerChiSnapshot
                    FROM PRODUCTION_UNIT_COMPONENTS
                    WHERE QC_UNIT_ID = @ID
                    ORDER BY ID
                    """,
                    new { ID = qcUnitId },
                    cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (FbException)
        {
            return Array.Empty<ProductionUnitComponentDto>();
        }
    }

    public async Task<IReadOnlyList<ProductionMaterialBalanceAllocationDto>> GetMaterialBalanceAllocationsAsync(
        long productionOrderId,
        long? excludeQcUnitId = null,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return Array.Empty<ProductionMaterialBalanceAllocationDto>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            var rows = await connection.QueryAsync<ProductionMaterialBalanceAllocationRow>(
                new CommandDefinition("""
                    SELECT B.ID AS MaterialBalanceId,
                           B.QUANTITY_IN_PRODUCT AS QuantityInProduct,
                           COALESCE((
                               SELECT SUM(C.QUANTITY_USED)
                               FROM PRODUCTION_UNIT_COMPONENTS C
                               JOIN PRODUCTION_QC_UNITS U ON U.ID = C.QC_UNIT_ID
                               WHERE C.MATERIAL_BALANCE_ID = B.ID
                                 AND U.STATUS_CODE = 'PASSED'
                                 AND (@EXCLUDE = 0 OR U.ID <> @EXCLUDE)), 0) AS QuantityUsedByPassed
                    FROM PRODUCTION_MATERIAL_BALANCES B
                    WHERE B.PRODUCTION_ORDER_ID = @PO
                    ORDER BY B.ID
                    """,
                    new
                    {
                        PO = productionOrderId,
                        EXCLUDE = excludeQcUnitId ?? 0
                    },
                    cancellationToken: cancellationToken));
            return rows
                .Select(r => new ProductionMaterialBalanceAllocationDto(
                    r.MaterialBalanceId,
                    r.QuantityInProduct,
                    r.QuantityUsedByPassed,
                    r.QuantityInProduct - r.QuantityUsedByPassed))
                .ToList();
        }
        catch (FbException)
        {
            return Array.Empty<ProductionMaterialBalanceAllocationDto>();
        }
    }

    public async Task<decimal> GetIssuedGoldWeightAsync(
        long productionOrderId,
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0 || branchId <= 0)
            return 0;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            // Vàng đã giao = tổng QuantityIssued của các material vàng (MATERIAL_TYPE = 'GOLD')
            // trong PRODUCTION_MATERIAL_BALANCES của lệnh. Đơn vị chuẩn của vàng là chỉ.
            return await connection.QuerySingleAsync<decimal>(
                new CommandDefinition("""
                    SELECT COALESCE(SUM(B.QUANTITY_ISSUED), 0)
                    FROM PRODUCTION_MATERIAL_BALANCES B
                    JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
                    JOIN PRODUCTION_ORDERS PO ON PO.ID = B.PRODUCTION_ORDER_ID
                    WHERE B.PRODUCTION_ORDER_ID = @PO
                      AND PO.BRANCH_ID = @BID
                      AND PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND M.MATERIAL_TYPE = 'GOLD'
                    """,
                    new { PO = productionOrderId, BID = branchId },
                    cancellationToken: cancellationToken));
        }
        catch (FbException)
        {
            // Migration 46 not applied yet — no material balances.
            return 0;
        }
    }

    public async Task<IReadOnlyList<IssuedGoldByPurityDto>> GetIssuedGoldByPurityAsync(
        long productionOrderId,
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0 || branchId <= 0)
            return Array.Empty<IssuedGoldByPurityDto>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            // Vàng đã giao theo từng loại (PURITY_CODE), từ PRODUCTION_MATERIAL_BALANCES.
            // Đơn vị chuẩn của vàng là chỉ. Kèm hao hụt thợ đã báo ở bước trả hàng
            // (QUANTITY_LOSS) để dialog nhập kho prefill, tránh nhập lại 2 số khác nhau.
            var rows = await connection.QueryAsync<IssuedGoldByPurityDto>(
                new CommandDefinition("""
                    SELECT M.PURITY_CODE AS PurityCode, COALESCE(SUM(B.QUANTITY_ISSUED-B.QUANTITY_RETURNED-B.QUANTITY_SCRAP), 0) AS WeightChi,
                           COALESCE(SUM(B.QUANTITY_LOSS), 0) AS ReportedLossChi
                    FROM PRODUCTION_MATERIAL_BALANCES B
                    JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
                    JOIN PRODUCTION_ORDERS PO ON PO.ID = B.PRODUCTION_ORDER_ID
                    WHERE B.PRODUCTION_ORDER_ID = @PO
                      AND PO.BRANCH_ID = @BID
                      AND PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND M.MATERIAL_TYPE = 'GOLD'
                    GROUP BY M.PURITY_CODE
                    """,
                    new { PO = productionOrderId, BID = branchId },
                    cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (FbException ex) { throw new InvalidOperationException("Không tải được dữ liệu sản xuất. Hãy thử lại; chưa thể đối chiếu vàng.",ex); }
    }

    public async Task<IReadOnlyList<IssuedStoneByMaterialDto>> GetIssuedStoneByMaterialAsync(
        long productionOrderId,
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0 || branchId <= 0)
            return Array.Empty<IssuedStoneByMaterialDto>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            // Đá đã giao theo từng loại NVL (MATERIAL_TYPE = 'STONE').
            // Chỉ hiển thị tham khảo trong dialog đối chiếu vàng.
            var rows = await connection.QueryAsync<IssuedStoneByMaterialDto>(
                new CommandDefinition("""
                    SELECT M.CODE AS MaterialCode, M.NAME AS MaterialName,
                           COALESCE(SUM(B.QUANTITY_ISSUED), 0) AS QuantityIssued,
                           MAX(B.UNIT_CODE) AS UnitCode
                    FROM PRODUCTION_MATERIAL_BALANCES B
                    JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
                    JOIN PRODUCTION_ORDERS PO ON PO.ID = B.PRODUCTION_ORDER_ID
                    WHERE B.PRODUCTION_ORDER_ID = @PO
                      AND PO.BRANCH_ID = @BID
                      AND PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND M.MATERIAL_TYPE = 'STONE'
                    GROUP BY M.CODE, M.NAME
                    """,
                    new { PO = productionOrderId, BID = branchId },
                    cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (FbException ex) { throw new InvalidOperationException("Không tải được dữ liệu sản xuất. Hãy thử lại; chưa thể đối chiếu vàng.",ex); }
    }

    private static async Task<IReadOnlyList<ProductionQcUnitDto>> LoadQcUnitsAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        try
        {
            var rows = await connection.QueryAsync<ProductionQcUnitDto>(
                new CommandDefinition("""
                    SELECT U.ID AS Id, U.ORDER_LINE_ID AS OrderLineId, U.UNIT_NUMBER AS UnitNumber,
                           U.STATUS_CODE AS StatusCode, L.MODEL_NAME AS ModelName,
                           U.ACTUAL_GOLD_WEIGHT AS ActualGoldWeight,
                           U.ACTUAL_WEIGHT_UNIT_CODE AS ActualWeightUnitCode,
                           U.GOLD_PURITY_CODE AS GoldPurityCode,
                           U.LABOR_COST_SNAPSHOT AS LaborCostSnapshot,
                           U.DEFECT_NOTES AS DefectNotes, U.LAST_QC_AT AS LastQcAt,
                           U.FINISHED_PRODUCT_ID AS FinishedProductId, U.ROW_VERSION AS RowVersion
                    FROM PRODUCTION_QC_UNITS U
                    JOIN PRODUCTION_ORDER_LINES L ON L.ID = U.ORDER_LINE_ID
                    WHERE U.PRODUCTION_ORDER_ID = @ID
                    ORDER BY L.LINE_NUMBER, U.UNIT_NUMBER
                    """,
                    new { ID = orderId },
                    cancellationToken: cancellationToken));
            return rows.AsList();
        }
        catch (FbException)
        {
            return Array.Empty<ProductionQcUnitDto>();
        }
    }
}
