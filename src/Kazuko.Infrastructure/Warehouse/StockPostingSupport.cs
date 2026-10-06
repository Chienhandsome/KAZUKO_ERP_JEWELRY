using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;

namespace Kazuko.Infrastructure.Warehouse;

internal static class StockPostingSupport
{
    public static void EnsureBranch(AuthenticatedSession session)
    {
        if (session.BranchId <= 0)
            throw new InvalidOperationException("Phiên làm việc thiếu chi nhánh.");
    }

    public static async Task<long> GetCompanyIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            "SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'",
            connection,
            transaction);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
            throw new InvalidOperationException("Không tìm thấy công ty KAZUKO.");
        return Convert.ToInt64(result);
    }

    public static async Task<string> NextDocumentCodeAsync(
        FbConnection connection,
        FbTransaction transaction,
        string documentType,
        long branchId,
        string prefix,
        CancellationToken cancellationToken)
    {
        var year = DateTime.Now.Year;
        await using (var ensure = new FbCommand("""
            INSERT INTO DOCUMENT_SEQUENCES (DOCUMENT_TYPE, BRANCH_ID, YEAR_NO, LAST_NUMBER, UPDATED_AT)
            SELECT @TYPE, @BRANCH, @YEAR, 0, CURRENT_TIMESTAMP
            FROM RDB$DATABASE
            WHERE NOT EXISTS (
                SELECT 1 FROM DOCUMENT_SEQUENCES
                WHERE DOCUMENT_TYPE = @TYPE AND BRANCH_ID = @BRANCH AND YEAR_NO = @YEAR)
            """, connection, transaction))
        {
            ensure.Parameters.Add("@TYPE", FbDbType.VarChar, 40).Value = documentType;
            ensure.Parameters.Add("@BRANCH", FbDbType.BigInt).Value = branchId;
            ensure.Parameters.Add("@YEAR", FbDbType.Integer).Value = year;
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = new FbCommand("""
            UPDATE DOCUMENT_SEQUENCES
               SET LAST_NUMBER = LAST_NUMBER + 1,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE DOCUMENT_TYPE = @TYPE
               AND BRANCH_ID = @BRANCH
               AND YEAR_NO = @YEAR
            RETURNING LAST_NUMBER
            """, connection, transaction);
        update.Parameters.Add("@TYPE", FbDbType.VarChar, 40).Value = documentType;
        update.Parameters.Add("@BRANCH", FbDbType.BigInt).Value = branchId;
        update.Parameters.Add("@YEAR", FbDbType.Integer).Value = year;
        var number = Convert.ToInt32(await update.ExecuteScalarAsync(cancellationToken));
        return $"{prefix}-{year}-{number:D6}";
    }

    public static async Task ApplyMaterialBalanceChangeAsync(
        FbConnection connection,
        FbTransaction transaction,
        long materialId,
        long locationId,
        decimal quantityChange,
        decimal unitCost,
        CancellationToken cancellationToken)
    {
        await using var select = new FbCommand("""
            SELECT QUANTITY, AVERAGE_COST
            FROM STOCK_BALANCES
            WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
            WITH LOCK
            """, connection, transaction);
        select.Parameters.Add("@MID", FbDbType.BigInt).Value = materialId;
        select.Parameters.Add("@LID", FbDbType.BigInt).Value = locationId;

        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            var qty = reader.GetDecimal(0);
            var avg = reader.GetDecimal(1);
            await reader.DisposeAsync();

            var newQty = qty + quantityChange;
            if (newQty < 0)
                throw new InvalidOperationException("Không đủ tồn kho để xuất.");

            decimal newAvg = avg;
            if (quantityChange > 0 && newQty > 0)
                newAvg = ((qty * avg) + (quantityChange * unitCost)) / newQty;

            await using var update = new FbCommand("""
                UPDATE STOCK_BALANCES
                   SET QUANTITY = @QTY,
                       AVERAGE_COST = @AVG,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
                """, connection, transaction);
            update.Parameters.Add("@QTY", FbDbType.Decimal).Value = newQty;
            update.Parameters.Add("@AVG", FbDbType.Decimal).Value = newAvg;
            update.Parameters.Add("@MID", FbDbType.BigInt).Value = materialId;
            update.Parameters.Add("@LID", FbDbType.BigInt).Value = locationId;
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        await reader.DisposeAsync();
        if (quantityChange < 0)
            throw new InvalidOperationException("Không đủ tồn kho để xuất.");

        await using var insert = new FbCommand("""
            INSERT INTO STOCK_BALANCES (MATERIAL_ID, LOCATION_ID, QUANTITY, AVERAGE_COST, UPDATED_AT)
            VALUES (@MID, @LID, @QTY, @AVG, CURRENT_TIMESTAMP)
            """, connection, transaction);
        insert.Parameters.Add("@MID", FbDbType.BigInt).Value = materialId;
        insert.Parameters.Add("@LID", FbDbType.BigInt).Value = locationId;
        insert.Parameters.Add("@QTY", FbDbType.Decimal).Value = quantityChange;
        insert.Parameters.Add("@AVG", FbDbType.Decimal).Value = unitCost;
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task InsertMovementAsync(
        FbConnection connection,
        FbTransaction transaction,
        long documentId,
        long documentLineId,
        long materialId,
        long locationId,
        string movementType,
        decimal quantityChange,
        decimal unitCost,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            INSERT INTO STOCK_MOVEMENTS
                (DOCUMENT_ID, DOCUMENT_LINE_ID, MATERIAL_ID, LOCATION_ID, MOVEMENT_TYPE,
                 OCCURRED_AT, QUANTITY_CHANGE, UNIT_COST, VALUE_CHANGE, CREATED_BY)
            VALUES
                (@DOC, @LINE, @MID, @LID, @TYPE,
                 CURRENT_TIMESTAMP, @QTY, @COST, @VALUE, @BY)
            """, connection, transaction);
        command.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
        command.Parameters.Add("@LINE", FbDbType.BigInt).Value = documentLineId;
        command.Parameters.Add("@MID", FbDbType.BigInt).Value = materialId;
        command.Parameters.Add("@LID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@TYPE", FbDbType.VarChar, 20).Value = movementType;
        command.Parameters.Add("@QTY", FbDbType.Decimal).Value = quantityChange;
        command.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
        command.Parameters.Add("@VALUE", FbDbType.Decimal).Value = quantityChange * unitCost;
        command.Parameters.Add("@BY", FbDbType.BigInt).Value = createdBy is > 0 ? createdBy.Value : DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<(string Code, string MaterialType, string UnitCode, string PurityCode, long? GoldTypeId)> GetMaterialAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long materialId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE, MATERIAL_TYPE, UNIT_CODE, COALESCE(PURITY_CODE, ''), GOLD_TYPE_ID
            FROM MATERIALS
            WHERE ID = @ID AND COMPANY_ID = @CID AND IS_ACTIVE = TRUE
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = materialId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Không tìm thấy NVL #{materialId}.");
        long? goldTypeId = reader.IsDBNull(4) ? null : reader.GetInt64(4);
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), goldTypeId);
    }
}
