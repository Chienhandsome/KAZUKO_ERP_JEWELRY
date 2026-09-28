using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdStockAdjustmentService : IStockAdjustmentService
{
    public async Task<long> CreateAndPostAsync(
        StockAdjustmentDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        if (draft.LocationId <= 0)
            throw new InvalidOperationException("Phải chọn kho điều chỉnh.");
        if (string.IsNullOrWhiteSpace(draft.Reason))
            throw new InvalidOperationException("Điều chỉnh kho bắt buộc có lý do.");
        if (draft.Lines is null || draft.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu điều chỉnh phải có ít nhất một dòng.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "STOCK_ADJUSTMENT", session.BranchId, "DC", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID, PURPOSE_CODE, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'STOCK_ADJUSTMENT', @DATE,
                     @LOC, @LOC, 'STOCK_ADJUST', @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@LOC", FbDbType.BigInt).Value = draft.LocationId;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    $"{draft.Reason.Trim()}" + (string.IsNullOrWhiteSpace(draft.Notes) ? string.Empty : $" | {draft.Notes.Trim()}");
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var lineNo = 0;
            foreach (var line in draft.Lines)
            {
                lineNo++;
                if (line.MaterialId <= 0 || line.QuantityChange == 0)
                    throw new InvalidOperationException($"Dòng {lineNo}: NVL/số lượng điều chỉnh không hợp lệ.");

                var qty = draft.IsIncrease
                    ? Math.Abs(line.QuantityChange)
                    : -Math.Abs(line.QuantityChange);
                if (draft.IsIncrease && line.QuantityChange < 0)
                    throw new InvalidOperationException($"Dòng {lineNo}: phiếu tăng tồn không được nhập số âm.");
                if (!draft.IsIncrease && line.QuantityChange > 0)
                    qty = -line.QuantityChange;

                var material = await StockPostingSupport.GetMaterialAsync(
                    connection, fbTx, companyId, line.MaterialId, cancellationToken);

                decimal unitCost = 0;
                await using (var costCmd = new FbCommand("""
                    SELECT COALESCE(AVERAGE_COST, 0) FROM STOCK_BALANCES
                    WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
                    """, connection, fbTx))
                {
                    costCmd.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    costCmd.Parameters.Add("@LID", FbDbType.BigInt).Value = draft.LocationId;
                    var costObj = await costCmd.ExecuteScalarAsync(cancellationToken);
                    unitCost = costObj is null or DBNull ? 0 : Convert.ToDecimal(costObj);
                }

                await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                    connection, fbTx, line.MaterialId, draft.LocationId, qty, unitCost, cancellationToken);

                long lineId;
                await using (var insertLine = new FbCommand("""
                    INSERT INTO STOCK_DOCUMENT_LINES
                        (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                         STANDARD_UNIT_CODE, NOTES)
                    VALUES
                        (@DOC, @LN, @MID, @QTY, @COST, @AMT, @UNIT, @NOTES)
                    RETURNING ID
                    """, connection, fbTx))
                {
                    insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNo;
                    insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = Math.Abs(qty);
                    insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                    insertLine.Parameters.Add("@AMT", FbDbType.Decimal).Value = Math.Abs(qty) * unitCost;
                    insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = material.UnitCode;
                    insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        (object?)line.Notes ?? DBNull.Value;
                    lineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                }

                await StockPostingSupport.InsertMovementAsync(
                    connection, fbTx, documentId, lineId, line.MaterialId,
                    draft.LocationId, qty > 0 ? "IN" : "OUT", qty, unitCost,
                    session.UserId, cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "STOCK_ADJUSTMENT_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(),
                $"{{\"reason\":{(draft.Reason is null ? "null" : $"\"{draft.Reason.Replace("\"", "'")}\"")},\"increase\":{draft.IsIncrease.ToString().ToLowerInvariant()}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return documentId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
