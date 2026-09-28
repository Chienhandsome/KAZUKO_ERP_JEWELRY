using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.PriceBoard;

public sealed class FirebirdPriceBoardRepository : IPriceBoardRepository
{
    public async Task<IReadOnlyList<GoldPriceRecord>> GetGoldPricesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<GoldPriceRecord>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, COMPANY_ID, CODE, NAME, PURITY_RATE,
                   BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
                   BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM,
                   CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE,
                   COALESCE(UPDATED_BY, ''), UPDATED_AT
            FROM GOLD_PRICES
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND IS_ACTIVE = TRUE
            ORDER BY DISPLAY_ORDER, ID
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new GoldPriceRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetString(9),
                reader.GetInt32(10),
                reader.GetBoolean(11),
                reader.GetString(12),
                reader.GetDateTime(13)));
        }

        return result;
    }

    public async Task SaveGoldPricesAsync(
        IReadOnlyList<GoldPriceUpdateEntry> updates,
        string note,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (updates.Count == 0)
            return;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var currentPrices = new Dictionary<long, GoldPriceRecord>();
            await using (var fetchCommand = new FbCommand("""
                SELECT ID, COMPANY_ID, CODE, NAME, PURITY_RATE,
                       BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
                       BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM,
                       CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE,
                       COALESCE(UPDATED_BY, ''), UPDATED_AT
                FROM GOLD_PRICES
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """, connection, (FbTransaction)transaction))
            {
                await using var reader = await fetchCommand.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var record = new GoldPriceRecord(
                        reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3),
                        reader.GetDecimal(4), reader.GetDecimal(5), reader.GetDecimal(6), reader.GetDecimal(7),
                        reader.GetDecimal(8), reader.GetString(9), reader.GetInt32(10), reader.GetBoolean(11),
                        reader.GetString(12), reader.GetDateTime(13));
                    currentPrices[record.Id] = record;
                }
            }

            var userName = ActorName(session);
            var now = DateTime.Now;

            foreach (var update in updates)
            {
                if (!currentPrices.TryGetValue(update.Id, out var existing))
                    continue;

                var newBuyChi = GoldUnitConverter.ConvertToChi(update.BuyPriceInSelectedUnit, update.UnitCode);
                var newSellChi = GoldUnitConverter.ConvertToChi(update.SellPriceInSelectedUnit, update.UnitCode);
                var newBuyGram = Math.Round(newBuyChi / GoldUnitConverter.GramsPerChi, 2);
                var newSellGram = Math.Round(newSellChi / GoldUnitConverter.GramsPerChi, 2);
                var oldBuyInUnit = existing.DisplayBuyPrice(update.UnitCode);
                var oldSellInUnit = existing.DisplaySellPrice(update.UnitCode);
                var priceChanged = newBuyChi != existing.BuyPricePerChi || newSellChi != existing.SellPricePerChi;
                if (!priceChanged)
                    continue;

                await using (var updateCmd = new FbCommand("""
                    UPDATE GOLD_PRICES
                    SET BUY_PRICE_PER_CHI = @BUY_CHI,
                        SELL_PRICE_PER_CHI = @SELL_CHI,
                        BUY_PRICE_PER_GRAM = @BUY_GRAM,
                        SELL_PRICE_PER_GRAM = @SELL_GRAM,
                        UPDATED_BY = @UPDATED_BY,
                        UPDATED_AT = @UPDATED_AT
                    WHERE ID = @ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, connection, (FbTransaction)transaction))
                {
                    updateCmd.Parameters.AddWithValue("@BUY_CHI", newBuyChi);
                    updateCmd.Parameters.AddWithValue("@SELL_CHI", newSellChi);
                    updateCmd.Parameters.AddWithValue("@BUY_GRAM", newBuyGram);
                    updateCmd.Parameters.AddWithValue("@SELL_GRAM", newSellGram);
                    updateCmd.Parameters.AddWithValue("@UPDATED_BY", userName);
                    updateCmd.Parameters.AddWithValue("@UPDATED_AT", now);
                    updateCmd.Parameters.AddWithValue("@ID", update.Id);
                    await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var histCmd = new FbCommand("""
                    INSERT INTO GOLD_PRICE_HISTORY
                        (GOLD_PRICE_ID, PRICE_CODE, GOLD_NAME, UNIT_NAME,
                         OLD_BUY_PRICE, NEW_BUY_PRICE, OLD_SELL_PRICE, NEW_SELL_PRICE,
                         CHANGED_BY, CHANGED_AT, NOTES)
                    VALUES
                        (@PRICE_ID, @CODE, @NAME, @UNIT,
                         @OLD_BUY, @NEW_BUY, @OLD_SELL, @NEW_SELL,
                         @CHANGED_BY, @CHANGED_AT, @NOTES)
                    """, connection, (FbTransaction)transaction))
                {
                    histCmd.Parameters.AddWithValue("@PRICE_ID", update.Id);
                    histCmd.Parameters.AddWithValue("@CODE", existing.Code);
                    histCmd.Parameters.AddWithValue("@NAME", existing.Name);
                    histCmd.Parameters.AddWithValue("@UNIT", GoldUnitConverter.GetUnit(update.UnitCode).Name);
                    histCmd.Parameters.AddWithValue("@OLD_BUY", oldBuyInUnit);
                    histCmd.Parameters.AddWithValue("@NEW_BUY", update.BuyPriceInSelectedUnit);
                    histCmd.Parameters.AddWithValue("@OLD_SELL", oldSellInUnit);
                    histCmd.Parameters.AddWithValue("@NEW_SELL", update.SellPriceInSelectedUnit);
                    histCmd.Parameters.AddWithValue("@CHANGED_BY", userName);
                    histCmd.Parameters.AddWithValue("@CHANGED_AT", now);
                    histCmd.Parameters.AddWithValue("@NOTES", note ?? string.Empty);
                    await histCmd.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var syncMatCmd = new FbCommand("""
                    UPDATE MATERIALS
                    SET REFERENCE_COST = @NEW_COST
                    WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND (MATERIAL_TYPE = 'GOLD' OR MATERIAL_TYPE = 'SILVER')
                      AND (PURITY_CODE = @CODE OR UPPER(CODE) = UPPER(@CODE))
                    """, connection, (FbTransaction)transaction);
                syncMatCmd.Parameters.AddWithValue("@NEW_COST", newBuyChi);
                syncMatCmd.Parameters.AddWithValue("@CODE", existing.Code);
                await syncMatCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { /* swallow */ }
            throw;
        }
    }

    public async Task<long> AddCustomGoldTypeAsync(
        string code,
        string name,
        decimal purityRate,
        decimal buyPriceInUnit,
        decimal sellPriceInUnit,
        string unitCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var buyChi = GoldUnitConverter.ConvertToChi(buyPriceInUnit, unitCode);
        var sellChi = GoldUnitConverter.ConvertToChi(sellPriceInUnit, unitCode);
        var buyGram = Math.Round(buyChi / GoldUnitConverter.GramsPerChi, 2);
        var sellGram = Math.Round(sellChi / GoldUnitConverter.GramsPerChi, 2);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            INSERT INTO GOLD_PRICES
                (COMPANY_ID, CODE, NAME, PURITY_RATE, BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
                 BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM, CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE,
                 UPDATED_BY, UPDATED_AT)
            VALUES
                ((SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'), @CODE, @NAME, @PURITY, @BUY_CHI, @SELL_CHI,
                 @BUY_GRAM, @SELL_GRAM, 'VND',
                 COALESCE((SELECT MAX(DISPLAY_ORDER) FROM GOLD_PRICES
                           WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')), 0) + 1,
                 TRUE, @UPDATED_BY, CURRENT_TIMESTAMP)
            RETURNING ID
            """, connection);
        command.Parameters.AddWithValue("@CODE", code.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@NAME", name.Trim());
        command.Parameters.AddWithValue("@PURITY", purityRate);
        command.Parameters.AddWithValue("@BUY_CHI", buyChi);
        command.Parameters.AddWithValue("@SELL_CHI", sellChi);
        command.Parameters.AddWithValue("@BUY_GRAM", buyGram);
        command.Parameters.AddWithValue("@SELL_GRAM", sellGram);
        command.Parameters.AddWithValue("@UPDATED_BY", ActorName(session));
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task DeleteGoldPriceAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            UPDATE GOLD_PRICES
            SET IS_ACTIVE = FALSE,
                UPDATED_BY = @UPDATED_BY,
                UPDATED_AT = CURRENT_TIMESTAMP
            WHERE ID = @ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@UPDATED_BY", ActorName(session));
        command.Parameters.AddWithValue("@ID", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GoldPriceHistoryRecord>> GetPriceHistoryAsync(
        long? goldPriceId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var result = new List<GoldPriceHistoryRecord>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT FIRST @LIMIT
                   H.ID, H.GOLD_PRICE_ID, H.PRICE_CODE, H.GOLD_NAME, H.UNIT_NAME,
                   H.OLD_BUY_PRICE, H.NEW_BUY_PRICE, H.OLD_SELL_PRICE, H.NEW_SELL_PRICE,
                   COALESCE(H.CHANGED_BY, ''), H.CHANGED_AT, COALESCE(H.NOTES, '')
            FROM GOLD_PRICE_HISTORY H
            WHERE (@HAS_FILTER = 0 OR H.GOLD_PRICE_ID = @PRICE_ID)
            ORDER BY H.CHANGED_AT DESC, H.ID DESC
            """, connection);
        command.Parameters.AddWithValue("@LIMIT", limit <= 0 ? 100 : limit);
        command.Parameters.AddWithValue("@HAS_FILTER", goldPriceId.HasValue ? 1 : 0);
        command.Parameters.AddWithValue("@PRICE_ID", goldPriceId ?? 0);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new GoldPriceHistoryRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetString(9),
                reader.GetDateTime(10),
                reader.GetString(11)));
        }

        return result;
    }

    public async Task<IReadOnlyList<CurrencyRateRecord>> GetCurrencyRatesAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<CurrencyRateRecord>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CURRENCY_CODE, CURRENCY_NAME, BUY_RATE, SELL_RATE, IS_ACTIVE,
                   COALESCE(UPDATED_BY, ''), UPDATED_AT
            FROM CURRENCY_RATES
            WHERE IS_ACTIVE = TRUE
            ORDER BY ID
            """, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CurrencyRateRecord(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4),
                reader.GetBoolean(5),
                reader.GetString(6),
                reader.GetDateTime(7)));
        }

        return result;
    }

    public async Task SaveCurrencyRatesAsync(
        IReadOnlyList<CurrencyRateRecord> rates,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (rates.Count == 0)
            return;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var userName = ActorName(session);
            var now = DateTime.Now;
            foreach (var rate in rates)
            {
                await using var command = new FbCommand("""
                    UPDATE CURRENCY_RATES
                    SET BUY_RATE = @BUY,
                        SELL_RATE = @SELL,
                        UPDATED_BY = @UPDATED_BY,
                        UPDATED_AT = @UPDATED_AT
                    WHERE ID = @ID
                    """, connection, (FbTransaction)transaction);
                command.Parameters.AddWithValue("@BUY", rate.BuyRate);
                command.Parameters.AddWithValue("@SELL", rate.SellRate);
                command.Parameters.AddWithValue("@UPDATED_BY", userName);
                command.Parameters.AddWithValue("@UPDATED_AT", now);
                command.Parameters.AddWithValue("@ID", rate.Id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { /* swallow */ }
            throw;
        }
    }

    public async Task<long> AddCurrencyRateAsync(
        string code,
        string name,
        decimal buyRate,
        decimal sellRate,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalizedCode = code.Trim().ToUpperInvariant();
        var userName = ActorName(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (var checkCmd = new FbCommand(
                         "SELECT COUNT(*) FROM CURRENCY_RATES WHERE CURRENCY_CODE = @CODE", connection))
        {
            checkCmd.Parameters.AddWithValue("@CODE", normalizedCode);
            if (Convert.ToInt32(await checkCmd.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                await using var reactivateCmd = new FbCommand("""
                    UPDATE CURRENCY_RATES
                    SET CURRENCY_NAME = @NAME,
                        BUY_RATE = @BUY,
                        SELL_RATE = @SELL,
                        IS_ACTIVE = TRUE,
                        UPDATED_BY = @UPDATED_BY,
                        UPDATED_AT = CURRENT_TIMESTAMP
                    WHERE CURRENCY_CODE = @CODE
                    """, connection);
                reactivateCmd.Parameters.AddWithValue("@NAME", name.Trim());
                reactivateCmd.Parameters.AddWithValue("@BUY", buyRate);
                reactivateCmd.Parameters.AddWithValue("@SELL", sellRate);
                reactivateCmd.Parameters.AddWithValue("@UPDATED_BY", userName);
                reactivateCmd.Parameters.AddWithValue("@CODE", normalizedCode);
                await reactivateCmd.ExecuteNonQueryAsync(cancellationToken);

                await using var getId = new FbCommand(
                    "SELECT ID FROM CURRENCY_RATES WHERE CURRENCY_CODE = @CODE", connection);
                getId.Parameters.AddWithValue("@CODE", normalizedCode);
                return Convert.ToInt64(await getId.ExecuteScalarAsync(cancellationToken));
            }
        }

        await using var insertCmd = new FbCommand("""
            INSERT INTO CURRENCY_RATES (CURRENCY_CODE, CURRENCY_NAME, BUY_RATE, SELL_RATE, IS_ACTIVE, UPDATED_BY)
            VALUES (@CODE, @NAME, @BUY, @SELL, TRUE, @UPDATED_BY)
            RETURNING ID
            """, connection);
        insertCmd.Parameters.AddWithValue("@CODE", normalizedCode);
        insertCmd.Parameters.AddWithValue("@NAME", name.Trim());
        insertCmd.Parameters.AddWithValue("@BUY", buyRate);
        insertCmd.Parameters.AddWithValue("@SELL", sellRate);
        insertCmd.Parameters.AddWithValue("@UPDATED_BY", userName);
        return Convert.ToInt64(await insertCmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task DeleteCurrencyRateAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            UPDATE CURRENCY_RATES
            SET IS_ACTIVE = FALSE,
                UPDATED_BY = @UPDATED_BY,
                UPDATED_AT = CURRENT_TIMESTAMP
            WHERE ID = @ID
            """, connection);
        command.Parameters.AddWithValue("@UPDATED_BY", ActorName(session));
        command.Parameters.AddWithValue("@ID", id);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ActorName(AuthenticatedSession session) =>
        string.IsNullOrWhiteSpace(session.DisplayName) ? "Administrator" : session.DisplayName.Trim();
}
