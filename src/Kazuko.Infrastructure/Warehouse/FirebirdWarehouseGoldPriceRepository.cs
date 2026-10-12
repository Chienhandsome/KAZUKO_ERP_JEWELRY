using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

// Dapper migration (phase 2): SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record (vd. GOLD_TYPE_CODE AS GoldCode).
public sealed class FirebirdWarehouseGoldPriceRepository : IWarehouseGoldPriceRepository
{
    public async Task<IReadOnlyList<WarehouseGoldPriceItem>> GetAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<WarehouseGoldPriceItem>(
            new CommandDefinition("""
                SELECT W.ID AS Id, W.GOLD_TYPE_CODE AS GoldCode, W.GOLD_TYPE_NAME AS GoldName,
                       W.PURITY_RATE AS PurityRate,
                       W.BUY_PRICE_PER_CHI AS BuyPricePerChi, W.SELL_PRICE_PER_CHI AS SellPricePerChi,
                       W.ROW_VERSION AS RowVersion, COALESCE(U.DISPLAY_NAME, '') AS UpdatedByName,
                       W.UPDATED_AT AS UpdatedAt
                FROM WAREHOUSE_GOLD_PRICES W
                LEFT JOIN USERS U ON U.ID = W.UPDATED_BY
                WHERE W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND W.IS_ACTIVE = TRUE
                ORDER BY W.PURITY_RATE DESC, W.ID
                """, cancellationToken: cancellationToken));
        return rows.AsList();
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
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var by = session.UserId > 0 ? (object?)session.UserId : null;

            foreach (var update in updates)
            {
                var current = await connection.QuerySingleOrDefaultAsync<WarehouseGoldPriceCurrent>(
                    new CommandDefinition("""
                        SELECT W.GOLD_TYPE_CODE AS Code, W.GOLD_TYPE_NAME AS Name,
                               W.BUY_PRICE_PER_CHI AS OldBuyPrice, W.SELL_PRICE_PER_CHI AS OldSellPrice
                        FROM WAREHOUSE_GOLD_PRICES W
                        WHERE W.ID = @ID
                          AND W.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                          AND W.IS_ACTIVE = TRUE
                        WITH LOCK
                        """,
                        new { ID = update.Id },
                        transaction,
                        cancellationToken: cancellationToken));
                if (current is null)
                    throw new InvalidOperationException("Không tìm thấy dòng giá vàng kho.");

                if (current.OldBuyPrice == update.BuyPricePerChi && current.OldSellPrice == update.SellPricePerChi)
                    continue;

                var affected = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE WAREHOUSE_GOLD_PRICES
                           SET BUY_PRICE_PER_CHI = @BUY,
                               SELL_PRICE_PER_CHI = @SELL,
                               ISSUE_PRICE_PER_CHI = @BUY,
                               ROW_VERSION = ROW_VERSION + 1,
                               UPDATED_BY = @BY,
                               UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE ID = @ID AND ROW_VERSION = @VERSION
                        """,
                        new
                        {
                            BUY = update.BuyPricePerChi,
                            SELL = update.SellPricePerChi,
                            BY = by,
                            ID = update.Id,
                            VERSION = update.RowVersion
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                if (affected != 1)
                    throw new InvalidOperationException($"Giá kho '{current.Code}' đã được thay đổi ở phiên khác. Vui lòng tải lại.");

                await connection.ExecuteAsync(
                    new CommandDefinition("""
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
                        """,
                        new
                        {
                            ID = update.Id,
                            CODE = current.Code,
                            NAME = current.Name,
                            OLD_BUY = current.OldBuyPrice,
                            NEW_BUY = update.BuyPricePerChi,
                            OLD_SELL = current.OldSellPrice,
                            NEW_SELL = update.SellPricePerChi,
                            BY = by,
                            NOTES = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { /* swallow */ }
            throw;
        }
    }

    private sealed record WarehouseGoldPriceCurrent(
        string Code,
        string Name,
        decimal OldBuyPrice,
        decimal OldSellPrice);
}
