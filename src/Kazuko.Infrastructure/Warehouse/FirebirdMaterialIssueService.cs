using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Inventory;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdMaterialIssueService : IMaterialIssueService
{
    public async Task<long> CreateAndPostAsync(
        MaterialIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = MaterialIssueValidator.Validate(draft);
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
            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, StockDocumentTypes.Issue, session.BranchId, "XK", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, PURPOSE_CODE, RECEIVED_BY_NAME, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'ISSUE', @DATE,
                     @SOURCE, 'GENERAL', @RECEIVER, @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = draft.SourceLocationId;
                insertDoc.Parameters.Add("@RECEIVER", FbDbType.VarChar, 150).Value =
                    string.IsNullOrWhiteSpace(draft.ReceivedByName) ? DBNull.Value : draft.ReceivedByName.Trim();
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var lineNo = 0;
            foreach (var line in draft.Lines)
            {
                lineNo++;
                var material = await StockPostingSupport.GetMaterialAsync(
                    connection, fbTx, companyId, line.MaterialId, cancellationToken);
                if (!MaterialTypes.IsRawMaterial(material.MaterialType))
                    throw new InvalidOperationException($"Dòng {lineNo}: '{material.Code}' không phải NVL.");

                decimal unitCost;
                await using (var costCmd = new FbCommand("""
                    SELECT COALESCE(AVERAGE_COST, 0)
                    FROM STOCK_BALANCES
                    WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
                    WITH LOCK
                    """, connection, fbTx))
                {
                    costCmd.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    costCmd.Parameters.Add("@LID", FbDbType.BigInt).Value = draft.SourceLocationId;
                    var value = await costCmd.ExecuteScalarAsync(cancellationToken);
                    unitCost = value is null or DBNull ? 0 : Convert.ToDecimal(value);
                }

                WarehouseGoldPriceSnapshot? warehouseGoldPrice = null;
                if (string.Equals(material.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
                {
                    warehouseGoldPrice = await FirebirdWarehouseGoldPricingSupport.ResolveAsync(
                        connection, fbTx, companyId, material.GoldTypeId,
                        string.IsNullOrWhiteSpace(material.PurityCode) ? material.Code : material.PurityCode,
                        cancellationToken);
                    unitCost = warehouseGoldPrice.IssuePricePerChi;
                }

                await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                    connection, fbTx, line.MaterialId, draft.SourceLocationId,
                    -line.Quantity, unitCost, cancellationToken);

                long lineId;
                await using (var insertLine = new FbCommand("""
                    INSERT INTO STOCK_DOCUMENT_LINES
                        (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                         STANDARD_UNIT_CODE, WAREHOUSE_GOLD_PRICE_ID,
                         WAREHOUSE_GOLD_PRICE_PER_CHI, NOTES)
                    VALUES
                        (@DOC, @LN, @MID, @QTY, @COST, @AMOUNT, @UNIT, @WGP, @WGPRICE, @NOTES)
                    RETURNING ID
                    """, connection, fbTx))
                {
                    insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNo;
                    insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                    insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                    insertLine.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = line.Quantity * unitCost;
                    insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = material.UnitCode;
                    insertLine.Parameters.Add("@WGP", FbDbType.BigInt).Value =
                        warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.Id;
                    insertLine.Parameters.Add("@WGPRICE", FbDbType.Decimal).Value =
                        warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.IssuePricePerChi;
                    insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        string.IsNullOrWhiteSpace(line.Notes) ? DBNull.Value : line.Notes.Trim();
                    lineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                }

                await StockPostingSupport.InsertMovementAsync(
                    connection, fbTx, documentId, lineId, line.MaterialId,
                    draft.SourceLocationId, "OUT", -line.Quantity, unitCost,
                    session.UserId, cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "MATERIAL_ISSUE_POSTED",
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
              AND INVENTORY_SCOPE = 'RAW_MATERIAL'
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho xuất NVL không hợp lệ hoặc không thuộc chi nhánh hiện tại.");
    }
}
