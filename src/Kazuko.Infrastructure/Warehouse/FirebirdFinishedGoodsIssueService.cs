using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdFinishedGoodsIssueService : IFinishedGoodsIssueService
{
    public async Task<long> CreateAndPostAsync(
        FinishedGoodsIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = FinishedGoodsIssueValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);

            string status;
            long? existingIssue;
            await using (var soCmd = new FbCommand("""
                SELECT STATUS_CODE, FINISHED_GOODS_ISSUE_DOCUMENT_ID
                FROM SALES_ORDERS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                soCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.SalesOrderId;
                soCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                soCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await soCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy đơn đặt chế tác.");
                status = reader.GetString(0);
                existingIssue = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            }

            if (!FinishedGoodsIssueValidator.IsConfirmedSalesOrderStatus(status))
                throw new InvalidOperationException("Đơn chưa xác nhận — không được xuất thành phẩm.");
            if (existingIssue is > 0)
                throw new InvalidOperationException("Đơn đã có phiếu xuất thành phẩm — không xuất lần hai.");

            var productIds = draft.Lines.Select(x => x.FinishedProductId).Distinct().ToList();
            foreach (var productId in productIds)
            {
                await using var fpCmd = new FbCommand("""
                    SELECT STATUS_CODE, SALES_ORDER_ID, LOCATION_ID
                    FROM FINISHED_PRODUCTS
                    WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                    WITH LOCK
                    """, connection, fbTx);
                fpCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
                fpCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                fpCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await fpCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException($"Không tìm thấy thành phẩm #{productId}.");
                var fpStatus = reader.GetString(0);
                var soId = reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1);
                if (soId != draft.SalesOrderId)
                    throw new InvalidOperationException($"Thành phẩm #{productId} không thuộc đơn đã chọn.");
                if (fpStatus is not ("IN_STOCK" or "WAITING_RECEIPT" or "RESERVED" or "DISPLAYED"))
                    throw new InvalidOperationException($"Thành phẩm #{productId} không ở trạng thái có thể xuất.");
            }

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "FINISHED_GOODS_ISSUE", session.BranchId, "XTP", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, SALES_ORDER_ID, PURPOSE_CODE,
                     DELIVERED_BY_NAME, RECEIVED_BY_NAME, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'FINISHED_GOODS_ISSUE', @DATE,
                     @LOC, @SO, 'FINISHED_GOODS_DELIVERY',
                     @DEL, @REC, @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@LOC", FbDbType.BigInt).Value = draft.SourceLocationId;
                insertDoc.Parameters.Add("@SO", FbDbType.BigInt).Value = draft.SalesOrderId;
                insertDoc.Parameters.Add("@DEL", FbDbType.VarChar, 150).Value =
                    (object?)draft.DeliveredByName ?? DBNull.Value;
                insertDoc.Parameters.Add("@REC", FbDbType.VarChar, 150).Value =
                    (object?)draft.ReceivedByName ?? DBNull.Value;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    (object?)draft.Notes ?? DBNull.Value;
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            foreach (var productId in productIds)
            {
                await using var updateFp = new FbCommand("""
                    UPDATE FINISHED_PRODUCTS
                       SET STATUS_CODE = 'SOLD',
                           LOCATION_ID = NULL
                     WHERE ID = @ID
                       AND STATUS_CODE IN ('IN_STOCK', 'WAITING_RECEIPT', 'RESERVED', 'DISPLAYED')
                    """, connection, fbTx);
                updateFp.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
                var affected = await updateFp.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                    throw new InvalidOperationException($"Không thể xuất thành phẩm #{productId} (đã thay đổi trạng thái).");
            }

            await using (var updateSo = new FbCommand("""
                UPDATE SALES_ORDERS
                   SET FINISHED_GOODS_ISSUE_DOCUMENT_ID = @DOC,
                       FINISHED_GOODS_ISSUED_AT = CURRENT_TIMESTAMP,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                   AND FINISHED_GOODS_ISSUE_DOCUMENT_ID IS NULL
                """, connection, fbTx))
            {
                updateSo.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                updateSo.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.SalesOrderId;
                var affected = await updateSo.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                    throw new InvalidOperationException("Đơn đã được xuất bởi phiên khác.");
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "FINISHED_GOODS_ISSUE_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), code, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return documentId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ReverseAsync(
        long documentId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Phải nhập lý do hủy.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            long? salesOrderId;
            string status;
            await using (var docCmd = new FbCommand("""
                SELECT DOCUMENT_TYPE, STATUS_CODE, SALES_ORDER_ID
                FROM STOCK_DOCUMENTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                docCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
                docCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                docCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await docCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy chứng từ.");
                if (reader.GetString(0) != StockDocumentTypes.FinishedGoodsIssue)
                    throw new InvalidOperationException("Chỉ hủy được phiếu xuất thành phẩm.");
                status = reader.GetString(1);
                salesOrderId = reader.IsDBNull(2) ? null : reader.GetInt64(2);
            }

            if (status != StockDocumentStatuses.Posted)
                throw new InvalidOperationException("Chỉ hủy chứng từ đã ghi sổ.");

            await using (var cancel = new FbCommand("""
                UPDATE STOCK_DOCUMENTS
                   SET STATUS_CODE = 'CANCELLED',
                       CANCELLED_BY = @BY,
                       CANCELLED_AT = CURRENT_TIMESTAMP,
                       CANCELLATION_REASON = @REASON,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID AND STATUS_CODE = 'POSTED'
                """, connection, fbTx))
            {
                cancel.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                cancel.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
                cancel.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
                if (await cancel.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không thể hủy — chứng từ đã thay đổi.");
            }

            if (salesOrderId is > 0)
            {
                await using var clearSo = new FbCommand("""
                    UPDATE SALES_ORDERS
                       SET FINISHED_GOODS_ISSUE_DOCUMENT_ID = NULL,
                           FINISHED_GOODS_ISSUED_AT = NULL,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID AND FINISHED_GOODS_ISSUE_DOCUMENT_ID = @DOC
                    """, connection, fbTx);
                clearSo.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId.Value;
                clearSo.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                await clearSo.ExecuteNonQueryAsync(cancellationToken);

                // Restore finished products that were sold for this order and have no other linkage.
                await using var restoreFp = new FbCommand("""
                    UPDATE FINISHED_PRODUCTS
                       SET STATUS_CODE = 'IN_STOCK'
                     WHERE SALES_ORDER_ID = @SO
                       AND STATUS_CODE = 'SOLD'
                       AND BRANCH_ID = @BID
                    """, connection, fbTx);
                restoreFp.Parameters.Add("@SO", FbDbType.BigInt).Value = salesOrderId.Value;
                restoreFp.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await restoreFp.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "FINISHED_GOODS_ISSUE_REVERSED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), reason, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
