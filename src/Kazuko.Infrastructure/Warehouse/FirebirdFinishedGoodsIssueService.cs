using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;

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
            await EnsureSourceLocationAsync(
                connection, fbTx, draft.SourceLocationId, session.BranchId, cancellationToken);

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
            var products = new Dictionary<long, IssueProduct>();
            foreach (var productId in productIds)
            {
                await using var fpCmd = new FbCommand("""
                    SELECT CODE, STATUS_CODE, SALES_ORDER_ID, LOCATION_ID,
                           GOLD_PURITY, GOLD_WEIGHT, TOTAL_WEIGHT, LABOR_COST,
                           NON_GOLD_MATERIAL_COST, ROW_VERSION
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
                var productCode = reader.GetString(0);
                var fpStatus = reader.GetString(1);
                var soId = reader.IsDBNull(2) ? (long?)null : reader.GetInt64(2);
                var locationId = reader.IsDBNull(3) ? (long?)null : reader.GetInt64(3);
                if (soId != draft.SalesOrderId)
                    throw new InvalidOperationException($"Thành phẩm #{productId} không thuộc đơn đã chọn.");
                if (fpStatus is not ("IN_STOCK" or "WAITING_RECEIPT" or "RESERVED" or "DISPLAYED"))
                    throw new InvalidOperationException($"Thành phẩm #{productId} không ở trạng thái có thể xuất.");
                if (locationId != draft.SourceLocationId)
                    throw new InvalidOperationException($"Thành phẩm '{productCode}' không còn ở kho/vị trí xuất đã chọn.");
                products[productId] = new IssueProduct(
                    productCode, fpStatus, locationId.Value,
                    reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                    reader.GetDecimal(5) > 0 ? reader.GetDecimal(5) : reader.GetDecimal(6),
                    reader.GetDecimal(7), reader.GetDecimal(8), reader.GetInt64(9));
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

            var lineNo = 0;
            foreach (var productId in productIds)
            {
                lineNo++;
                var product = products[productId];
                var warehouseGoldPrice = await FirebirdWarehouseGoldPricingSupport.ResolveAsync(
                    connection, fbTx, companyId, product.GoldPurity, cancellationToken);
                var issueValue = WarehouseIssuePricing.CalculateFinishedProductIssueValue(
                    product.GoldWeightChi, warehouseGoldPrice.IssuePricePerChi,
                    product.LaborCost, product.NonGoldCost);
                await using var updateFp = new FbCommand("""
                    UPDATE FINISHED_PRODUCTS
                       SET STATUS_CODE = 'SOLD',
                           LOCATION_ID = NULL,
                           ROW_VERSION = ROW_VERSION + 1
                     WHERE ID = @ID
                       AND STATUS_CODE IN ('IN_STOCK', 'WAITING_RECEIPT', 'RESERVED', 'DISPLAYED')
                       AND LOCATION_ID = @LOCATION
                       AND ROW_VERSION = @VERSION
                    """, connection, fbTx);
                updateFp.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
                updateFp.Parameters.Add("@LOCATION", FbDbType.BigInt).Value = product.LocationId;
                updateFp.Parameters.Add("@VERSION", FbDbType.BigInt).Value = product.RowVersion;
                var affected = await updateFp.ExecuteNonQueryAsync(cancellationToken);
                if (affected != 1)
                    throw new InvalidOperationException($"Không thể xuất thành phẩm '{product.Code}' vì dữ liệu đã thay đổi.");

                await FirebirdFinishedProductTransferService.InsertDocumentLineAsync(
                    connection, fbTx, documentId, lineNo, productId,
                    product.LocationId, null, product.StatusCode, "SOLD", issueValue, cancellationToken,
                    warehouseGoldPrice.Id, warehouseGoldPrice.Code, warehouseGoldPrice.IssuePricePerChi,
                    product.GoldWeightChi, product.LaborCost, product.NonGoldCost);
                await FirebirdFinishedProductTransferService.InsertMovementAsync(
                    connection, fbTx, companyId, session.BranchId, productId,
                    "OUT", "FINISHED_GOODS_ISSUE", documentId, -1, issueValue,
                    product.LocationId, product.LocationId, null,
                    product.StatusCode, "SOLD", session.UserId, cancellationToken);
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

    public Task ReverseAsync(
        long documentId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        new FirebirdDocumentReversalService().ReverseStockDocumentAsync(
            documentId, reason, session, cancellationToken);

    private static async Task EnsureSourceLocationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long locationId,
        long branchId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM LOCATIONS
            WHERE ID = @ID AND BRANCH_ID = @BID AND IS_ACTIVE = TRUE
              AND INVENTORY_SCOPE IN ('FINISHED_GOODS', 'SALES_FLOOR')
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho xuất thành phẩm không hợp lệ hoặc không thuộc chi nhánh hiện tại.");
    }

    private sealed record IssueProduct(
        string Code,
        string StatusCode,
        long LocationId,
        string GoldPurity,
        decimal GoldWeightChi,
        decimal LaborCost,
        decimal NonGoldCost,
        long RowVersion);
}
