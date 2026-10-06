using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.PriceBoard;

public sealed class FirebirdGoldTypeCatalogService : IGoldTypeCatalogService
{
    public async Task<IReadOnlyList<GoldTypeOption>> ListAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GoldTypeOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT T.ID, T.CODE, T.NAME, T.PURITY_RATE, T.IS_ACTIVE
            FROM GOLD_TYPES T
            WHERE T.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@ACTIVE_ONLY = 0 OR T.IS_ACTIVE = TRUE)
            ORDER BY T.DISPLAY_ORDER, T.CODE
            """, connection);
        command.Parameters.Add("@ACTIVE_ONLY", FbDbType.Integer).Value = activeOnly ? 1 : 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new GoldTypeOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetBoolean(4)));
        }

        return result;
    }

    public async Task<GoldTypeDetail?> GetByIdAsync(
        long id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT T.ID, T.CODE, T.NAME, T.PURITY_RATE, T.DISPLAY_ORDER, T.IS_ACTIVE,
                   G.ID, G.BUY_PRICE_PER_CHI, G.SELL_PRICE_PER_CHI,
                   W.ID, W.ISSUE_PRICE_PER_CHI,
                   M.ID
            FROM GOLD_TYPES T
            JOIN GOLD_PRICES G ON G.GOLD_TYPE_ID = T.ID
            JOIN WAREHOUSE_GOLD_PRICES W ON W.GOLD_TYPE_ID = T.ID
            JOIN MATERIALS M ON M.GOLD_TYPE_ID = T.ID AND M.MATERIAL_TYPE = 'GOLD'
            WHERE T.ID = @ID
              AND T.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = id;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new GoldTypeDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetDecimal(3),
            reader.GetInt32(4),
            reader.GetBoolean(5),
            reader.GetInt64(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetInt64(9),
            reader.GetDecimal(10),
            reader.GetInt64(11));
    }

    public async Task<long> CreateAsync(
        GoldTypeUpsertRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var errors = GoldTypeCatalogValidator.Validate(request);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var purity = GoldTypePurity.Resolve(request);
        var buyGram = Math.Round(request.BuyPricePerChi / GoldUnitConverter.GramsPerChi, 2);
        var sellGram = Math.Round(request.SellPricePerChi / GoldUnitConverter.GramsPerChi, 2);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            await EnsureCodeUniqueAsync(connection, fbTx, companyId, code, excludeId: null, cancellationToken);

            var displayOrder = request.DisplayOrder
                ?? await NextDisplayOrderAsync(connection, fbTx, companyId, cancellationToken);

            long typeId;
            await using (var insertType = new FbCommand("""
                INSERT INTO GOLD_TYPES
                    (COMPANY_ID, CODE, NAME, PURITY_RATE, DISPLAY_ORDER, IS_ACTIVE,
                     CREATED_BY, CREATED_AT, UPDATED_BY, UPDATED_AT)
                VALUES
                    (@CID, @CODE, @NAME, @PURITY, @ORD, TRUE,
                     @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertType.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertType.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertType.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                insertType.Parameters.Add("@PURITY", FbDbType.Decimal).Value = purity;
                insertType.Parameters.Add("@ORD", FbDbType.Integer).Value = displayOrder;
                insertType.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                typeId = Convert.ToInt64(await insertType.ExecuteScalarAsync(cancellationToken));
            }

            long retailId;
            await using (var insertRetail = new FbCommand("""
                INSERT INTO GOLD_PRICES
                    (COMPANY_ID, CODE, NAME, PURITY_RATE, BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
                     BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM, CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE,
                     GOLD_TYPE_ID, UPDATED_BY, UPDATED_AT)
                VALUES
                    (@CID, @CODE, @NAME, @PURITY, @BUY, @SELL, @BUY_G, @SELL_G, 'VND', @ORD, TRUE,
                     @TID, @BY_NAME, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertRetail.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertRetail.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertRetail.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                insertRetail.Parameters.Add("@PURITY", FbDbType.Decimal).Value = purity;
                insertRetail.Parameters.Add("@BUY", FbDbType.Decimal).Value = request.BuyPricePerChi;
                insertRetail.Parameters.Add("@SELL", FbDbType.Decimal).Value = request.SellPricePerChi;
                insertRetail.Parameters.Add("@BUY_G", FbDbType.Decimal).Value = buyGram;
                insertRetail.Parameters.Add("@SELL_G", FbDbType.Decimal).Value = sellGram;
                insertRetail.Parameters.Add("@ORD", FbDbType.Integer).Value = displayOrder;
                insertRetail.Parameters.Add("@TID", FbDbType.BigInt).Value = typeId;
                insertRetail.Parameters.Add("@BY_NAME", FbDbType.VarChar, 100).Value =
                    string.IsNullOrWhiteSpace(session.Username) ? "system" : session.Username;
                retailId = Convert.ToInt64(await insertRetail.ExecuteScalarAsync(cancellationToken));
            }

            await using (var insertWh = new FbCommand("""
                INSERT INTO WAREHOUSE_GOLD_PRICES
                    (COMPANY_ID, GOLD_PRICE_ID, GOLD_TYPE_CODE, GOLD_TYPE_NAME, PURITY_RATE,
                     ISSUE_PRICE_PER_CHI, IS_ACTIVE, ROW_VERSION, UPDATED_BY, UPDATED_AT, GOLD_TYPE_ID)
                VALUES
                    (@CID, @GID, @CODE, @NAME, @PURITY,
                     @ISSUE, TRUE, 0, @BY, CURRENT_TIMESTAMP, @TID)
                """, connection, fbTx))
            {
                insertWh.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertWh.Parameters.Add("@GID", FbDbType.BigInt).Value = retailId;
                insertWh.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertWh.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                insertWh.Parameters.Add("@PURITY", FbDbType.Decimal).Value = purity;
                insertWh.Parameters.Add("@ISSUE", FbDbType.Decimal).Value = request.IssuePricePerChi;
                insertWh.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                insertWh.Parameters.Add("@TID", FbDbType.BigInt).Value = typeId;
                await insertWh.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insertMat = new FbCommand("""
                INSERT INTO MATERIALS
                    (COMPANY_ID, CODE, NAME, MATERIAL_TYPE, PURITY_CODE, UNIT_CODE,
                     REFERENCE_COST, IS_ACTIVE, TRACKING_MODE, MIN_STOCK,
                     OLD_GOLD_PRICE_ID, GOLD_TYPE_ID)
                VALUES
                    (@CID, @CODE, @NAME, 'GOLD', @PURITY, 'CHI',
                     @COST, TRUE, 'WEIGHT', 0, @RETAIL, @TID)
                """, connection, fbTx))
            {
                insertMat.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertMat.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = "GOLD_" + code;
                insertMat.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                insertMat.Parameters.Add("@PURITY", FbDbType.VarChar, 40).Value = code;
                insertMat.Parameters.Add("@COST", FbDbType.Decimal).Value = request.BuyPricePerChi;
                insertMat.Parameters.Add("@RETAIL", FbDbType.BigInt).Value = retailId;
                insertMat.Parameters.Add("@TID", FbDbType.BigInt).Value = typeId;
                await insertMat.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return typeId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task UpdateAsync(
        long id,
        GoldTypeUpsertRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var errors = GoldTypeCatalogValidator.Validate(request);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var code = request.Code.Trim().ToUpperInvariant();
        var name = request.Name.Trim();
        var purity = GoldTypePurity.Resolve(request);
        var buyGram = Math.Round(request.BuyPricePerChi / GoldUnitConverter.GramsPerChi, 2);
        var sellGram = Math.Round(request.SellPricePerChi / GoldUnitConverter.GramsPerChi, 2);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            await EnsureCodeUniqueAsync(connection, fbTx, companyId, code, excludeId: id, cancellationToken);

            await using (var updateType = new FbCommand("""
                UPDATE GOLD_TYPES
                   SET CODE = @CODE,
                       NAME = @NAME,
                       PURITY_RATE = @PURITY,
                       DISPLAY_ORDER = COALESCE(@ORD, DISPLAY_ORDER),
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID AND COMPANY_ID = @CID AND IS_ACTIVE = TRUE
                """, connection, fbTx))
            {
                updateType.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                updateType.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                updateType.Parameters.Add("@PURITY", FbDbType.Decimal).Value = purity;
                updateType.Parameters.Add("@ORD", FbDbType.Integer).Value =
                    request.DisplayOrder is int ord ? ord : DBNull.Value;
                updateType.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateType.Parameters.Add("@ID", FbDbType.BigInt).Value = id;
                updateType.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                if (await updateType.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không tìm thấy loại vàng đang hoạt động để cập nhật.");
            }

            await using (var updateRetail = new FbCommand("""
                UPDATE GOLD_PRICES
                   SET CODE = @CODE,
                       NAME = @NAME,
                       PURITY_RATE = @PURITY,
                       BUY_PRICE_PER_CHI = @BUY,
                       SELL_PRICE_PER_CHI = @SELL,
                       BUY_PRICE_PER_GRAM = @BUY_G,
                       SELL_PRICE_PER_GRAM = @SELL_G,
                       UPDATED_BY = @BY_NAME,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE GOLD_TYPE_ID = @TID AND COMPANY_ID = @CID
                """, connection, fbTx))
            {
                updateRetail.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                updateRetail.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                updateRetail.Parameters.Add("@PURITY", FbDbType.Decimal).Value = purity;
                updateRetail.Parameters.Add("@BUY", FbDbType.Decimal).Value = request.BuyPricePerChi;
                updateRetail.Parameters.Add("@SELL", FbDbType.Decimal).Value = request.SellPricePerChi;
                updateRetail.Parameters.Add("@BUY_G", FbDbType.Decimal).Value = buyGram;
                updateRetail.Parameters.Add("@SELL_G", FbDbType.Decimal).Value = sellGram;
                updateRetail.Parameters.Add("@BY_NAME", FbDbType.VarChar, 100).Value =
                    string.IsNullOrWhiteSpace(session.Username) ? "system" : session.Username;
                updateRetail.Parameters.Add("@TID", FbDbType.BigInt).Value = id;
                updateRetail.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                await updateRetail.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateWh = new FbCommand("""
                UPDATE WAREHOUSE_GOLD_PRICES
                   SET GOLD_TYPE_CODE = @CODE,
                       GOLD_TYPE_NAME = @NAME,
                       PURITY_RATE = @PURITY,
                       ISSUE_PRICE_PER_CHI = @ISSUE,
                       ROW_VERSION = ROW_VERSION + 1,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE GOLD_TYPE_ID = @TID AND COMPANY_ID = @CID
                """, connection, fbTx))
            {
                updateWh.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                updateWh.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                updateWh.Parameters.Add("@PURITY", FbDbType.Decimal).Value = purity;
                updateWh.Parameters.Add("@ISSUE", FbDbType.Decimal).Value = request.IssuePricePerChi;
                updateWh.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateWh.Parameters.Add("@TID", FbDbType.BigInt).Value = id;
                updateWh.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                await updateWh.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateMat = new FbCommand("""
                UPDATE MATERIALS
                   SET CODE = @MCODE,
                       NAME = @NAME,
                       PURITY_CODE = @CODE,
                       REFERENCE_COST = @COST,
                       OLD_GOLD_PRICE_ID = (
                           SELECT G.ID FROM GOLD_PRICES G WHERE G.GOLD_TYPE_ID = @TID)
                 WHERE GOLD_TYPE_ID = @TID AND COMPANY_ID = @CID AND MATERIAL_TYPE = 'GOLD'
                """, connection, fbTx))
            {
                updateMat.Parameters.Add("@MCODE", FbDbType.VarChar, 40).Value = "GOLD_" + code;
                updateMat.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                updateMat.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                updateMat.Parameters.Add("@COST", FbDbType.Decimal).Value = request.BuyPricePerChi;
                updateMat.Parameters.Add("@TID", FbDbType.BigInt).Value = id;
                updateMat.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                await updateMat.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeactivateAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);

            await using (var updateType = new FbCommand("""
                UPDATE GOLD_TYPES
                   SET IS_ACTIVE = FALSE, UPDATED_BY = @BY, UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID AND COMPANY_ID = @CID
                """, connection, fbTx))
            {
                updateType.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateType.Parameters.Add("@ID", FbDbType.BigInt).Value = id;
                updateType.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                if (await updateType.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không tìm thấy loại vàng để ngưng dùng.");
            }

            await using (var updateRetail = new FbCommand("""
                UPDATE GOLD_PRICES SET IS_ACTIVE = FALSE, UPDATED_AT = CURRENT_TIMESTAMP
                WHERE GOLD_TYPE_ID = @TID AND COMPANY_ID = @CID
                """, connection, fbTx))
            {
                updateRetail.Parameters.Add("@TID", FbDbType.BigInt).Value = id;
                updateRetail.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                await updateRetail.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateWh = new FbCommand("""
                UPDATE WAREHOUSE_GOLD_PRICES
                   SET IS_ACTIVE = FALSE, ROW_VERSION = ROW_VERSION + 1, UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE GOLD_TYPE_ID = @TID AND COMPANY_ID = @CID
                """, connection, fbTx))
            {
                updateWh.Parameters.Add("@TID", FbDbType.BigInt).Value = id;
                updateWh.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                await updateWh.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateMat = new FbCommand("""
                UPDATE MATERIALS SET IS_ACTIVE = FALSE
                WHERE GOLD_TYPE_ID = @TID AND COMPANY_ID = @CID AND MATERIAL_TYPE = 'GOLD'
                """, connection, fbTx))
            {
                updateMat.Parameters.Add("@TID", FbDbType.BigInt).Value = id;
                updateMat.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                await updateMat.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task EnsureCodeUniqueAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string code,
        long? excludeId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM GOLD_TYPES
            WHERE COMPANY_ID = @CID AND CODE = @CODE
              AND (@EXCLUDE = 0 OR ID <> @EXCLUDE)
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
        command.Parameters.Add("@EXCLUDE", FbDbType.BigInt).Value = excludeId ?? 0;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (count > 0)
            throw new InvalidOperationException($"Mã loại vàng '{code}' đã tồn tại.");
    }

    private static async Task<int> NextDisplayOrderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COALESCE(MAX(DISPLAY_ORDER), 0) + 1 FROM GOLD_TYPES WHERE COMPANY_ID = @CID
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
