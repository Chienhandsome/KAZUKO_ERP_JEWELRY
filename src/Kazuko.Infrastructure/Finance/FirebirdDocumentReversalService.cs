using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdDocumentReversalService : IDocumentReversalService
{
    public async Task<DocumentReversalResult> ReverseStockDocumentAsync(
        long documentId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        EnsureReason(reason);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);

            string docType;
            string status;
            await using (var docCmd = new FbCommand("""
                SELECT DOCUMENT_TYPE, STATUS_CODE
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
                    throw new InvalidOperationException("Không tìm thấy chứng từ kho.");
                docType = reader.GetString(0);
                status = reader.GetString(1);
            }

            if (status == StockDocumentStatuses.Cancelled)
                throw new InvalidOperationException("Chứng từ đã hủy — không đảo lần hai.");
            if (status != StockDocumentStatuses.Posted)
                throw new InvalidOperationException("Chỉ hủy chứng từ đã ghi sổ.");

            await EnsureNoOpenDependentsAsync(connection, fbTx, documentId, docType, cancellationToken);

            // Movements → invert balances + insert reverse rows
            await ReverseStockMovementsAsync(
                connection, fbTx, documentId, session.UserId, cancellationToken);

            // Linked cash vouchers (stock payments)
            await CancelVouchersForStockDocumentAsync(
                connection, fbTx, documentId, reason, session.UserId, cancellationToken);

            // Settlements that referenced this stock document as source of vouchers stay linked via voucher cancel.
            // Payable debts originated by this document:
            await ReverseOriginPayableDebtsAsync(
                connection, fbTx, documentId, reason, session.UserId, cancellationToken);

            // Receivables originated by craft return compensation:
            await ReverseOriginReceivableDebtsAsync(
                connection, fbTx, documentId, reason, session.UserId, cancellationToken);

            if (docType == StockDocumentTypes.FinishedGoodsIssue)
                await ClearFinishedGoodsIssueMarkAsync(
                    connection, fbTx, companyId, documentId, session.BranchId, session.UserId, cancellationToken);

            if (docType == StockDocumentTypes.Transfer)
                await ReverseFinishedProductTransferAsync(
                    connection, fbTx, companyId, documentId, session.BranchId, session.UserId, cancellationToken);

            if (docType == StockDocumentTypes.CraftReturnReceipt)
                await ReverseCraftReturnOutputsAsync(connection, fbTx, companyId, session.BranchId, documentId, session.UserId, cancellationToken);

            if (docType == StockDocumentTypes.OutsourcedCraftIssue)
                await ReverseCraftIssueHoldingsAsync(connection, fbTx, documentId, cancellationToken);

            await MarkStockDocumentCancelledAsync(
                connection, fbTx, documentId, reason, session.UserId, cancellationToken);

            var reversalId = await InsertReversalLedgerAsync(
                connection, fbTx, companyId, session.BranchId,
                DocumentReversalSourceTypes.StockDocument, documentId, reason, session.UserId, cancellationToken);

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "STOCK_DOCUMENT_REVERSED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(),
                $"{{\"type\":\"{docType}\",\"reversalId\":{reversalId}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new DocumentReversalResult(reversalId, DocumentReversalSourceTypes.StockDocument, documentId, false);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Chứng từ đã được đảo trước đó — không đảo lần hai.", ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<DocumentReversalResult> ReverseSettlementAsync(
        long settlementId,
        string reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        EnsureReason(reason);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);

            string status;
            string settlementType;
            await using (var cmd = new FbCommand("""
                SELECT STATUS_CODE, SETTLEMENT_TYPE
                FROM FINANCIAL_SETTLEMENTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                cmd.Parameters.Add("@ID", FbDbType.BigInt).Value = settlementId;
                cmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                cmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy settlement.");
                status = reader.GetString(0);
                settlementType = reader.GetString(1);
            }

            if (status == SettlementStatuses.Cancelled)
                throw new InvalidOperationException("Settlement đã hủy — không đảo lần hai.");
            if (status != SettlementStatuses.Posted)
                throw new InvalidOperationException("Chỉ hủy settlement đã ghi sổ.");

            // Reverse applications first (debt / order effects)
            await using (var appCmd = new FbCommand("""
                SELECT TARGET_TYPE, TARGET_ID, AMOUNT_VND
                FROM FINANCIAL_SETTLEMENT_APPLICATIONS
                WHERE SETTLEMENT_ID = @SID
                """, connection, fbTx))
            {
                appCmd.Parameters.Add("@SID", FbDbType.BigInt).Value = settlementId;
                await using var reader = await appCmd.ExecuteReaderAsync(cancellationToken);
                var apps = new List<(string Type, long Id, decimal Amount)>();
                while (await reader.ReadAsync(cancellationToken))
                    apps.Add((reader.GetString(0), reader.GetInt64(1), reader.GetDecimal(2)));

                await reader.DisposeAsync();
                foreach (var app in apps)
                {
                    if (app.Type == SettlementApplicationTargetTypes.PayableDebt)
                        await UndoPayableApplicationAsync(connection, fbTx, app.Id, app.Amount, session.UserId, cancellationToken);
                    else if (app.Type == SettlementApplicationTargetTypes.ReceivableDebt)
                        await UndoReceivableApplicationAsync(connection, fbTx, app.Id, app.Amount, session.UserId, cancellationToken);
                    else if (app.Type == SettlementApplicationTargetTypes.SalesOrder)
                        await VoidOrderPaymentsForSettlementAsync(connection, fbTx, app.Id, settlementId, cancellationToken);
                }
            }

            // Cancel linked vouchers
            await using (var allocCmd = new FbCommand("""
                SELECT CASH_VOUCHER_ID FROM FINANCIAL_SETTLEMENT_ALLOCATIONS
                WHERE SETTLEMENT_ID = @SID AND CASH_VOUCHER_ID IS NOT NULL
                """, connection, fbTx))
            {
                allocCmd.Parameters.Add("@SID", FbDbType.BigInt).Value = settlementId;
                await using var reader = await allocCmd.ExecuteReaderAsync(cancellationToken);
                var voucherIds = new List<long>();
                while (await reader.ReadAsync(cancellationToken))
                    voucherIds.Add(reader.GetInt64(0));
                await reader.DisposeAsync();
                foreach (var voucherId in voucherIds)
                    await CancelCashVoucherAsync(connection, fbTx, voucherId, reason, session.UserId, cancellationToken);
            }

            await using (var cancel = new FbCommand("""
                UPDATE FINANCIAL_SETTLEMENTS
                   SET STATUS_CODE = 'CANCELLED',
                       CANCELLED_BY = @BY,
                       CANCELLED_AT = CURRENT_TIMESTAMP,
                       CANCELLATION_REASON = @REASON
                 WHERE ID = @ID AND STATUS_CODE = 'POSTED'
                """, connection, fbTx))
            {
                cancel.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                cancel.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
                cancel.Parameters.Add("@ID", FbDbType.BigInt).Value = settlementId;
                if (await cancel.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không thể hủy settlement — trạng thái đã đổi.");
            }

            var reversalId = await InsertReversalLedgerAsync(
                connection, fbTx, companyId, session.BranchId,
                DocumentReversalSourceTypes.FinancialSettlement, settlementId, reason, session.UserId, cancellationToken);

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "FINANCIAL_SETTLEMENT_REVERSED",
                session.UserId, session.Username, session.BranchId,
                "FINANCIAL_SETTLEMENT", settlementId.ToString(),
                $"{{\"settlementType\":\"{settlementType}\",\"reversalId\":{reversalId}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new DocumentReversalResult(
                reversalId, DocumentReversalSourceTypes.FinancialSettlement, settlementId, false);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Settlement đã được đảo trước đó — không đảo lần hai.", ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Phải nhập lý do hủy/đảo.");
    }

    private static async Task EnsureNoOpenDependentsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        string docType,
        CancellationToken cancellationToken)
    {
        // Child stock documents still POSTED that reference this as source issue.
        await using (var child = new FbCommand("""
            SELECT COUNT(*) FROM STOCK_DOCUMENTS
            WHERE SOURCE_ISSUE_DOCUMENT_ID = @ID AND STATUS_CODE = 'POSTED'
            """, connection, transaction))
        {
            child.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
            var count = Convert.ToInt32(await child.ExecuteScalarAsync(cancellationToken));
            if (count > 0)
                throw new InvalidOperationException(
                    "Không hủy được: còn chứng từ phụ thuộc (phiếu nhập chế tác/liên kết) chưa đảo.");
        }

        if (docType == StockDocumentTypes.Receipt)
        {
            await using var paid = new FbCommand("""
                SELECT COUNT(*)
                FROM PAYABLE_DEBTS D
                JOIN PAYABLE_DEBT_PAYMENTS P ON P.DEBT_ID = D.ID
                WHERE D.ORIGIN_DOCUMENT_TYPE = 'STOCK_DOCUMENT'
                  AND D.ORIGIN_DOCUMENT_ID = @ID
                  AND D.STATUS_CODE <> 'CANCELLED'
                """, connection, transaction);
            paid.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
            if (Convert.ToInt32(await paid.ExecuteScalarAsync(cancellationToken)) > 0)
                throw new InvalidOperationException(
                    "Không hủy được: công nợ phát sinh từ phiếu đã có thanh toán — hãy đảo settlement/thanh toán trước.");
        }

        if (docType == StockDocumentTypes.CraftReturnReceipt)
        {
            await using var sold = new FbCommand("""
                SELECT COUNT(*)
                FROM STOCK_CRAFT_OUTPUT_LINES O
                JOIN FINISHED_PRODUCTS F ON F.ID = O.FINISHED_PRODUCT_ID
                WHERE O.RETURN_DOCUMENT_ID = @ID AND F.STATUS_CODE = 'SOLD'
                """, connection, transaction);
            sold.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
            if (Convert.ToInt32(await sold.ExecuteScalarAsync(cancellationToken)) > 0)
                throw new InvalidOperationException(
                    "Không hủy được: thành phẩm từ phiếu nhập chế tác đã xuất bán — hãy đảo phiếu xuất TP trước.");
        }
    }

    private static async Task ReverseStockMovementsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        long userId,
        CancellationToken cancellationToken)
    {
        var rows = new List<(long Id, long? LineId, long MaterialId, long LocationId, string Type, decimal Qty, decimal Cost)>();
        await using (var cmd = new FbCommand("""
            SELECT ID, DOCUMENT_LINE_ID, MATERIAL_ID, LOCATION_ID, MOVEMENT_TYPE, QUANTITY_CHANGE, UNIT_COST
            FROM STOCK_MOVEMENTS
            WHERE DOCUMENT_ID = @DOC AND REVERSAL_OF_ID IS NULL
            ORDER BY ID
            """, connection, transaction))
        {
            cmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? null : reader.GetInt64(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetString(4),
                    reader.GetDecimal(5),
                    reader.GetDecimal(6)));
            }
        }

        foreach (var row in rows)
        {
            // Invert stock: original +qty → -qty
            await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                connection, transaction, row.MaterialId, row.LocationId,
                -row.Qty, row.Cost, cancellationToken);

            await using var insert = new FbCommand("""
                INSERT INTO STOCK_MOVEMENTS
                    (DOCUMENT_ID, DOCUMENT_LINE_ID, MATERIAL_ID, LOCATION_ID, MOVEMENT_TYPE,
                     OCCURRED_AT, QUANTITY_CHANGE, UNIT_COST, VALUE_CHANGE, REVERSAL_OF_ID, CREATED_BY)
                VALUES
                    (@DOC, @LINE, @MID, @LID, @TYPE,
                     CURRENT_TIMESTAMP, @QTY, @COST, @VALUE, @REV, @BY)
                """, connection, transaction);
            insert.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            insert.Parameters.Add("@LINE", FbDbType.BigInt).Value = row.LineId is > 0 ? row.LineId.Value : DBNull.Value;
            insert.Parameters.Add("@MID", FbDbType.BigInt).Value = row.MaterialId;
            insert.Parameters.Add("@LID", FbDbType.BigInt).Value = row.LocationId;
            insert.Parameters.Add("@TYPE", FbDbType.VarChar, 20).Value = row.Qty > 0 ? "OUT" : "IN";
            insert.Parameters.Add("@QTY", FbDbType.Decimal).Value = -row.Qty;
            insert.Parameters.Add("@COST", FbDbType.Decimal).Value = row.Cost;
            insert.Parameters.Add("@VALUE", FbDbType.Decimal).Value = -row.Qty * row.Cost;
            insert.Parameters.Add("@REV", FbDbType.BigInt).Value = row.Id;
            insert.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task CancelVouchersForStockDocumentAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        string reason,
        long userId,
        CancellationToken cancellationToken)
    {
        var ids = new List<long>();
        await using (var cmd = new FbCommand("""
            SELECT CASH_VOUCHER_ID FROM STOCK_DOCUMENT_PAYMENTS
            WHERE DOCUMENT_ID = @DOC AND CASH_VOUCHER_ID IS NOT NULL
            """, connection, transaction))
        {
            cmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(reader.GetInt64(0));
        }

        foreach (var id in ids)
            await CancelCashVoucherAsync(connection, transaction, id, reason, userId, cancellationToken);
    }

    private static async Task CancelCashVoucherAsync(
        FbConnection connection,
        FbTransaction transaction,
        long voucherId,
        string reason,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            UPDATE CASH_VOUCHERS
               SET STATUS_CODE = 'CANCELLED',
                   CANCELLED_BY = @BY,
                   CANCELLED_AT = CURRENT_TIMESTAMP,
                   CANCELLATION_REASON = @REASON
             WHERE ID = @ID AND STATUS_CODE = 'POSTED'
            """, connection, transaction);
        cmd.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        cmd.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
        cmd.Parameters.Add("@ID", FbDbType.BigInt).Value = voucherId;
        var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
        if (affected == 0)
        {
            await using var check = new FbCommand(
                "SELECT STATUS_CODE FROM CASH_VOUCHERS WHERE ID = @ID", connection, transaction);
            check.Parameters.Add("@ID", FbDbType.BigInt).Value = voucherId;
            var status = Convert.ToString(await check.ExecuteScalarAsync(cancellationToken));
            if (status == FinancialVoucherStatuses.Cancelled)
                return; // already cancelled in same chain — ok
            throw new InvalidOperationException($"Không hủy được phiếu thu/chi #{voucherId}.");
        }
    }

    private static async Task ReverseOriginPayableDebtsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        string reason,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            UPDATE PAYABLE_DEBTS
               SET STATUS_CODE = 'CANCELLED',
                   UPDATED_BY = @BY,
                   UPDATED_AT = CURRENT_TIMESTAMP,
                   NOTES = COALESCE(NOTES, '') || ' | REVERSED: ' || @REASON
             WHERE ORIGIN_DOCUMENT_TYPE = 'STOCK_DOCUMENT'
               AND ORIGIN_DOCUMENT_ID = @DOC
               AND STATUS_CODE IN ('OPEN')
               AND PAID_AMOUNT_VND = 0
            """, connection, transaction);
        cmd.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        cmd.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
        cmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ReverseOriginReceivableDebtsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        string reason,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            UPDATE RECEIVABLE_DEBTS
               SET STATUS_CODE = 'CANCELLED',
                   UPDATED_BY = @BY,
                   UPDATED_AT = CURRENT_TIMESTAMP,
                   NOTES = COALESCE(NOTES, '') || ' | REVERSED: ' || @REASON
             WHERE ORIGIN_DOCUMENT_TYPE = 'STOCK_DOCUMENT'
               AND ORIGIN_DOCUMENT_ID = @DOC
               AND STATUS_CODE IN ('OPEN')
               AND COLLECTED_AMOUNT_VND = 0
            """, connection, transaction);
        cmd.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        cmd.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
        cmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UndoPayableApplicationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long debtId,
        decimal amount,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            UPDATE PAYABLE_DEBTS
               SET PAID_AMOUNT_VND = PAID_AMOUNT_VND - @AMT,
                   REMAINING_AMOUNT_VND = REMAINING_AMOUNT_VND + @AMT,
                   STATUS_CODE = CASE
                       WHEN PAID_AMOUNT_VND - @AMT <= 0 THEN 'OPEN'
                       ELSE 'PARTIAL' END,
                   UPDATED_BY = @BY,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID AND STATUS_CODE IN ('OPEN', 'PARTIAL', 'PAID')
            """, connection, transaction);
        cmd.Parameters.Add("@AMT", FbDbType.Decimal).Value = amount;
        cmd.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        cmd.Parameters.Add("@ID", FbDbType.BigInt).Value = debtId;
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Không hoàn được phân bổ công nợ phải trả #{debtId}.");
    }

    private static async Task UndoReceivableApplicationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long debtId,
        decimal amount,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            UPDATE RECEIVABLE_DEBTS
               SET COLLECTED_AMOUNT_VND = COLLECTED_AMOUNT_VND - @AMT,
                   REMAINING_AMOUNT_VND = REMAINING_AMOUNT_VND + @AMT,
                   STATUS_CODE = CASE
                       WHEN COLLECTED_AMOUNT_VND - @AMT <= 0 THEN 'OPEN'
                       ELSE 'PARTIAL' END,
                   UPDATED_BY = @BY,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID AND STATUS_CODE IN ('OPEN', 'PARTIAL', 'PAID')
            """, connection, transaction);
        cmd.Parameters.Add("@AMT", FbDbType.Decimal).Value = amount;
        cmd.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        cmd.Parameters.Add("@ID", FbDbType.BigInt).Value = debtId;
        if (await cmd.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException($"Không hoàn được phân bổ phải thu #{debtId}.");
    }

    private static async Task VoidOrderPaymentsForSettlementAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        long settlementId,
        CancellationToken cancellationToken)
    {
        // Settlement code was stored in REFERENCE_NO when collecting customer debt.
        string? settlementCode = null;
        await using (var codeCmd = new FbCommand(
            "SELECT CODE FROM FINANCIAL_SETTLEMENTS WHERE ID = @ID", connection, transaction))
        {
            codeCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = settlementId;
            settlementCode = Convert.ToString(await codeCmd.ExecuteScalarAsync(cancellationToken));
        }

        await using var voidCmd = new FbCommand("""
            UPDATE ORDER_PAYMENTS
               SET IS_VOIDED = TRUE
             WHERE ORDER_ID = @OID
               AND IS_VOIDED = FALSE
               AND (@CODE IS NULL OR REFERENCE_NO = @CODE)
            """, connection, transaction);
        voidCmd.Parameters.Add("@OID", FbDbType.BigInt).Value = orderId;
        voidCmd.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value =
            (object?)settlementCode ?? DBNull.Value;
        await voidCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ClearFinishedGoodsIssueMarkAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long documentId,
        long branchId,
        long userId,
        CancellationToken cancellationToken)
    {
        long? salesOrderId = null;
        long? documentSourceLocationId = null;
        await using (var cmd = new FbCommand(
            "SELECT SALES_ORDER_ID, SOURCE_LOCATION_ID FROM STOCK_DOCUMENTS WHERE ID = @ID", connection, transaction))
        {
            cmd.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                if (!reader.IsDBNull(0))
                    salesOrderId = reader.GetInt64(0);
                if (!reader.IsDBNull(1))
                    documentSourceLocationId = reader.GetInt64(1);
            }
        }

        if (salesOrderId is not > 0)
            return;

        await using var clearSo = new FbCommand("""
            UPDATE SALES_ORDERS
               SET FINISHED_GOODS_ISSUE_DOCUMENT_ID = NULL,
                   FINISHED_GOODS_ISSUED_AT = NULL,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID AND FINISHED_GOODS_ISSUE_DOCUMENT_ID = @DOC
            """, connection, transaction);
        clearSo.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId.Value;
        clearSo.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        await clearSo.ExecuteNonQueryAsync(cancellationToken);

        var products = new List<(long ProductId, long SourceLocationId, string StatusBefore, decimal UnitCost)>();
        await using (var lineCmd = new FbCommand("""
            SELECT FINISHED_PRODUCT_ID, SOURCE_LOCATION_ID, STATUS_BEFORE, UNIT_COST
            FROM FINISHED_PRODUCT_DOCUMENT_LINES
            WHERE DOCUMENT_ID = @DOC
            ORDER BY LINE_NUMBER
            """, connection, transaction))
        {
            lineCmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            await using var reader = await lineCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                products.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetDecimal(3)));
        }

        // Chứng từ tạo trước migration 0040 chưa lưu từng serial trong bảng dòng thành phẩm.
        // Giữ khả năng hoàn tác cũ bằng cách khôi phục các món SOLD của chính đơn hàng về kho nguồn chứng từ.
        if (products.Count == 0 && documentSourceLocationId is > 0)
        {
            await using var legacyProducts = new FbCommand("""
                SELECT ID, COALESCE(TOTAL_COST, 0)
                FROM FINISHED_PRODUCTS
                WHERE SALES_ORDER_ID = @SO AND STATUS_CODE = 'SOLD'
                  AND LOCATION_ID IS NULL AND BRANCH_ID = @BID
                ORDER BY ID
                WITH LOCK
                """, connection, transaction);
            legacyProducts.Parameters.Add("@SO", FbDbType.BigInt).Value = salesOrderId.Value;
            legacyProducts.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            await using var legacyReader = await legacyProducts.ExecuteReaderAsync(cancellationToken);
            while (await legacyReader.ReadAsync(cancellationToken))
                products.Add((legacyReader.GetInt64(0), documentSourceLocationId.Value, "IN_STOCK", legacyReader.GetDecimal(1)));
        }

        foreach (var product in products)
        {
            await using var restoreFp = new FbCommand("""
                UPDATE FINISHED_PRODUCTS
                   SET STATUS_CODE = @STATUS,
                       LOCATION_ID = @LOCATION,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID AND STATUS_CODE = 'SOLD' AND LOCATION_ID IS NULL AND BRANCH_ID = @BID
                """, connection, transaction);
            restoreFp.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = product.StatusBefore;
            restoreFp.Parameters.Add("@LOCATION", FbDbType.BigInt).Value = product.SourceLocationId;
            restoreFp.Parameters.Add("@ID", FbDbType.BigInt).Value = product.ProductId;
            restoreFp.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            if (await restoreFp.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException($"Không thể hoàn tồn thành phẩm #{product.ProductId}.");

            await FirebirdFinishedProductTransferService.InsertMovementAsync(
                connection, transaction, companyId, branchId, product.ProductId,
                "IN", "FINISHED_GOODS_ISSUE", documentId, 1m, product.UnitCost,
                product.SourceLocationId, null, product.SourceLocationId,
                "SOLD", product.StatusBefore, userId, cancellationToken);
        }
    }

    private static async Task ReverseFinishedProductTransferAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long documentId,
        long branchId,
        long userId,
        CancellationToken cancellationToken)
    {
        var products = new List<(long ProductId, long SourceId, long DestinationId, string Before, string After, decimal Cost)>();
        await using (var command = new FbCommand("""
            SELECT FINISHED_PRODUCT_ID, SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID,
                   STATUS_BEFORE, STATUS_AFTER, UNIT_COST
            FROM FINISHED_PRODUCT_DOCUMENT_LINES
            WHERE DOCUMENT_ID = @DOC AND DESTINATION_LOCATION_ID IS NOT NULL
            ORDER BY LINE_NUMBER
            """, connection, transaction))
        {
            command.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                products.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2),
                    reader.GetString(3), reader.GetString(4), reader.GetDecimal(5)));
        }

        foreach (var product in products)
        {
            await using var update = new FbCommand("""
                UPDATE FINISHED_PRODUCTS
                   SET LOCATION_ID = @SOURCE,
                       STATUS_CODE = @BEFORE,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID AND BRANCH_ID = @BID
                   AND LOCATION_ID = @DESTINATION AND STATUS_CODE = @AFTER
                """, connection, transaction);
            update.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = product.SourceId;
            update.Parameters.Add("@BEFORE", FbDbType.VarChar, 30).Value = product.Before;
            update.Parameters.Add("@ID", FbDbType.BigInt).Value = product.ProductId;
            update.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            update.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = product.DestinationId;
            update.Parameters.Add("@AFTER", FbDbType.VarChar, 30).Value = product.After;
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException($"Không thể hoàn chuyển thành phẩm #{product.ProductId}.");

            await FirebirdFinishedProductTransferService.InsertMovementAsync(
                connection, transaction, companyId, branchId, product.ProductId,
                "OUT", "TRANSFER", documentId, -1m, product.Cost,
                product.DestinationId, product.DestinationId, product.SourceId,
                product.After, product.Before, userId, cancellationToken);
            await FirebirdFinishedProductTransferService.InsertMovementAsync(
                connection, transaction, companyId, branchId, product.ProductId,
                "IN", "TRANSFER", documentId, 1m, product.Cost,
                product.SourceId, product.DestinationId, product.SourceId,
                product.After, product.Before, userId, cancellationToken);
        }
    }

    private static async Task ReverseCraftReturnOutputsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long documentId,
        long userId,
        CancellationToken cancellationToken)
    {
        var products = new List<(long ProductId, long LocationId, decimal Qty, decimal Cost)>();
        await using (var cmd = new FbCommand("""
            SELECT FINISHED_PRODUCT_ID, DESTINATION_LOCATION_ID, QUANTITY, TOTAL_COST_VND
            FROM STOCK_CRAFT_OUTPUT_LINES
            WHERE RETURN_DOCUMENT_ID = @DOC
            """, connection, transaction))
        {
            cmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                products.Add((reader.GetInt64(0), reader.GetInt64(1), reader.GetDecimal(2), reader.GetDecimal(3)));
        }

        foreach (var p in products)
        {
            await using var upd = new FbCommand("""
                UPDATE FINISHED_PRODUCTS
                   SET STATUS_CODE = 'WAITING_RECEIPT'
                 WHERE ID = @ID AND STATUS_CODE = 'IN_STOCK'
                """, connection, transaction);
            upd.Parameters.Add("@ID", FbDbType.BigInt).Value = p.ProductId;
            await upd.ExecuteNonQueryAsync(cancellationToken);

            await using var move = new FbCommand("""
                INSERT INTO FINISHED_PRODUCT_MOVEMENTS
                    (COMPANY_ID, BRANCH_ID, FINISHED_PRODUCT_ID, MOVEMENT_TYPE,
                     DOCUMENT_TYPE, DOCUMENT_ID, OCCURRED_AT, QUANTITY_CHANGE,
                     UNIT_COST, VALUE_CHANGE, LOCATION_ID, STATUS_BEFORE, STATUS_AFTER,
                     NOTES, CREATED_BY)
                VALUES
                    (@CID, @BID, @FP, 'OUT',
                     'CRAFT_RETURN_RECEIPT', @DOC, CURRENT_TIMESTAMP, @QTY,
                     @COST, @VALUE, @LOC, 'IN_STOCK', 'WAITING_RECEIPT',
                     'REVERSAL', @BY)
                """, connection, transaction);
            move.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            move.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            move.Parameters.Add("@FP", FbDbType.BigInt).Value = p.ProductId;
            move.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            move.Parameters.Add("@QTY", FbDbType.Decimal).Value = -p.Qty;
            move.Parameters.Add("@COST", FbDbType.Decimal).Value = p.Cost;
            move.Parameters.Add("@VALUE", FbDbType.Decimal).Value = -p.Cost;
            move.Parameters.Add("@LOC", FbDbType.BigInt).Value = p.LocationId;
            move.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
            await move.ExecuteNonQueryAsync(cancellationToken);
        }

        // Restore holdings quantities from settlement lines
        await using var holdings = new FbCommand("""
            SELECT HOLDING_ID, QUANTITY_IN_PRODUCT, QUANTITY_RETURNED, QUANTITY_SCRAP, QUANTITY_LOSS
            FROM STOCK_CRAFT_SETTLEMENT_LINES
            WHERE RETURN_DOCUMENT_ID = @DOC
            """, connection, transaction);
        holdings.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        await using var hReader = await holdings.ExecuteReaderAsync(cancellationToken);
        var holdRows = new List<(long Id, decimal Prod, decimal Ret, decimal Scrap, decimal Loss)>();
        while (await hReader.ReadAsync(cancellationToken))
            holdRows.Add((hReader.GetInt64(0), hReader.GetDecimal(1), hReader.GetDecimal(2), hReader.GetDecimal(3), hReader.GetDecimal(4)));
        await hReader.DisposeAsync();

        foreach (var h in holdRows)
        {
            await using var undo = new FbCommand("""
                UPDATE EXTERNAL_WORKER_HOLDINGS
                   SET QUANTITY_IN_PRODUCT = QUANTITY_IN_PRODUCT - @PROD,
                       QUANTITY_RETURNED = QUANTITY_RETURNED - @RET,
                       QUANTITY_SCRAP = QUANTITY_SCRAP - @SCRAP,
                       QUANTITY_LOSS = QUANTITY_LOSS - @LOSS,
                       STATUS_CODE = 'IN_PROGRESS',
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                """, connection, transaction);
            undo.Parameters.Add("@PROD", FbDbType.Decimal).Value = h.Prod;
            undo.Parameters.Add("@RET", FbDbType.Decimal).Value = h.Ret;
            undo.Parameters.Add("@SCRAP", FbDbType.Decimal).Value = h.Scrap;
            undo.Parameters.Add("@LOSS", FbDbType.Decimal).Value = h.Loss;
            undo.Parameters.Add("@ID", FbDbType.BigInt).Value = h.Id;
            await undo.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReverseCraftIssueHoldingsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            UPDATE EXTERNAL_WORKER_HOLDINGS
               SET STATUS_CODE = 'SETTLED',
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ISSUE_DOCUMENT_ID = @DOC
               AND QUANTITY_RETURNED = 0 AND QUANTITY_IN_PRODUCT = 0
               AND QUANTITY_SCRAP = 0 AND QUANTITY_LOSS = 0
            """, connection, transaction);
        cmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkStockDocumentCancelledAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        string reason,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cancel = new FbCommand("""
            UPDATE STOCK_DOCUMENTS
               SET STATUS_CODE = 'CANCELLED',
                   CANCELLED_BY = @BY,
                   CANCELLED_AT = CURRENT_TIMESTAMP,
                   CANCELLATION_REASON = @REASON,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID AND STATUS_CODE = 'POSTED'
            """, connection, transaction);
        cancel.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        cancel.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
        cancel.Parameters.Add("@ID", FbDbType.BigInt).Value = documentId;
        if (await cancel.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("Không thể hủy — chứng từ đã thay đổi.");
    }

    private static async Task<long> InsertReversalLedgerAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        string sourceType,
        long sourceId,
        string reason,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var cmd = new FbCommand("""
            INSERT INTO DOCUMENT_REVERSALS
                (COMPANY_ID, BRANCH_ID, SOURCE_DOCUMENT_TYPE, SOURCE_DOCUMENT_ID,
                 REASON, REVERSED_BY, REVERSED_AT)
            VALUES
                (@CID, @BID, @TYPE, @SID, @REASON, @BY, CURRENT_TIMESTAMP)
            RETURNING ID
            """, connection, transaction);
        cmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        cmd.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        cmd.Parameters.Add("@TYPE", FbDbType.VarChar, 40).Value = sourceType;
        cmd.Parameters.Add("@SID", FbDbType.BigInt).Value = sourceId;
        cmd.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value = reason.Trim();
        cmd.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    private static bool IsUniqueViolation(FbException ex) =>
        ex.ErrorCode == 335544665
        || (ex.Message?.Contains("UQ_DREV_SOURCE", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.Message?.Contains("violation of PRIMARY or UNIQUE KEY", StringComparison.OrdinalIgnoreCase) ?? false);
}
