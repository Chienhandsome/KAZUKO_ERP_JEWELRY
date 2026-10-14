using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdFinishedProductTransferService : IFinishedProductTransferService
{
    public async Task<long> CreateRequestAsync(
        FinishedProductTransferDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = FinishedProductTransferValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            await EnsureLocationAsync(connection, fbTx, draft.SourceLocationId,
                session.BranchId, InventoryScopes.FinishedGoods, "Kho nguồn phải là Kho thành phẩm.", cancellationToken);
            await EnsureLocationAsync(connection, fbTx, draft.DestinationLocationId,
                session.BranchId, InventoryScopes.SalesFloor, "Kho đích phải là Kho bán hàng.", cancellationToken);

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "SALES_INTAKE_REQUEST", session.BranchId, "YCN", cancellationToken);

            long requestId;
            await using (var insertReq = new FbCommand("""
                INSERT INTO SALES_INTAKE_REQUESTS
                    (COMPANY_ID, BRANCH_ID, CODE, REQUEST_DATE,
                     SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID,
                     STATUS_CODE, NOTES, CREATED_BY, CREATED_AT)
                VALUES
                    (@CID, @BID, @CODE, @DATE,
                     @SOURCE, @DESTINATION,
                     'PENDING', @NOTES, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertReq.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertReq.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertReq.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertReq.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertReq.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = draft.SourceLocationId;
                insertReq.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = draft.DestinationLocationId;
                insertReq.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
                insertReq.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                requestId = Convert.ToInt64(await insertReq.ExecuteScalarAsync(cancellationToken));
            }

            var lineNo = 0;
            foreach (var productId in draft.FinishedProductIds.Distinct())
            {
                lineNo++;
                var product = await GetAndLockProductAsync(
                    connection, fbTx, companyId, session.BranchId, productId, cancellationToken);
                if (product.LocationId != draft.SourceLocationId)
                    throw new InvalidOperationException($"Thành phẩm '{product.Code}' không còn ở kho nguồn.");
                if (product.StatusCode != "IN_STOCK")
                    throw new InvalidOperationException($"Thành phẩm '{product.Code}' không ở trạng thái có thể chuyển.");

                await EnsureProductNotOnOtherPendingAsync(
                    connection, fbTx, productId, requestId, product.Code, cancellationToken);

                await using var insertLine = new FbCommand("""
                    INSERT INTO SALES_INTAKE_REQUEST_LINES
                        (REQUEST_ID, LINE_NUMBER, FINISHED_PRODUCT_ID, UNIT_COST)
                    VALUES
                        (@REQ, @LINE, @PRODUCT, @COST)
                    """, connection, fbTx);
                insertLine.Parameters.Add("@REQ", FbDbType.BigInt).Value = requestId;
                insertLine.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNo;
                insertLine.Parameters.Add("@PRODUCT", FbDbType.BigInt).Value = productId;
                insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = product.UnitCost;
                await insertLine.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "SALES_INTAKE_REQUEST_CREATED",
                session.UserId, session.Username, session.BranchId,
                "SALES_INTAKE_REQUEST", requestId.ToString(), code, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return requestId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ApproveAsync(
        long requestId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (requestId <= 0)
            throw new InvalidOperationException("Yêu cầu không hợp lệ.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            await EnsureSalesIntakePermissionAsync(connection, fbTx, session, cancellationToken);

            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var request = await LockRequestAsync(
                connection, fbTx, companyId, session.BranchId, requestId, cancellationToken);
            if (!string.Equals(request.StatusCode, SalesIntakeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Yêu cầu '{request.Code}' không còn ở trạng thái chờ duyệt.");

            var lines = await LoadRequestProductLinesAsync(connection, fbTx, requestId, cancellationToken);
            if (lines.Count == 0)
                throw new InvalidOperationException($"Yêu cầu '{request.Code}' không có dòng thành phẩm.");

            var docCode = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "FINISHED_PRODUCT_TRANSFER", session.BranchId, "CTP", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID, PURPOSE_CODE, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'TRANSFER', @DATE,
                     @SOURCE, @DESTINATION, 'FINISHED_TO_SALES', @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = docCode;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = request.RequestDate;
                insertDoc.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = request.SourceLocationId;
                insertDoc.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = request.DestinationLocationId;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes;
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var lineNo = 0;
            foreach (var line in lines)
            {
                lineNo++;
                var product = await GetAndLockProductAsync(
                    connection, fbTx, companyId, session.BranchId, line.ProductId, cancellationToken);
                if (product.LocationId != request.SourceLocationId)
                    throw new InvalidOperationException($"Thành phẩm '{product.Code}' không còn ở kho nguồn.");
                if (product.StatusCode != "IN_STOCK")
                    throw new InvalidOperationException($"Thành phẩm '{product.Code}' không ở trạng thái có thể chuyển.");

                await using (var update = new FbCommand("""
                    UPDATE FINISHED_PRODUCTS
                    SET LOCATION_ID = @DESTINATION,
                        STATUS_CODE = 'DISPLAYED',
                        ROW_VERSION = ROW_VERSION + 1
                    WHERE ID = @ID
                      AND LOCATION_ID = @SOURCE
                      AND STATUS_CODE = 'IN_STOCK'
                      AND ROW_VERSION = @VERSION
                    """, connection, fbTx))
                {
                    update.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = request.DestinationLocationId;
                    update.Parameters.Add("@ID", FbDbType.BigInt).Value = line.ProductId;
                    update.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = request.SourceLocationId;
                    update.Parameters.Add("@VERSION", FbDbType.BigInt).Value = product.RowVersion;
                    if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException($"Thành phẩm '{product.Code}' đã được người khác thay đổi. Vui lòng tải lại.");
                }

                await InsertDocumentLineAsync(
                    connection, fbTx, documentId, lineNo, line.ProductId,
                    request.SourceLocationId, request.DestinationLocationId,
                    "IN_STOCK", "DISPLAYED", product.UnitCost, cancellationToken);

                await InsertMovementAsync(
                    connection, fbTx, companyId, session.BranchId, line.ProductId,
                    "OUT", "TRANSFER", documentId, -1, product.UnitCost, request.SourceLocationId,
                    request.SourceLocationId, request.DestinationLocationId,
                    "IN_STOCK", "DISPLAYED", session.UserId, cancellationToken);
                await InsertMovementAsync(
                    connection, fbTx, companyId, session.BranchId, line.ProductId,
                    "IN", "TRANSFER", documentId, 1, product.UnitCost, request.DestinationLocationId,
                    request.SourceLocationId, request.DestinationLocationId,
                    "IN_STOCK", "DISPLAYED", session.UserId, cancellationToken);
            }

            await using (var approveReq = new FbCommand("""
                UPDATE SALES_INTAKE_REQUESTS
                SET STATUS_CODE = 'APPROVED',
                    STOCK_DOCUMENT_ID = @DOC,
                    DECIDED_BY = @BY,
                    DECIDED_AT = CURRENT_TIMESTAMP
                WHERE ID = @ID
                  AND STATUS_CODE = 'PENDING'
                """, connection, fbTx))
            {
                approveReq.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                approveReq.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                approveReq.Parameters.Add("@ID", FbDbType.BigInt).Value = requestId;
                if (await approveReq.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Yêu cầu '{request.Code}' đã được người khác xử lý.");
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "FINISHED_PRODUCT_TRANSFER_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), docCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RejectAsync(
        long requestId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (requestId <= 0)
            throw new InvalidOperationException("Yêu cầu không hợp lệ.");

        var rejectReason = (reason ?? string.Empty).Trim();
        if (rejectReason.Length == 0)
            throw new InvalidOperationException("Phải nhập lý do từ chối.");
        if (rejectReason.Length > 500)
            rejectReason = rejectReason[..500];

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            await EnsureSalesIntakePermissionAsync(connection, fbTx, session, cancellationToken);

            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var request = await LockRequestAsync(
                connection, fbTx, companyId, session.BranchId, requestId, cancellationToken);
            if (!string.Equals(request.StatusCode, SalesIntakeRequestStatuses.Pending, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Yêu cầu '{request.Code}' không còn ở trạng thái chờ duyệt.");

            await using (var rejectReq = new FbCommand("""
                UPDATE SALES_INTAKE_REQUESTS
                SET STATUS_CODE = 'REJECTED',
                    REJECTION_REASON = @REASON,
                    DECIDED_BY = @BY,
                    DECIDED_AT = CURRENT_TIMESTAMP
                WHERE ID = @ID
                  AND STATUS_CODE = 'PENDING'
                """, connection, fbTx))
            {
                rejectReq.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = rejectReason;
                rejectReq.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                rejectReq.Parameters.Add("@ID", FbDbType.BigInt).Value = requestId;
                if (await rejectReq.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Yêu cầu '{request.Code}' đã được người khác xử lý.");
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "SALES_INTAKE_REQUEST_REJECTED",
                session.UserId, session.Username, session.BranchId,
                "SALES_INTAKE_REQUEST", requestId.ToString(),
                $"{{\"code\":\"{request.Code}\",\"reason\":\"{rejectReason.Replace("\"", "'")}\"}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<SalesIntakeRequestSummary>> ListRequestsAsync(
        string? statusCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var status = (statusCode ?? string.Empty).Trim().ToUpperInvariant();
        var filterStatus = status is "PENDING" or "APPROVED" or "REJECTED";

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand($"""
            SELECT R.ID, R.CODE, R.REQUEST_DATE, R.STATUS_CODE,
                   COALESCE(SL.NAME, '') AS SOURCE_NAME,
                   COALESCE(DL.NAME, '') AS DEST_NAME,
                   COALESCE(U.DISPLAY_NAME, '') AS CREATED_BY_NAME,
                   R.CREATED_AT,
                   (SELECT COUNT(*) FROM SALES_INTAKE_REQUEST_LINES L WHERE L.REQUEST_ID = R.ID) AS LINE_COUNT,
                   COALESCE(R.NOTES, '') AS NOTES,
                   COALESCE(R.REJECTION_REASON, '') AS REJECT_REASON
            FROM SALES_INTAKE_REQUESTS R
            LEFT JOIN LOCATIONS SL ON SL.ID = R.SOURCE_LOCATION_ID
            LEFT JOIN LOCATIONS DL ON DL.ID = R.DESTINATION_LOCATION_ID
            LEFT JOIN USERS U ON U.ID = R.CREATED_BY
            WHERE R.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND R.BRANCH_ID = @BID
              {(filterStatus ? "AND R.STATUS_CODE = @STATUS" : string.Empty)}
            ORDER BY
              CASE R.STATUS_CODE WHEN 'PENDING' THEN 0 WHEN 'APPROVED' THEN 1 ELSE 2 END,
              R.CREATED_AT DESC
            """, connection);
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        if (filterStatus)
            command.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = status;

        var rows = new List<SalesIntakeRequestSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new SalesIntakeRequestSummary(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDateTime(7),
                Convert.ToInt32(reader.GetValue(8)),
                reader.GetString(9),
                reader.GetString(10)));
        }

        return rows;
    }

    public async Task<SalesIntakeRequestDetail?> GetRequestDetailAsync(
        long requestId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (requestId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT R.ID, R.CODE, R.REQUEST_DATE, R.STATUS_CODE,
                   R.SOURCE_LOCATION_ID, COALESCE(SL.NAME, '') AS SOURCE_NAME,
                   R.DESTINATION_LOCATION_ID, COALESCE(DL.NAME, '') AS DEST_NAME,
                   COALESCE(U.DISPLAY_NAME, '') AS CREATED_BY_NAME,
                   R.CREATED_AT,
                   COALESCE(R.NOTES, '') AS NOTES,
                   COALESCE(R.REJECTION_REASON, '') AS REJECT_REASON
            FROM SALES_INTAKE_REQUESTS R
            LEFT JOIN LOCATIONS SL ON SL.ID = R.SOURCE_LOCATION_ID
            LEFT JOIN LOCATIONS DL ON DL.ID = R.DESTINATION_LOCATION_ID
            LEFT JOIN USERS U ON U.ID = R.CREATED_BY
            WHERE R.ID = @ID
              AND R.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND R.BRANCH_ID = @BID
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = requestId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var detail = new SalesIntakeRequestDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            reader.GetString(3),
            reader.GetInt64(4),
            reader.GetString(5),
            reader.GetInt64(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetDateTime(9),
            reader.GetString(10),
            reader.GetString(11),
            Array.Empty<SalesIntakeRequestLine>());
        await reader.CloseAsync();

        var lines = new List<SalesIntakeRequestLine>();
        await using (var lineCmd = new FbCommand("""
            SELECT L.FINISHED_PRODUCT_ID, F.CODE, F.PRODUCT_NAME,
                   COALESCE(F.GOLD_PURITY, '') AS GOLD_PURITY, L.UNIT_COST
            FROM SALES_INTAKE_REQUEST_LINES L
            JOIN FINISHED_PRODUCTS F ON F.ID = L.FINISHED_PRODUCT_ID
            WHERE L.REQUEST_ID = @REQ
            ORDER BY L.LINE_NUMBER
            """, connection))
        {
            lineCmd.Parameters.Add("@REQ", FbDbType.BigInt).Value = requestId;
            await using var lineReader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await lineReader.ReadAsync(cancellationToken))
            {
                lines.Add(new SalesIntakeRequestLine(
                    lineReader.GetInt64(0),
                    lineReader.GetString(1),
                    lineReader.GetString(2),
                    lineReader.GetString(3),
                    lineReader.GetDecimal(4)));
            }
        }

        return detail with { Lines = lines };
    }

    private static async Task EnsureSalesIntakePermissionAsync(
        FbConnection connection,
        FbTransaction transaction,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (session.UserId <= 0)
            throw new InvalidOperationException("Phiên làm việc thiếu người dùng.");

        await using var command = new FbCommand("""
            SELECT R.CODE,
                   MAX(CASE WHEN P.CODE = @PERM AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END)
            FROM USERS U
            JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
            LEFT JOIN ROLE_PERMISSIONS RP ON RP.ROLE_ID = R.ID
            LEFT JOIN PERMISSIONS P ON P.ID = RP.PERMISSION_ID
            WHERE U.ID = @USER_ID
              AND U.IS_ACTIVE = TRUE
            GROUP BY R.CODE
            """, connection, transaction);
        command.Parameters.Add("@USER_ID", FbDbType.BigInt).Value = session.UserId;
        command.Parameters.Add("@PERM", FbDbType.VarChar, 80).Value = WarehouseWritePermissions.SalesIntakeApprove;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không có quyền duyệt nhập kho bán hàng.");

        var roleCode = reader.GetString(0).Trim();
        if (string.Equals(roleCode, "ADMIN", StringComparison.OrdinalIgnoreCase))
            return;

        if (reader.IsDBNull(1) || Convert.ToInt32(reader.GetValue(1)) != 1)
            throw new InvalidOperationException("Không có quyền duyệt nhập kho bán hàng.");
    }

    private static async Task EnsureProductNotOnOtherPendingAsync(
        FbConnection connection,
        FbTransaction transaction,
        long productId,
        long currentRequestId,
        string productCode,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM SALES_INTAKE_REQUEST_LINES L
            JOIN SALES_INTAKE_REQUESTS R ON R.ID = L.REQUEST_ID
            WHERE L.FINISHED_PRODUCT_ID = @PRODUCT
              AND R.ID <> @REQ
              AND R.STATUS_CODE = 'PENDING'
            """, connection, transaction);
        command.Parameters.Add("@PRODUCT", FbDbType.BigInt).Value = productId;
        command.Parameters.Add("@REQ", FbDbType.BigInt).Value = currentRequestId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0)
            throw new InvalidOperationException($"Thành phẩm '{productCode}' đã nằm trên yêu cầu khác đang chờ duyệt.");
    }

    private static async Task<LockedSalesIntakeRequest> LockRequestAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long requestId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE, STATUS_CODE, REQUEST_DATE,
                   SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID,
                   COALESCE(NOTES, '') AS NOTES
            FROM SALES_INTAKE_REQUESTS
            WHERE ID = @ID
              AND COMPANY_ID = @CID
              AND BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = requestId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy yêu cầu nhập kho bán hàng.");
        return new LockedSalesIntakeRequest(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetString(5));
    }

    private static async Task<IReadOnlyList<RequestProductLine>> LoadRequestProductLinesAsync(
        FbConnection connection,
        FbTransaction transaction,
        long requestId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT FINISHED_PRODUCT_ID
            FROM SALES_INTAKE_REQUEST_LINES
            WHERE REQUEST_ID = @REQ
            ORDER BY LINE_NUMBER
            """, connection, transaction);
        command.Parameters.Add("@REQ", FbDbType.BigInt).Value = requestId;
        var lines = new List<RequestProductLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            lines.Add(new RequestProductLine(reader.GetInt64(0)));
        return lines;
    }

    private static async Task EnsureLocationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long locationId,
        long branchId,
        string requiredScope,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM LOCATIONS
            WHERE ID = @ID AND BRANCH_ID = @BID AND IS_ACTIVE = TRUE AND INVENTORY_SCOPE = @SCOPE
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@SCOPE", FbDbType.VarChar, 30).Value = requiredScope;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException(errorMessage);
    }

    private static async Task<LockedProduct> GetAndLockProductAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long productId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE, STATUS_CODE, LOCATION_ID, TOTAL_COST, ROW_VERSION
            FROM FINISHED_PRODUCTS
            WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Không tìm thấy thành phẩm #{productId}.");
        return new LockedProduct(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.GetDecimal(3),
            reader.GetInt64(4));
    }

    internal static async Task InsertDocumentLineAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        int lineNumber,
        long productId,
        long? sourceLocationId,
        long? destinationLocationId,
        string statusBefore,
        string statusAfter,
        decimal unitCost,
        CancellationToken cancellationToken,
        long? warehouseGoldPriceId = null,
        string? goldPriceCode = null,
        decimal goldPricePerChi = 0,
        decimal goldWeightChi = 0,
        decimal laborCost = 0,
        decimal nonGoldCost = 0)
    {
        await using var command = new FbCommand("""
            INSERT INTO FINISHED_PRODUCT_DOCUMENT_LINES
                (DOCUMENT_ID, LINE_NUMBER, FINISHED_PRODUCT_ID,
                 SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID,
                 STATUS_BEFORE, STATUS_AFTER, UNIT_COST,
                 WAREHOUSE_GOLD_PRICE_ID, GOLD_PRICE_CODE, GOLD_PRICE_PER_CHI,
                 GOLD_WEIGHT_CHI, LABOR_COST, NON_GOLD_COST)
            VALUES
                (@DOC, @LINE, @PRODUCT, @SOURCE, @DESTINATION,
                 @BEFORE, @AFTER, @COST,
                 @WGP, @GOLD_CODE, @GOLD_PRICE, @GOLD_WEIGHT, @LABOR, @NON_GOLD)
            """, connection, transaction);
        command.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        command.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNumber;
        command.Parameters.Add("@PRODUCT", FbDbType.BigInt).Value = productId;
        command.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = sourceLocationId is > 0 ? sourceLocationId.Value : DBNull.Value;
        command.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = destinationLocationId is > 0 ? destinationLocationId.Value : DBNull.Value;
        command.Parameters.Add("@BEFORE", FbDbType.VarChar, 30).Value = statusBefore;
        command.Parameters.Add("@AFTER", FbDbType.VarChar, 30).Value = statusAfter;
        command.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
        command.Parameters.Add("@WGP", FbDbType.BigInt).Value =
            warehouseGoldPriceId is > 0 ? warehouseGoldPriceId.Value : DBNull.Value;
        command.Parameters.Add("@GOLD_CODE", FbDbType.VarChar, 40).Value =
            string.IsNullOrWhiteSpace(goldPriceCode) ? DBNull.Value : goldPriceCode;
        command.Parameters.Add("@GOLD_PRICE", FbDbType.Decimal).Value = goldPricePerChi;
        command.Parameters.Add("@GOLD_WEIGHT", FbDbType.Decimal).Value = goldWeightChi;
        command.Parameters.Add("@LABOR", FbDbType.Decimal).Value = laborCost;
        command.Parameters.Add("@NON_GOLD", FbDbType.Decimal).Value = nonGoldCost;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static async Task InsertMovementAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long productId,
        string movementType,
        string documentType,
        long documentId,
        decimal quantityChange,
        decimal unitCost,
        long? locationId,
        long? fromLocationId,
        long? toLocationId,
        string statusBefore,
        string statusAfter,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            INSERT INTO FINISHED_PRODUCT_MOVEMENTS
                (COMPANY_ID, BRANCH_ID, FINISHED_PRODUCT_ID, MOVEMENT_TYPE,
                 DOCUMENT_TYPE, DOCUMENT_ID, OCCURRED_AT, QUANTITY_CHANGE,
                 UNIT_COST, VALUE_CHANGE, LOCATION_ID, FROM_LOCATION_ID, TO_LOCATION_ID,
                 STATUS_BEFORE, STATUS_AFTER, CREATED_BY, CREATED_AT)
            VALUES
                (@CID, @BID, @PRODUCT, @TYPE,
                 @DOCUMENT_TYPE, @DOC, CURRENT_TIMESTAMP, @QTY,
                 @COST, @VALUE, @LOCATION, @FROM_LOCATION, @TO_LOCATION,
                 @BEFORE, @AFTER, @BY, CURRENT_TIMESTAMP)
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@PRODUCT", FbDbType.BigInt).Value = productId;
        command.Parameters.Add("@TYPE", FbDbType.VarChar, 20).Value = movementType;
        command.Parameters.Add("@DOCUMENT_TYPE", FbDbType.VarChar, 40).Value = documentType;
        command.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        command.Parameters.Add("@QTY", FbDbType.Decimal).Value = quantityChange;
        command.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
        command.Parameters.Add("@VALUE", FbDbType.Decimal).Value = quantityChange * unitCost;
        command.Parameters.Add("@LOCATION", FbDbType.BigInt).Value = locationId is > 0 ? locationId.Value : DBNull.Value;
        command.Parameters.Add("@FROM_LOCATION", FbDbType.BigInt).Value = fromLocationId is > 0 ? fromLocationId.Value : DBNull.Value;
        command.Parameters.Add("@TO_LOCATION", FbDbType.BigInt).Value = toLocationId is > 0 ? toLocationId.Value : DBNull.Value;
        command.Parameters.Add("@BEFORE", FbDbType.VarChar, 30).Value = statusBefore;
        command.Parameters.Add("@AFTER", FbDbType.VarChar, 30).Value = statusAfter;
        command.Parameters.Add("@BY", FbDbType.BigInt).Value = createdBy is > 0 ? createdBy.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record LockedProduct(
        string Code,
        string StatusCode,
        long? LocationId,
        decimal UnitCost,
        long RowVersion);

    private sealed record LockedSalesIntakeRequest(
        string Code,
        string StatusCode,
        DateTime RequestDate,
        long SourceLocationId,
        long DestinationLocationId,
        string Notes);

    private sealed record RequestProductLine(long ProductId);
}
