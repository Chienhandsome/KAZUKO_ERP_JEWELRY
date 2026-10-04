using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdFinishedProductTransferService : IFinishedProductTransferService
{
    public async Task<long> CreateAndPostAsync(
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
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = draft.SourceLocationId;
                insertDoc.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = draft.DestinationLocationId;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
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
                    update.Parameters.Add("@DESTINATION", FbDbType.BigInt).Value = draft.DestinationLocationId;
                    update.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
                    update.Parameters.Add("@SOURCE", FbDbType.BigInt).Value = draft.SourceLocationId;
                    update.Parameters.Add("@VERSION", FbDbType.BigInt).Value = product.RowVersion;
                    if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException($"Thành phẩm '{product.Code}' đã được người khác thay đổi. Vui lòng tải lại.");
                }

                await InsertDocumentLineAsync(
                    connection, fbTx, documentId, lineNo, productId,
                    draft.SourceLocationId, draft.DestinationLocationId,
                    "IN_STOCK", "DISPLAYED", product.UnitCost, cancellationToken);

                await InsertMovementAsync(
                    connection, fbTx, companyId, session.BranchId, productId,
                    "OUT", "TRANSFER", documentId, -1, product.UnitCost, draft.SourceLocationId,
                    draft.SourceLocationId, draft.DestinationLocationId,
                    "IN_STOCK", "DISPLAYED", session.UserId, cancellationToken);
                await InsertMovementAsync(
                    connection, fbTx, companyId, session.BranchId, productId,
                    "IN", "TRANSFER", documentId, 1, product.UnitCost, draft.DestinationLocationId,
                    draft.SourceLocationId, draft.DestinationLocationId,
                    "IN_STOCK", "DISPLAYED", session.UserId, cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "FINISHED_PRODUCT_TRANSFER_POSTED",
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
}
