using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdWarehouseRepository : IWarehouseRepository
{
    public async Task<IReadOnlyList<WarehouseLocationOption>> GetLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var result = new List<WarehouseLocationOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, BRANCH_ID, CODE, NAME, LOCATION_TYPE
            FROM LOCATIONS
            WHERE BRANCH_ID = @BRANCH_ID
              AND IS_ACTIVE = TRUE
              AND LOCATION_TYPE IN ('WAREHOUSE', 'COUNTER')
            ORDER BY CASE WHEN LOCATION_TYPE = 'WAREHOUSE' THEN 0 ELSE 1 END, NAME
            """, connection);
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WarehouseLocationOption(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<MaterialOption>> SearchMaterialsAsync(
        string searchText,
        string materialType,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var result = new List<MaterialOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT M.ID, M.CODE, M.NAME, M.MATERIAL_TYPE, COALESCE(M.PURITY_CODE, ''), M.UNIT_CODE, M.IS_ACTIVE
            FROM MATERIALS M
            WHERE M.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND M.IS_ACTIVE = TRUE
              AND (@HAS_TYPE = 0 OR M.MATERIAL_TYPE = @TYPE)
              AND (@HAS_SEARCH = 0
                   OR CAST(M.CODE AS VARCHAR(100) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR M.NAME CONTAINING @SEARCH)
            ORDER BY M.MATERIAL_TYPE, M.NAME, M.CODE
            """, connection);

        var search = (searchText ?? string.Empty).Trim();
        var type = (materialType ?? string.Empty).Trim().ToUpperInvariant();
        command.Parameters.Add("@HAS_TYPE", FbDbType.Integer).Value = type.Length > 0 ? 1 : 0;
        command.Parameters.Add("@TYPE", FbDbType.VarChar, 20).Value = type;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MaterialOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return result;
    }

    public async Task<IReadOnlyList<MaterialBalanceItem>> SearchMaterialBalancesAsync(
        MaterialBalanceSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var result = new List<MaterialBalanceItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT M.ID, M.CODE, M.NAME, M.MATERIAL_TYPE, COALESCE(M.PURITY_CODE, ''), M.UNIT_CODE,
                   L.ID, L.CODE, L.NAME, B.QUANTITY, B.AVERAGE_COST,
                   B.QUANTITY * B.AVERAGE_COST, M.MIN_STOCK
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
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var type = (filter.MaterialType ?? string.Empty).Trim().ToUpperInvariant();
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_LOCATION", FbDbType.Integer).Value = filter.LocationId > 0 ? 1 : 0;
        command.Parameters.Add("@LOCATION_ID", FbDbType.BigInt).Value = filter.LocationId;
        command.Parameters.Add("@HAS_TYPE", FbDbType.Integer).Value = type.Length > 0 ? 1 : 0;
        command.Parameters.Add("@TYPE", FbDbType.VarChar, 20).Value = type;
        command.Parameters.Add("@LOW_ONLY", FbDbType.Integer).Value = filter.LowStockOnly ? 1 : 0;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var quantity = reader.GetDecimal(9);
            var minimum = reader.GetDecimal(12);
            result.Add(new MaterialBalanceItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetString(8),
                quantity,
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                minimum,
                quantity <= minimum));
        }

        return result;
    }

    public async Task<IReadOnlyList<StockDocumentSummary>> SearchDocumentsAsync(
        StockDocumentSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var result = new List<StockDocumentSummary>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT D.ID, D.CODE, D.DOCUMENT_DATE, D.DOCUMENT_TYPE, D.STATUS_CODE,
                   COALESCE(S.NAME, ''), COALESCE(T.NAME, ''), COALESCE(D.REFERENCE_NO, ''),
                   COALESCE(D.COUNTERPARTY_NAME, ''), D.SUPPLIER_ID,
                   (SELECT COUNT(*) FROM STOCK_DOCUMENT_LINES X WHERE X.DOCUMENT_ID = D.ID),
                   COALESCE((SELECT SUM(X.QUANTITY) FROM STOCK_DOCUMENT_LINES X WHERE X.DOCUMENT_ID = D.ID), 0),
                   COALESCE((SELECT SUM(X.LINE_AMOUNT) FROM STOCK_DOCUMENT_LINES X WHERE X.DOCUMENT_ID = D.ID), 0),
                   COALESCE(CU.DISPLAY_NAME, ''), D.CREATED_AT,
                   COALESCE(PU.DISPLAY_NAME, ''), D.POSTED_AT
            FROM STOCK_DOCUMENTS D
            LEFT JOIN LOCATIONS S ON S.ID = D.SOURCE_LOCATION_ID
            LEFT JOIN LOCATIONS T ON T.ID = D.DESTINATION_LOCATION_ID
            LEFT JOIN USERS CU ON CU.ID = D.CREATED_BY
            LEFT JOIN USERS PU ON PU.ID = D.POSTED_BY
            WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND D.BRANCH_ID = @BRANCH_ID
              AND (@HAS_TYPE = 0 OR D.DOCUMENT_TYPE = @TYPE)
              AND (@HAS_STATUS = 0 OR D.STATUS_CODE = @STATUS)
              AND (@HAS_FROM = 0 OR D.DOCUMENT_DATE >= @FROM_DATE)
              AND (@HAS_TO = 0 OR D.DOCUMENT_DATE <= @TO_DATE)
              AND (@HAS_SEARCH = 0
                   OR CAST(D.CODE AS VARCHAR(80) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR CAST(COALESCE(D.REFERENCE_NO, '') AS VARCHAR(200) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR COALESCE(D.COUNTERPARTY_NAME, '') CONTAINING @SEARCH)
            ORDER BY D.DOCUMENT_DATE DESC, D.ID DESC
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var type = (filter.DocumentType ?? string.Empty).Trim().ToUpperInvariant();
        var status = (filter.StatusCode ?? string.Empty).Trim().ToUpperInvariant();
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_TYPE", FbDbType.Integer).Value = type.Length > 0 ? 1 : 0;
        command.Parameters.Add("@TYPE", FbDbType.VarChar, 30).Value = type;
        command.Parameters.Add("@HAS_STATUS", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = status;
        command.Parameters.Add("@HAS_FROM", FbDbType.Integer).Value = filter.FromDate.HasValue ? 1 : 0;
        command.Parameters.Add("@FROM_DATE", FbDbType.Date).Value = filter.FromDate?.Date ?? DateTime.Today;
        command.Parameters.Add("@HAS_TO", FbDbType.Integer).Value = filter.ToDate.HasValue ? 1 : 0;
        command.Parameters.Add("@TO_DATE", FbDbType.Date).Value = filter.ToDate?.Date ?? DateTime.Today;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StockDocumentSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetInt64(9),
                reader.GetInt32(10),
                reader.GetDecimal(11),
                reader.GetDecimal(12),
                reader.GetString(13),
                reader.GetDateTime(14),
                reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetDateTime(16)));
        }

        return result;
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
        await using var command = new FbCommand("""
            SELECT D.ID, D.CODE, D.DOCUMENT_DATE, D.DOCUMENT_TYPE, D.STATUS_CODE,
                   D.SOURCE_LOCATION_ID, D.DESTINATION_LOCATION_ID, D.PRODUCTION_ORDER_ID, D.SUPPLIER_ID,
                   COALESCE(D.PURPOSE_CODE, ''), COALESCE(D.REFERENCE_NO, ''),
                   COALESCE(D.COUNTERPARTY_NAME, ''), COALESCE(D.NOTES, ''),
                   COALESCE(CU.DISPLAY_NAME, ''), D.CREATED_AT,
                   COALESCE(PU.DISPLAY_NAME, ''), D.POSTED_AT, COALESCE(D.CANCELLATION_REASON, '')
            FROM STOCK_DOCUMENTS D
            LEFT JOIN USERS CU ON CU.ID = D.CREATED_BY
            LEFT JOIN USERS PU ON PU.ID = D.POSTED_BY
            WHERE D.ID = @ID
              AND D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND D.BRANCH_ID = @BRANCH_ID
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new StockDocumentDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.IsDBNull(6) ? null : reader.GetInt64(6),
            reader.IsDBNull(7) ? null : reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetDateTime(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetDateTime(16),
            reader.GetString(17));
    }

    public async Task<IReadOnlyList<StockDocumentLine>> GetDocumentLinesAsync(
        long documentId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var result = new List<StockDocumentLine>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT L.ID, L.LINE_NUMBER, L.MATERIAL_ID, L.PRODUCTION_MATERIAL_PLAN_ID,
                   M.CODE, M.NAME, M.MATERIAL_TYPE, COALESCE(M.PURITY_CODE, ''), M.UNIT_CODE,
                   L.QUANTITY, L.UNIT_COST, L.LINE_AMOUNT,
                   COALESCE(L.LOT_NO, ''), COALESCE(L.NOTES, '')
            FROM STOCK_DOCUMENT_LINES L
            JOIN STOCK_DOCUMENTS D ON D.ID = L.DOCUMENT_ID
            JOIN MATERIALS M ON M.ID = L.MATERIAL_ID
            WHERE L.DOCUMENT_ID = @ID
              AND D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND D.BRANCH_ID = @BRANCH_ID
            ORDER BY L.LINE_NUMBER
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StockDocumentLine(
                reader.GetInt64(0),
                reader.GetInt32(1),
                reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetDecimal(11),
                reader.GetString(12),
                reader.GetString(13)));
        }

        return result;
    }

    public async Task<IReadOnlyList<StockMovementItem>> SearchMovementsAsync(
        StockMovementSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        EnsureBranch(session);

        var result = new List<StockMovementItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT X.ID, X.OCCURRED_AT, D.CODE, D.DOCUMENT_TYPE, M.CODE, M.NAME,
                   L.NAME, X.MOVEMENT_TYPE, X.QUANTITY_CHANGE, X.UNIT_COST,
                   X.VALUE_CHANGE, COALESCE(U.DISPLAY_NAME, '')
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
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_MATERIAL", FbDbType.Integer).Value = filter.MaterialId > 0 ? 1 : 0;
        command.Parameters.Add("@MATERIAL_ID", FbDbType.BigInt).Value = filter.MaterialId;
        command.Parameters.Add("@HAS_LOCATION", FbDbType.Integer).Value = filter.LocationId > 0 ? 1 : 0;
        command.Parameters.Add("@LOCATION_ID", FbDbType.BigInt).Value = filter.LocationId;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new StockMovementItem(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetDecimal(10),
                reader.GetString(11)));
        }

        return result;
    }

    private static void EnsureBranch(AuthenticatedSession session)
    {
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên đăng nhập không có chi nhánh hợp lệ.");
    }
}
