using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public static class FinishedGoodsPostingSupport
{
    public static Task<long> PostRetailIssueAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        AuthenticatedSession session, long salesOrderId, long sourceLocationId,
        IReadOnlyList<long> finishedProductIds, CancellationToken cancellationToken) =>
        PostAsync(connection, transaction, companyId, session, salesOrderId,
            sourceLocationId, DateTime.Now, null, null, null, finishedProductIds,
            retailOnly: true, requireExistingOrderAssignment: false, cancellationToken);

    internal static Task<long> PostExistingOrderIssueAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        AuthenticatedSession session, FinishedGoodsIssueDraft draft,
        CancellationToken cancellationToken) =>
        PostAsync(connection, transaction, companyId, session, draft.SalesOrderId,
            draft.SourceLocationId, draft.DocumentDate, draft.DeliveredByName,
            draft.ReceivedByName, draft.Notes,
            draft.Lines.Select(x => x.FinishedProductId).Distinct().ToArray(),
            retailOnly: false, requireExistingOrderAssignment: true, cancellationToken);

    private static async Task<long> PostAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        AuthenticatedSession session, long salesOrderId, long sourceLocationId,
        DateTime documentDate, string? deliveredByName, string? receivedByName,
        string? notes, IReadOnlyList<long> finishedProductIds, bool retailOnly,
        bool requireExistingOrderAssignment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(session);
        if (finishedProductIds.Count == 0)
            throw new InvalidOperationException("Phiếu xuất phải có ít nhất một thành phẩm.");

        await EnsureSourceLocationAsync(connection, transaction, companyId, session.BranchId,
            sourceLocationId, cancellationToken);

        var products = new List<IssueProduct>(finishedProductIds.Count);
        foreach (var productId in finishedProductIds)
        {
            await using var command = new FbCommand("""
                SELECT CODE, STATUS_CODE, SALES_ORDER_ID, LOCATION_ID,
                       COALESCE(GOLD_PURITY, ''), GOLD_WEIGHT, TOTAL_WEIGHT, LABOR_COST,
                       NON_GOLD_MATERIAL_COST, ROW_VERSION, GOLD_TYPE_ID
                FROM FINISHED_PRODUCTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, transaction);
            command.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
            command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException($"Không tìm thấy thành phẩm #{productId}.");

            var product = new IssueProduct(
                productId, reader.GetString(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetInt64(2),
                reader.IsDBNull(3) ? null : reader.GetInt64(3), reader.GetString(4),
                reader.GetDecimal(5) > 0 ? reader.GetDecimal(5) : reader.GetDecimal(6),
                reader.GetDecimal(7), reader.GetDecimal(8), reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetInt64(10));

            var allowed = retailOnly
                ? product.StatusCode is "IN_STOCK" or "DISPLAYED"
                : product.StatusCode is "IN_STOCK" or "WAITING_RECEIPT" or "RESERVED" or "DISPLAYED";
            if (!allowed)
                throw new InvalidOperationException($"Thành phẩm '{product.Code}' không ở trạng thái có thể xuất.");
            if (product.LocationId != sourceLocationId)
                throw new InvalidOperationException($"Thành phẩm '{product.Code}' không còn ở kho/vị trí xuất đã chọn.");
            if (requireExistingOrderAssignment && product.SalesOrderId != salesOrderId)
                throw new InvalidOperationException($"Thành phẩm #{productId} không thuộc đơn đã chọn.");
            products.Add(product);
        }

        var code = await StockPostingSupport.NextDocumentCodeAsync(
            connection, transaction, "FINISHED_GOODS_ISSUE", session.BranchId, "XTP", cancellationToken);
        long documentId;
        await using (var insert = new FbCommand("""
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
            """, connection, transaction))
        {
            insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
            insert.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = documentDate;
            insert.Parameters.Add("@LOC", FbDbType.BigInt).Value = sourceLocationId;
            insert.Parameters.Add("@SO", FbDbType.BigInt).Value = salesOrderId;
            insert.Parameters.Add("@DEL", FbDbType.VarChar, 150).Value = (object?)deliveredByName ?? DBNull.Value;
            insert.Parameters.Add("@REC", FbDbType.VarChar, 150).Value = (object?)receivedByName ?? DBNull.Value;
            insert.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value = (object?)notes ?? DBNull.Value;
            insert.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            documentId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        }

        var lineNumber = 0;
        foreach (var product in products)
        {
            lineNumber++;
            var warehousePrice = await FirebirdWarehouseGoldPricingSupport.ResolveAsync(
                connection, transaction, companyId, product.GoldTypeId, product.GoldPurity, cancellationToken);
            var issueValue = WarehouseIssuePricing.CalculateFinishedProductIssueValue(
                product.GoldWeightChi, warehousePrice.IssuePricePerChi, product.LaborCost, product.NonGoldCost);

            await using (var update = new FbCommand($"""
                UPDATE FINISHED_PRODUCTS
                   SET STATUS_CODE = 'SOLD', LOCATION_ID = NULL,
                       SALES_ORDER_ID = @SO, ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                   AND LOCATION_ID = @LOCATION AND ROW_VERSION = @VERSION
                   AND STATUS_CODE IN ({(retailOnly ? "'IN_STOCK', 'DISPLAYED'" : "'IN_STOCK', 'WAITING_RECEIPT', 'RESERVED', 'DISPLAYED'")})
                """, connection, transaction))
            {
                update.Parameters.Add("@SO", FbDbType.BigInt).Value = salesOrderId;
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = product.Id;
                update.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                update.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                update.Parameters.Add("@LOCATION", FbDbType.BigInt).Value = sourceLocationId;
                update.Parameters.Add("@VERSION", FbDbType.BigInt).Value = product.RowVersion;
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException($"Thành phẩm '{product.Code}' vừa được giao dịch ở máy khác.");
            }

            await FirebirdFinishedProductTransferService.InsertDocumentLineAsync(
                connection, transaction, documentId, lineNumber, product.Id,
                sourceLocationId, null, product.StatusCode, "SOLD", issueValue, cancellationToken,
                warehousePrice.Id, warehousePrice.Code, warehousePrice.IssuePricePerChi,
                product.GoldWeightChi, product.LaborCost, product.NonGoldCost);
            await FirebirdFinishedProductTransferService.InsertMovementAsync(
                connection, transaction, companyId, session.BranchId, product.Id,
                "OUT", "FINISHED_GOODS_ISSUE", documentId, -1, issueValue,
                sourceLocationId, sourceLocationId, null, product.StatusCode, "SOLD",
                session.UserId, cancellationToken);
        }

        await using (var updateOrder = new FbCommand("""
            UPDATE SALES_ORDERS
               SET FINISHED_GOODS_ISSUE_DOCUMENT_ID = @DOC,
                   FINISHED_GOODS_ISSUED_AT = CURRENT_TIMESTAMP,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
               AND FINISHED_GOODS_ISSUE_DOCUMENT_ID IS NULL
            """, connection, transaction))
        {
            updateOrder.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            updateOrder.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
            updateOrder.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            updateOrder.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            if (await updateOrder.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Đơn đã được xuất bởi phiên khác.");
        }

        await AuditLogWriter.WriteAsync(connection, transaction, "FINISHED_GOODS_ISSUE_POSTED",
            session.UserId, session.Username, session.BranchId, "STOCK_DOCUMENT",
            documentId.ToString(), code, cancellationToken);
        return documentId;
    }

    private static async Task EnsureSourceLocationAsync(
        FbConnection connection, FbTransaction transaction, long companyId, long branchId,
        long locationId, CancellationToken cancellationToken)
    {
        // Retail checkout and warehouse FG issue both allow FINISHED_GOODS | SALES_FLOOR
        // (aligned with SalesCheckoutPostingSupport.EnsureSalesLocationAsync).
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM LOCATIONS L
            JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @CID
            WHERE L.ID = @ID AND L.BRANCH_ID = @BID AND L.IS_ACTIVE = TRUE
              AND L.INVENTORY_SCOPE IN ('FINISHED_GOODS', 'SALES_FLOOR')
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho xuất thành phẩm không hợp lệ hoặc không thuộc chi nhánh hiện tại.");
    }

    private sealed record IssueProduct(
        long Id, string Code, string StatusCode, long? SalesOrderId, long? LocationId,
        string GoldPurity, decimal GoldWeightChi, decimal LaborCost, decimal NonGoldCost, long RowVersion,
        long? GoldTypeId);
}
