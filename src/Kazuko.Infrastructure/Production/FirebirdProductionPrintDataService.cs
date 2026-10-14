using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Production;

public sealed class FirebirdProductionPrintDataService : IProductionPrintDataService
{
    public async Task<ProductionMaterialIssuePrintDto?> GetMaterialIssuePrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var header = new FbCommand("""
            SELECT PO.CODE, D.DOCUMENT_DATE, B.NAME, D.DELIVERED_BY_NAME, D.RECEIVED_BY_NAME, D.CODE
            FROM PRODUCTION_ORDERS PO
            JOIN STOCK_DOCUMENTS D ON D.ID = PO.MATERIAL_ISSUE_DOCUMENT_ID
            JOIN BRANCHES B ON B.ID = PO.BRANCH_ID
            WHERE PO.ID = @ID
            """, connection);
        header.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await header.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var dtoHeader = (
            OrderCode: reader.GetString(0),
            DocumentDate: reader.GetDateTime(1),
            BranchName: reader.GetString(2),
            DeliveredBy: reader.IsDBNull(3) ? null : reader.GetString(3),
            ReceivedBy: reader.IsDBNull(4) ? null : reader.GetString(4),
            DocumentCode: reader.GetString(5));
        await reader.DisposeAsync();

        var lines = new List<ProductionMaterialIssuePrintLine>();
        await using var lineCmd = new FbCommand("""
            SELECT L.LINE_NUMBER, M.CODE, M.NAME, L.QUANTITY, COALESCE(L.STANDARD_UNIT_CODE, M.UNIT_CODE),
                   L.UNIT_COST, L.NOTES
            FROM STOCK_DOCUMENT_LINES L
            JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
            JOIN PRODUCTION_ORDERS PO ON PO.MATERIAL_ISSUE_DOCUMENT_ID = L.DOCUMENT_ID
            WHERE PO.ID = @ID
            ORDER BY L.LINE_NUMBER
            """, connection);
        lineCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken);
        while (await lineReader.ReadAsync(cancellationToken))
        {
            lines.Add(new ProductionMaterialIssuePrintLine(
                lineReader.GetInt32(0),
                lineReader.GetString(1),
                lineReader.GetString(2),
                lineReader.GetDecimal(3),
                lineReader.GetString(4),
                lineReader.GetDecimal(5),
                lineReader.IsDBNull(6) ? null : lineReader.GetString(6)));
        }

        return new ProductionMaterialIssuePrintDto(
            dtoHeader.OrderCode, dtoHeader.DocumentDate, dtoHeader.BranchName,
            dtoHeader.DeliveredBy, dtoHeader.ReceivedBy, dtoHeader.DocumentCode, lines);
    }

    public async Task<ProductionReturnPrintDto?> GetReturnPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var header = new FbCommand("""
            SELECT PO.CODE, D.DOCUMENT_DATE, B.NAME, D.DELIVERED_BY_NAME, D.RECEIVED_BY_NAME, D.CODE
            FROM PRODUCTION_ORDERS PO
            JOIN STOCK_DOCUMENTS D ON D.ID = PO.RETURN_DOCUMENT_ID
            JOIN BRANCHES B ON B.ID = PO.BRANCH_ID
            WHERE PO.ID = @ID
            """, connection);
        header.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await header.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var dtoHeader = (
            OrderCode: reader.GetString(0),
            DocumentDate: reader.GetDateTime(1),
            BranchName: reader.GetString(2),
            DeliveredBy: reader.IsDBNull(3) ? null : reader.GetString(3),
            ReceivedBy: reader.IsDBNull(4) ? null : reader.GetString(4),
            DocumentCode: reader.GetString(5));
        await reader.DisposeAsync();

        var lines = await LoadBalancePrintLinesAsync(connection, productionOrderId, cancellationToken);
        return new ProductionReturnPrintDto(
            dtoHeader.OrderCode, dtoHeader.DocumentDate, dtoHeader.BranchName,
            dtoHeader.DeliveredBy, dtoHeader.ReceivedBy, dtoHeader.DocumentCode, lines);
    }

    public async Task<ProductionQcPrintDto?> GetQcPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var header = new FbCommand("""
            SELECT PO.CODE, B.NAME
            FROM PRODUCTION_ORDERS PO
            JOIN BRANCHES B ON B.ID = PO.BRANCH_ID
            WHERE PO.ID = @ID
            """, connection);
        header.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await header.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var orderCode = reader.GetString(0);
        var branchName = reader.GetString(1);
        await reader.DisposeAsync();

        var units = new List<ProductionQcPrintUnit>();
        await using var unitCmd = new FbCommand("""
            SELECT L.MODEL_NAME, U.UNIT_NUMBER, U.STATUS_CODE, U.ACTUAL_GOLD_WEIGHT,
                   U.ACTUAL_WEIGHT_UNIT_CODE, U.GOLD_PURITY_CODE, U.LABOR_COST_SNAPSHOT, U.DEFECT_NOTES
            FROM PRODUCTION_QC_UNITS U
            JOIN PRODUCTION_ORDER_LINES L ON L.ID = U.ORDER_LINE_ID
            WHERE U.PRODUCTION_ORDER_ID = @ID
            ORDER BY L.LINE_NUMBER, U.UNIT_NUMBER
            """, connection);
        unitCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var unitReader = await unitCmd.ExecuteReaderAsync(cancellationToken);
        while (await unitReader.ReadAsync(cancellationToken))
        {
            units.Add(new ProductionQcPrintUnit(
                unitReader.GetString(0),
                unitReader.GetInt32(1),
                ProductionQcUnitStatuses.ToDisplay(unitReader.GetString(2)),
                unitReader.IsDBNull(3) ? null : unitReader.GetDecimal(3),
                unitReader.IsDBNull(4) ? null : unitReader.GetString(4),
                unitReader.IsDBNull(5) ? null : unitReader.GetString(5),
                unitReader.GetDecimal(6),
                unitReader.IsDBNull(7) ? null : unitReader.GetString(7)));
        }

        return new ProductionQcPrintDto(orderCode, DateTime.Now, branchName, units);
    }

    public async Task<ProductionFinishedReceiptPrintDto?> GetFinishedReceiptPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var header = new FbCommand("""
            SELECT PO.CODE, D.DOCUMENT_DATE, B.NAME, D.CODE, L.NAME
            FROM PRODUCTION_ORDERS PO
            JOIN STOCK_DOCUMENTS D ON D.ID = PO.FINISHED_RECEIPT_DOCUMENT_ID
            JOIN BRANCHES B ON B.ID = PO.BRANCH_ID
            JOIN LOCATIONS L ON L.ID = PO.FINISHED_LOCATION_ID
            WHERE PO.ID = @ID
            """, connection);
        header.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await header.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var dtoHeader = (
            OrderCode: reader.GetString(0),
            DocumentDate: reader.GetDateTime(1),
            BranchName: reader.GetString(2),
            DocumentCode: reader.GetString(3),
            LocationName: reader.GetString(4));
        await reader.DisposeAsync();

        var lines = new List<ProductionFinishedReceiptPrintLine>();
        await using var lineCmd = new FbCommand("""
            SELECT FP.CODE, COALESCE(OL.MODEL_NAME, FP.PRODUCT_NAME), FP.GOLD_WEIGHT,
                   FP.MATERIAL_COST, FP.LABOR_COST, FP.NON_GOLD_MATERIAL_COST, FP.TOTAL_COST
            FROM FINISHED_PRODUCTS FP
            LEFT JOIN PRODUCTION_QC_UNITS U ON U.ID = FP.PRODUCTION_QC_UNIT_ID
            LEFT JOIN PRODUCTION_ORDER_LINES OL ON OL.ID = U.ORDER_LINE_ID
            WHERE FP.PRODUCTION_ORDER_ID = @ID
            ORDER BY FP.CODE
            """, connection);
        lineCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken);
        while (await lineReader.ReadAsync(cancellationToken))
        {
            lines.Add(new ProductionFinishedReceiptPrintLine(
                lineReader.GetString(0),
                lineReader.GetString(1),
                lineReader.GetDecimal(2),
                lineReader.GetDecimal(3),
                lineReader.GetDecimal(4),
                lineReader.GetDecimal(5),
                lineReader.GetDecimal(6)));
        }

        return new ProductionFinishedReceiptPrintDto(
            dtoHeader.OrderCode, dtoHeader.DocumentDate, dtoHeader.BranchName,
            dtoHeader.DocumentCode, dtoHeader.LocationName, lines);
    }

    public async Task<ProductionGoldSettlementPrintDto?> GetGoldSettlementPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var header = new FbCommand("""
            SELECT PO.CODE, B.NAME, PO.EXECUTION_TYPE,
                   COALESCE((SELECT SUM(U.LABOR_COST_SNAPSHOT)
                             FROM PRODUCTION_QC_UNITS U
                             WHERE U.PRODUCTION_ORDER_ID = PO.ID AND U.STATUS_CODE = 'PASSED'), 0)
            FROM PRODUCTION_ORDERS PO
            JOIN BRANCHES B ON B.ID = PO.BRANCH_ID
            WHERE PO.ID = @ID
            """, connection);
        header.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await header.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var orderCode = reader.GetString(0);
        var branchName = reader.GetString(1);
        var execution = reader.GetString(2);
        var labor = reader.GetDecimal(3);
        await reader.DisposeAsync();

        var lines = await LoadBalancePrintLinesAsync(connection, productionOrderId, cancellationToken);
        var createsDebt = string.Equals(execution, ProductionExecutionTypes.Outsourced, StringComparison.OrdinalIgnoreCase)
            && labor > 0;
        return new ProductionGoldSettlementPrintDto(
            orderCode, DateTime.Now, branchName, lines, labor, createsDebt);
    }

    private static async Task<IReadOnlyList<ProductionReturnPrintLine>> LoadBalancePrintLinesAsync(
        FbConnection connection,
        long productionOrderId,
        CancellationToken cancellationToken)
    {
        var lines = new List<ProductionReturnPrintLine>();
        await using var command = new FbCommand("""
            SELECT M.CODE, M.NAME, B.QUANTITY_ISSUED, B.QUANTITY_IN_PRODUCT,
                   B.QUANTITY_RETURNED, B.QUANTITY_SCRAP, B.QUANTITY_LOSS, B.UNIT_CODE
            FROM PRODUCTION_MATERIAL_BALANCES B
            JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
            WHERE B.PRODUCTION_ORDER_ID = @ID
            ORDER BY B.ID
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ProductionReturnPrintLine(
                reader.GetString(0), reader.GetString(1),
                reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4),
                reader.GetDecimal(5), reader.GetDecimal(6), reader.GetString(7)));
        }

        return lines;
    }

    public async Task<ProductionOrderPrintDto?> GetOrderPrintAsync(
        long productionOrderId,
        CancellationToken cancellationToken = default)
    {
        if (productionOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var header = new FbCommand("""
            SELECT PO.CODE, PO.ORDER_DATE, MI.DOCUMENT_DATE, PO.DUE_DATE,
                   PO.STATUS_CODE, B.NAME, C.NAME, SO.CODE, PO.NOTES,
                   CASE
                       WHEN PO.EXECUTION_TYPE = 'OUTSOURCED' THEN EW.NAME
                       ELSE (
                           SELECT LIST(TRIM(E.FULL_NAME), ', ')
                           FROM PRODUCTION_ORDER_WORKERS POW
                           JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
                           WHERE POW.PRODUCTION_ORDER_ID = PO.ID)
                   END
            FROM PRODUCTION_ORDERS PO
            JOIN BRANCHES B ON B.ID = PO.BRANCH_ID
            LEFT JOIN SALES_ORDERS SO ON SO.ID = PO.SALES_ORDER_ID
            LEFT JOIN CUSTOMERS C ON C.ID = SO.CUSTOMER_ID AND C.COMPANY_ID = PO.COMPANY_ID
            LEFT JOIN EXTERNAL_WORKERS EW ON EW.COMPANY_ID = PO.COMPANY_ID AND EW.ID = PO.EXTERNAL_WORKER_ID
            LEFT JOIN STOCK_DOCUMENTS MI ON MI.ID = PO.MATERIAL_ISSUE_DOCUMENT_ID
            WHERE PO.ID = @ID
            """, connection);
        header.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await header.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var dto = new ProductionOrderPrintDto(
            reader.GetString(0),
            reader.GetDateTime(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetDateTime(3),
            ProductionOrderStatuses.ToListDisplay(reader.GetString(4)),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            await GetOrderPrintLinesAsync(connection, productionOrderId, cancellationToken));
        await reader.DisposeAsync();
        return dto;
    }

    private static async Task<IReadOnlyList<ProductionOrderPrintLine>> GetOrderPrintLinesAsync(
        FbConnection connection,
        long productionOrderId,
        CancellationToken cancellationToken)
    {
        var lines = new List<ProductionOrderPrintLine>();
        await using var command = new FbCommand("""
            SELECT POL.LINE_NUMBER, POL.MODEL_NAME, POL.PLANNED_QUANTITY,
                   POL.GOLD_WEIGHT, POL.GOLD_PURITY_CODE, POL.LABOR_COST
            FROM PRODUCTION_ORDER_LINES POL
            WHERE POL.PRODUCTION_ORDER_ID = @ID
            ORDER BY POL.LINE_NUMBER
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new ProductionOrderPrintLine(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? 0 : reader.GetDecimal(5)));
        }
        return lines;
    }
}
