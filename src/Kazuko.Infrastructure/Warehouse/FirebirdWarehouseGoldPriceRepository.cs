using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdWarehouseGoldPriceRepository : IWarehouseGoldPriceRepository
{
    public async Task<IReadOnlyList<WarehouseGoldPriceItem>> GetAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = new List<WarehouseGoldPriceItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT W.ID, W.GOLD_TYPE_CODE, W.GOLD_TYPE_NAME, W.PURITY_RATE,
                   W.BUY_PRICE_PER_CHI, W.SELL_PRICE_PER_CHI,
                   W.ROW_VERSION, COALESCE(U.DISPLAY_NAME, ''), W.UPDATED_AT
            FROM WAREHOUSE_GOLD_PRICES W
            LEFT JOIN USERS U ON U.ID = W.UPDATED_BY
            WHERE W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND W.IS_ACTIVE = TRUE
            ORDER BY W.PURITY_RATE DESC, W.ID
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WarehouseGoldPriceItem(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetInt64(6), reader.GetString(7),
                reader.GetDateTime(8)));
        }

        return result;
    }

    public async Task SaveAsync(
        IReadOnlyList<WarehouseGoldPriceUpdate> updates,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(session);
        if (updates.Count == 0)
            return;
        if (updates.Any(x => x.BuyPricePerChi <= 0 || x.SellPricePerChi <= 0))
            throw new InvalidOperationException("Giá mua và giá bán trong kho phải lớn hơn 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var update in updates)
            {
                string code;
                string name;
                decimal oldBuyPrice;
                decimal oldSellPrice;
                await using (var current = new FbCommand("""
                    SELECT W.GOLD_TYPE_CODE, W.GOLD_TYPE_NAME,
                           W.BUY_PRICE_PER_CHI, W.SELL_PRICE_PER_CHI
                    FROM WAREHOUSE_GOLD_PRICES W
                    WHERE W.ID = @ID
                      AND W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND W.IS_ACTIVE = TRUE
                    WITH LOCK
                    """, connection, transaction))
                {
                    current.Parameters.Add("@ID", FbDbType.BigInt).Value = update.Id;
                    await using var reader = await current.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                        throw new InvalidOperationException("Không tìm thấy dòng giá vàng kho.");
                    code = reader.GetString(0);
                    name = reader.GetString(1);
                    oldBuyPrice = reader.GetDecimal(2);
                    oldSellPrice = reader.GetDecimal(3);
                }

                if (oldBuyPrice == update.BuyPricePerChi && oldSellPrice == update.SellPricePerChi)
                    continue;

                await using (var command = new FbCommand("""
                    UPDATE WAREHOUSE_GOLD_PRICES
                       SET BUY_PRICE_PER_CHI = @BUY,
                           SELL_PRICE_PER_CHI = @SELL,
                           ISSUE_PRICE_PER_CHI = @BUY,
                           ROW_VERSION = ROW_VERSION + 1,
                           UPDATED_BY = @BY,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID AND ROW_VERSION = @VERSION
                    """, connection, transaction))
                {
                    command.Parameters.Add("@BUY", FbDbType.Decimal).Value = update.BuyPricePerChi;
                    command.Parameters.Add("@SELL", FbDbType.Decimal).Value = update.SellPricePerChi;
                    command.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId > 0 ? session.UserId : DBNull.Value;
                    command.Parameters.Add("@ID", FbDbType.BigInt).Value = update.Id;
                    command.Parameters.Add("@VERSION", FbDbType.BigInt).Value = update.RowVersion;
                    if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException($"Giá kho '{code}' đã được thay đổi ở phiên khác. Vui lòng tải lại.");
                }

                await using var history = new FbCommand("""
                    INSERT INTO WAREHOUSE_GOLD_PRICE_HISTORY
                        (WAREHOUSE_GOLD_PRICE_ID, GOLD_CODE, GOLD_NAME,
                         OLD_PRICE_PER_CHI, NEW_PRICE_PER_CHI,
                         OLD_BUY_PRICE_PER_CHI, NEW_BUY_PRICE_PER_CHI,
                         OLD_SELL_PRICE_PER_CHI, NEW_SELL_PRICE_PER_CHI,
                         CHANGED_BY, CHANGED_AT, NOTES)
                    VALUES
                        (@ID, @CODE, @NAME, @OLD_BUY, @NEW_BUY,
                         @OLD_BUY, @NEW_BUY, @OLD_SELL, @NEW_SELL,
                         @BY, CURRENT_TIMESTAMP, @NOTES)
                    """, connection, transaction);
                history.Parameters.Add("@ID", FbDbType.BigInt).Value = update.Id;
                history.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                history.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                history.Parameters.Add("@OLD_BUY", FbDbType.Decimal).Value = oldBuyPrice;
                history.Parameters.Add("@NEW_BUY", FbDbType.Decimal).Value = update.BuyPricePerChi;
                history.Parameters.Add("@OLD_SELL", FbDbType.Decimal).Value = oldSellPrice;
                history.Parameters.Add("@NEW_SELL", FbDbType.Decimal).Value = update.SellPricePerChi;
                history.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId > 0 ? session.UserId : DBNull.Value;
                history.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim();
                await history.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
