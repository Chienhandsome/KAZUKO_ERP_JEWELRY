using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdStockTransferService : IStockTransferService
{
    public async Task<long> CreateAndPostAsync(
        StockTransferDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        if (draft.SourceLocationId <= 0 || draft.DestinationLocationId <= 0)
            throw new InvalidOperationException("Phải chọn kho nguồn và kho đích.");
        if (draft.SourceLocationId == draft.DestinationLocationId)
            throw new InvalidOperationException("Kho nguồn và kho đích phải khác nhau.");
        if (draft.Lines is null || draft.Lines.Count == 0)
            throw new InvalidOperationException("Phiếu chuyển kho phải có ít nhất một dòng.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "STOCK_TRANSFER", session.BranchId, "CK", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, DESTINATION_LOCATION_ID, PURPOSE_CODE, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'TRANSFER', @DATE,
                     @SRC, @DST, 'GENERAL', @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@SRC", FbDbType.BigInt).Value = draft.SourceLocationId;
                insertDoc.Parameters.Add("@DST", FbDbType.BigInt).Value = draft.DestinationLocationId;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    (object?)draft.Notes ?? DBNull.Value;
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var lineNo = 0;
            foreach (var line in draft.Lines)
            {
                lineNo++;
                if (line.MaterialId <= 0 || line.Quantity <= 0)
                    throw new InvalidOperationException($"Dòng {lineNo}: NVL/số lượng không hợp lệ.");

                var material = await StockPostingSupport.GetMaterialAsync(
                    connection, fbTx, companyId, line.MaterialId, cancellationToken);

                decimal unitCost = 0;
                await using (var srcCost = new FbCommand("""
                    SELECT COALESCE(AVERAGE_COST, 0)
                    FROM STOCK_BALANCES
                    WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
                    WITH LOCK
                    """, connection, fbTx))
                {
                    srcCost.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    srcCost.Parameters.Add("@LID", FbDbType.BigInt).Value = draft.SourceLocationId;
                    var costObj = await srcCost.ExecuteScalarAsync(cancellationToken);
                    unitCost = costObj is null or DBNull ? 0 : Convert.ToDecimal(costObj);
                }

                await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                    connection, fbTx, line.MaterialId, draft.SourceLocationId,
                    -line.Quantity, unitCost, cancellationToken);

                await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                    connection, fbTx, line.MaterialId, draft.DestinationLocationId,
                    line.Quantity, unitCost, cancellationToken);

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
                    insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                    insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                    insertLine.Parameters.Add("@AMT", FbDbType.Decimal).Value = line.Quantity * unitCost;
                    insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = material.UnitCode;
                    insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        (object?)line.Notes ?? DBNull.Value;
                    lineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                }

                await StockPostingSupport.InsertMovementAsync(
                    connection, fbTx, documentId, lineId, line.MaterialId,
                    draft.SourceLocationId, "OUT", -line.Quantity, unitCost,
                    session.UserId, cancellationToken);
                await StockPostingSupport.InsertMovementAsync(
                    connection, fbTx, documentId, lineId, line.MaterialId,
                    draft.DestinationLocationId, "IN", line.Quantity, unitCost,
                    session.UserId, cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "STOCK_TRANSFER_POSTED",
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
}
