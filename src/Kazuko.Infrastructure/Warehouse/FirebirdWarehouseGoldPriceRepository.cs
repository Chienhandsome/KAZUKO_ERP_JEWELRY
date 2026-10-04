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
            SELECT W.ID, COALESCE(W.GOLD_PRICE_ID, 0), W.GOLD_TYPE_CODE, W.GOLD_TYPE_NAME, W.PURITY_RATE,
                   W.ISSUE_PRICE_PER_CHI, COALESCE(G.BUY_PRICE_PER_CHI, 0), COALESCE(G.SELL_PRICE_PER_CHI, 0),
                   W.ROW_VERSION, COALESCE(U.DISPLAY_NAME, ''), W.UPDATED_AT
            FROM WAREHOUSE_GOLD_PRICES W
            LEFT JOIN GOLD_PRICES G ON G.ID = W.GOLD_PRICE_ID
            LEFT JOIN USERS U ON U.ID = W.UPDATED_BY
            WHERE W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND W.IS_ACTIVE = TRUE
            ORDER BY W.PURITY_RATE DESC, W.ID
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new WarehouseGoldPriceItem(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                reader.GetInt64(8), reader.GetString(9), reader.GetDateTime(10)));
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
        if (updates.Any(x => x.IssuePricePerChi <= 0))
            throw new InvalidOperationException("Giá xuất kho phải lớn hơn 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var update in updates)
            {
                string code;
                string name;
                decimal oldPrice;
                await using (var current = new FbCommand("""
                    SELECT W.GOLD_TYPE_CODE, W.GOLD_TYPE_NAME, W.ISSUE_PRICE_PER_CHI
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
                    oldPrice = reader.GetDecimal(2);
                }

                if (oldPrice == update.IssuePricePerChi)
                    continue;

                await using (var command = new FbCommand("""
                    UPDATE WAREHOUSE_GOLD_PRICES
                       SET ISSUE_PRICE_PER_CHI = @PRICE,
                           ROW_VERSION = ROW_VERSION + 1,
                           UPDATED_BY = @BY,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID AND ROW_VERSION = @VERSION
                    """, connection, transaction))
                {
                    command.Parameters.Add("@PRICE", FbDbType.Decimal).Value = update.IssuePricePerChi;
                    command.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId > 0 ? session.UserId : DBNull.Value;
                    command.Parameters.Add("@ID", FbDbType.BigInt).Value = update.Id;
                    command.Parameters.Add("@VERSION", FbDbType.BigInt).Value = update.RowVersion;
                    if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                        throw new InvalidOperationException($"Giá kho '{code}' đã được thay đổi ở phiên khác. Vui lòng tải lại.");
                }

                await using var history = new FbCommand("""
                    INSERT INTO WAREHOUSE_GOLD_PRICE_HISTORY
                        (WAREHOUSE_GOLD_PRICE_ID, GOLD_CODE, GOLD_NAME,
                         OLD_PRICE_PER_CHI, NEW_PRICE_PER_CHI, CHANGED_BY, CHANGED_AT, NOTES)
                    VALUES
                        (@ID, @CODE, @NAME, @OLD, @NEW, @BY, CURRENT_TIMESTAMP, @NOTES)
                    """, connection, transaction);
                history.Parameters.Add("@ID", FbDbType.BigInt).Value = update.Id;
                history.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                history.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
                history.Parameters.Add("@OLD", FbDbType.Decimal).Value = oldPrice;
                history.Parameters.Add("@NEW", FbDbType.Decimal).Value = update.IssuePricePerChi;
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
