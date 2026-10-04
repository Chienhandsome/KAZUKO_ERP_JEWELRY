using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;
namespace Kazuko.Infrastructure.Production;

public sealed class FirebirdProductionQueryRepository : IProductionQueryRepository
{
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
        // Period filter uses ORDER_DATE (always present). COMPLETED_AT is preferred when
        // migration 45+ has been applied; COALESCE keeps summary working on legacy headers.
        await using var command = new FbCommand("""
            SELECT
                SUM(CASE WHEN PO.STATUS_CODE = 'IN_PROGRESS' THEN 1 ELSE 0 END),
                SUM(CASE WHEN PO.STATUS_CODE = 'COMPLETED'
                          AND (@HAS_PERIOD = 0
                               OR (PO.ORDER_DATE >= @FROM_DATE AND PO.ORDER_DATE < @TO_DATE_EXCL))
                     THEN 1 ELSE 0 END),
                SUM(CASE WHEN PO.STATUS_CODE = 'IN_PROGRESS' AND PO.EXECUTION_TYPE = 'OUTSOURCED' THEN 1 ELSE 0 END),
                SUM(CASE WHEN PO.STATUS_CODE = 'IN_PROGRESS' AND PO.EXECUTION_TYPE = 'IN_HOUSE' THEN 1 ELSE 0 END)
            FROM PRODUCTION_ORDERS PO
            WHERE PO.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND PO.BRANCH_ID = @BRANCH_ID
            """, connection);

        var hasPeriod = periodFrom is not null && periodTo is not null ? 1 : 0;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@HAS_PERIOD", FbDbType.Integer).Value = hasPeriod;
        command.Parameters.Add("@FROM_DATE", FbDbType.TimeStamp).Value =
            periodFrom ?? (object)DBNull.Value;
        command.Parameters.Add("@TO_DATE_EXCL", FbDbType.TimeStamp).Value =
            periodTo?.Date.AddDays(1) ?? (object)DBNull.Value;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new ProductionOrderSummary(
            reader.IsDBNull(0) ? 0 : Convert.ToInt32(reader.GetValue(0)),
            reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
            reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
            reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)));
    }

    public async Task<IReadOnlyList<ProductionOrderListItem>> SearchOrdersAsync(
        long branchId,
        ProductionOrderListFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (branchId <= 0)
            throw new InvalidOperationException("Thiếu chi nhánh.");

        var result = new List<ProductionOrderListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT PO.ID, PO.CODE, PO.ORDER_SOURCE, PO.EXECUTION_TYPE, PO.STATUS_CODE, PO.ORDER_DATE,
                   SO.CODE,
                   CASE
                       WHEN PO.EXECUTION_TYPE = 'OUTSOURCED' THEN EW.NAME
                       ELSE (
                           SELECT LIST(TRIM(E.FULL_NAME), ', ')
                           FROM PRODUCTION_ORDER_WORKERS POW
                           JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
                           WHERE POW.PRODUCTION_ORDER_ID = PO.ID)
                   END,
                   COALESCE((
                       SELECT SUBSTRING(LIST(TRIM(POL.MODEL_NAME), '; ') FROM 1 FOR 200)
                       FROM PRODUCTION_ORDER_LINES POL
                       WHERE POL.PRODUCTION_ORDER_ID = PO.ID
                   ), ''),
                   COALESCE((
                       SELECT SUM(POL.PLANNED_QUANTITY)
                       FROM PRODUCTION_ORDER_LINES POL
                       WHERE POL.PRODUCTION_ORDER_ID = PO.ID
                   ), 0),
                   PO.ROW_VERSION
            FROM PRODUCTION_ORDERS PO
            LEFT JOIN SALES_ORDERS SO ON SO.ID = PO.SALES_ORDER_ID
            LEFT JOIN EXTERNAL_WORKERS EW ON EW.COMPANY_ID = PO.COMPANY_ID AND EW.ID = PO.EXTERNAL_WORKER_ID
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
            ORDER BY PO.ORDER_DATE DESC, PO.ID DESC
            """, connection);

        var search = filter.SearchText?.Trim() ?? string.Empty;
        var status = filter.StatusCode?.Trim() ?? string.Empty;
        var source = filter.OrderSource?.Trim() ?? string.Empty;
        var execution = filter.ExecutionType?.Trim() ?? string.Empty;

        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@INCLUDE_DRAFTS", FbDbType.Integer).Value = filter.IncludeDrafts ? 1 : 0;
        command.Parameters.Add("@HAS_STATUS", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS_CODE", FbDbType.VarChar, 20).Value = status;
        command.Parameters.Add("@HAS_SOURCE", FbDbType.Integer).Value = source.Length > 0 ? 1 : 0;
        command.Parameters.Add("@ORDER_SOURCE", FbDbType.VarChar, 20).Value = source;
        command.Parameters.Add("@HAS_EXEC", FbDbType.Integer).Value = execution.Length > 0 ? 1 : 0;
        command.Parameters.Add("@EXECUTION_TYPE", FbDbType.VarChar, 20).Value = execution;
        command.Parameters.Add("@HAS_FROM", FbDbType.Integer).Value = filter.FromDate is not null ? 1 : 0;
        command.Parameters.Add("@FROM_DATE", FbDbType.TimeStamp).Value = filter.FromDate ?? (object)DBNull.Value;
        command.Parameters.Add("@HAS_TO", FbDbType.Integer).Value = filter.ToDate is not null ? 1 : 0;
        command.Parameters.Add("@TO_DATE_EXCL", FbDbType.TimeStamp).Value =
            filter.ToDate?.Date.AddDays(1) ?? (object)DBNull.Value;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionOrderListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8),
                Convert.ToInt32(reader.GetValue(9)),
                reader.GetInt64(10)));
        }

        return result;
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

        await using var headerCmd = new FbCommand("""
            SELECT PO.ID, PO.CODE, PO.ORDER_SOURCE, PO.SALES_ORDER_ID, SO.CODE,
                   PO.EXECUTION_TYPE, PO.EXTERNAL_WORKER_ID, EW.NAME,
                   PO.STATUS_CODE, PO.ORDER_DATE,
                   PO.SOURCE_LOCATION_ID, SL.NAME,
                   PO.FINISHED_LOCATION_ID, FL.NAME,
                   PO.MATERIAL_ISSUE_DOCUMENT_ID, MI.CODE,
                   PO.RETURN_DOCUMENT_ID, RR.CODE,
                   PO.FINISHED_RECEIPT_DOCUMENT_ID, FR.CODE,
                   PO.DELIVERED_BY_NAME, PO.RECEIVED_BY_NAME, PO.NOTES, PO.ROW_VERSION
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
            """, connection);
        headerCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        headerCmd.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;

        await using var headerReader = await headerCmd.ExecuteReaderAsync(cancellationToken);
        if (!await headerReader.ReadAsync(cancellationToken))
            return null;

        var detail = new ProductionOrderDetail(
            headerReader.GetInt64(0),
            headerReader.GetString(1),
            headerReader.GetString(2),
            headerReader.IsDBNull(3) ? null : headerReader.GetInt64(3),
            headerReader.IsDBNull(4) ? null : headerReader.GetString(4),
            headerReader.GetString(5),
            headerReader.IsDBNull(6) ? null : headerReader.GetInt64(6),
            headerReader.IsDBNull(7) ? null : headerReader.GetString(7),
            headerReader.GetString(8),
            headerReader.GetDateTime(9),
            headerReader.GetInt64(10),
            headerReader.GetString(11),
            headerReader.GetInt64(12),
            headerReader.GetString(13),
            headerReader.IsDBNull(14) ? null : headerReader.GetInt64(14),
            headerReader.IsDBNull(15) ? null : headerReader.GetString(15),
            headerReader.IsDBNull(16) ? null : headerReader.GetInt64(16),
            headerReader.IsDBNull(17) ? null : headerReader.GetString(17),
            headerReader.IsDBNull(18) ? null : headerReader.GetInt64(18),
            headerReader.IsDBNull(19) ? null : headerReader.GetString(19),
            headerReader.IsDBNull(20) ? null : headerReader.GetString(20),
            headerReader.IsDBNull(21) ? null : headerReader.GetString(21),
            headerReader.IsDBNull(22) ? null : headerReader.GetString(22),
            headerReader.GetInt64(23),
            await LoadLinesAsync(connection, orderId, cancellationToken),
            await LoadWorkersAsync(connection, orderId, cancellationToken),
            await LoadAttachmentsAsync(connection, orderId, cancellationToken),
            await LoadMaterialBalancesAsync(connection, orderId, cancellationToken),
            await LoadQcUnitsAsync(connection, orderId, cancellationToken));

        return detail;
    }

    public async Task<IReadOnlyList<ProductionSalesOrderOption>> SearchEligibleSalesOrdersAsync(
        long branchId,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return Array.Empty<ProductionSalesOrderOption>();

        var result = new List<ProductionSalesOrderOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT O.ID, O.CODE, C.NAME, O.STATUS_CODE, O.ORDER_DATE
            FROM SALES_ORDERS O
            JOIN CUSTOMERS C ON C.ID = O.CUSTOMER_ID
            WHERE O.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND O.BRANCH_ID = @BRANCH_ID
              AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
              AND NOT EXISTS (
                  SELECT 1 FROM PRODUCTION_ORDERS PO
                  WHERE PO.SALES_ORDER_ID = O.ID)
              AND (@HAS_SEARCH = 0
                   OR O.CODE CONTAINING @SEARCH
                   OR C.NAME CONTAINING @SEARCH)
            ORDER BY O.ORDER_DATE DESC, O.ID DESC
            ROWS 100
            """, connection);

        var search = searchText?.Trim() ?? string.Empty;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionSalesOrderOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ProductionEmployeeOption>> SearchWorkingEmployeesAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ProductionEmployeeOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT E.ID, E.CODE, E.FULL_NAME
            FROM HR_EMPLOYEES E
            WHERE E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND E.STATUS_CODE = 'WORKING'
              AND E.IS_ACTIVE = TRUE
              AND (@HAS_SEARCH = 0
                   OR E.CODE CONTAINING @SEARCH
                   OR E.FULL_NAME CONTAINING @SEARCH)
            ORDER BY E.FULL_NAME, E.CODE
            ROWS 200
            """, connection);

        var search = searchText?.Trim() ?? string.Empty;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionEmployeeOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ProductionExternalWorkerOption>> SearchActiveExternalWorkersAsync(
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        var result = new List<ProductionExternalWorkerOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT W.ID, W.CODE, W.NAME
            FROM EXTERNAL_WORKERS W
            WHERE W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND W.IS_ACTIVE = TRUE
              AND (@HAS_SEARCH = 0
                   OR W.CODE CONTAINING @SEARCH
                   OR W.NAME CONTAINING @SEARCH)
            ORDER BY W.NAME, W.CODE
            ROWS 200
            """, connection);

        var search = searchText?.Trim() ?? string.Empty;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionExternalWorkerOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ProductionLocationOption>> GetLocationsAsync(
        long branchId,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0)
            return Array.Empty<ProductionLocationOption>();

        var result = new List<ProductionLocationOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT L.ID, L.CODE, L.NAME, L.INVENTORY_SCOPE
            FROM LOCATIONS L
            WHERE L.BRANCH_ID = @BRANCH_ID
              AND L.IS_ACTIVE = TRUE
            ORDER BY L.NAME, L.CODE
            """, connection);
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionLocationOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }

    public async Task<IReadOnlyList<ProductionMaterialStockOption>> SearchMaterialStockAsync(
        long branchId,
        long locationId,
        string? searchText,
        CancellationToken cancellationToken = default)
    {
        if (branchId <= 0 || locationId <= 0)
            return Array.Empty<ProductionMaterialStockOption>();

        var result = new List<ProductionMaterialStockOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT M.ID, M.CODE, M.NAME, M.MATERIAL_TYPE, M.UNIT_CODE, M.PURITY_CODE,
                   B.QUANTITY, COALESCE(B.AVERAGE_COST, 0), B.LOCATION_ID
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
            """, connection);

        var search = searchText?.Trim() ?? string.Empty;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@LOCATION_ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ProductionMaterialStockOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetInt64(8)));
        }

        return result;
    }

    private static async Task<IReadOnlyList<ProductionOrderLineDto>> LoadLinesAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var lines = new List<ProductionOrderLineDto>();
        await using var command = new FbCommand("""
            SELECT ID, LINE_NUMBER, MODEL_CODE, MODEL_NAME, GOLD_TYPE_CODE, GOLD_TYPE_NAME,
                   PLANNED_QUANTITY, LABOR_COST_PER_UNIT, DESCRIPTION, NOTES
            FROM PRODUCTION_ORDER_LINES
            WHERE PRODUCTION_ORDER_ID = @ID
            ORDER BY LINE_NUMBER
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ProductionOrderLineDto(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetDecimal(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return lines;
    }

    private static async Task<IReadOnlyList<ProductionOrderWorkerDto>> LoadWorkersAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var workers = new List<ProductionOrderWorkerDto>();
        await using var command = new FbCommand("""
            SELECT E.ID, E.CODE, E.FULL_NAME
            FROM PRODUCTION_ORDER_WORKERS POW
            JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
            WHERE POW.PRODUCTION_ORDER_ID = @ID
            ORDER BY E.FULL_NAME
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            workers.Add(new ProductionOrderWorkerDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return workers;
    }

    private static async Task<IReadOnlyList<ProductionAttachmentDto>> LoadAttachmentsAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var attachments = new List<ProductionAttachmentDto>();
        await using var command = new FbCommand("""
            SELECT ID, ATTACHMENT_KIND, STORED_PATH, ORIGINAL_FILE_NAME, MIME_TYPE, CAPTION, DISPLAY_ORDER
            FROM PRODUCTION_ATTACHMENTS
            WHERE PRODUCTION_ORDER_ID = @ID
            ORDER BY DISPLAY_ORDER, ID
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            attachments.Add(new ProductionAttachmentDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6)));
        }

        return attachments;
    }

    private static async Task<IReadOnlyList<ProductionMaterialBalanceDto>> LoadMaterialBalancesAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var items = new List<ProductionMaterialBalanceDto>();
        await using var command = new FbCommand("""
            SELECT B.ID, B.ISSUE_DOCUMENT_ID, B.ISSUE_LINE_ID, B.MATERIAL_ID,
                   M.CODE, M.NAME, M.MATERIAL_TYPE, B.UNIT_CODE,
                   B.QUANTITY_ISSUED, B.QUANTITY_IN_PRODUCT, B.QUANTITY_RETURNED,
                   B.QUANTITY_SCRAP, B.QUANTITY_LOSS, B.UNIT_COST_SNAPSHOT,
                   B.WAREHOUSE_GOLD_PRICE_ID, B.WAREHOUSE_GOLD_PRICE_PER_CHI
            FROM PRODUCTION_MATERIAL_BALANCES B
            JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
            WHERE B.PRODUCTION_ORDER_ID = @ID
            ORDER BY B.ID
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ProductionMaterialBalanceDto(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
                    reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13),
                    reader.IsDBNull(14) ? null : reader.GetInt64(14),
                    reader.IsDBNull(15) ? null : reader.GetDecimal(15)));
            }
        }
        catch (FbException)
        {
            // Migration 46 not applied yet — return empty balances.
            return Array.Empty<ProductionMaterialBalanceDto>();
        }

        return items;
    }

    private static async Task<IReadOnlyList<ProductionQcUnitDto>> LoadQcUnitsAsync(
        FbConnection connection,
        long orderId,
        CancellationToken cancellationToken)
    {
        var items = new List<ProductionQcUnitDto>();
        await using var command = new FbCommand("""
            SELECT U.ID, U.ORDER_LINE_ID, U.UNIT_NUMBER, U.STATUS_CODE, L.MODEL_NAME,
                   U.ACTUAL_GOLD_WEIGHT, U.ACTUAL_WEIGHT_UNIT_CODE, U.GOLD_PURITY_CODE,
                   U.LABOR_COST_SNAPSHOT, U.DEFECT_NOTES, U.LAST_QC_AT,
                   U.FINISHED_PRODUCT_ID, U.ROW_VERSION
            FROM PRODUCTION_QC_UNITS U
            JOIN PRODUCTION_ORDER_LINES L ON L.ID = U.ORDER_LINE_ID
            WHERE U.PRODUCTION_ORDER_ID = @ID
            ORDER BY L.LINE_NUMBER, U.UNIT_NUMBER
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new ProductionQcUnitDto(
                    reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.GetDecimal(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                    reader.IsDBNull(11) ? null : reader.GetInt64(11),
                    reader.GetInt64(12)));
            }
        }
        catch (FbException)
        {
            return Array.Empty<ProductionQcUnitDto>();
        }

        return items;
    }
}
