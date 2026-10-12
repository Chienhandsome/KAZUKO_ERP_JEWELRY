using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.PriceBoard;

// Dapper pilot: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record (vd. COMPANY_ID AS CompanyId).
public sealed class FirebirdPriceBoardRepository : IPriceBoardRepository
{
    public async Task<IReadOnlyList<GoldPriceRecord>> GetGoldPricesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<GoldPriceRecord>(
            new CommandDefinition("""
                SELECT ID AS Id, COMPANY_ID AS CompanyId, CODE AS Code, NAME AS Name, PURITY_RATE AS PurityRate,
                       BUY_PRICE_PER_CHI AS BuyPricePerChi, SELL_PRICE_PER_CHI AS SellPricePerChi,
                       BUY_PRICE_PER_GRAM AS BuyPricePerGram, SELL_PRICE_PER_GRAM AS SellPricePerGram,
                       CURRENCY_CODE AS CurrencyCode, DISPLAY_ORDER AS DisplayOrder, IS_ACTIVE AS IsActive,
                       COALESCE(UPDATED_BY, '') AS UpdatedBy, UPDATED_AT AS UpdatedAt
                FROM GOLD_PRICES
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND IS_ACTIVE = TRUE
                ORDER BY DISPLAY_ORDER, ID
                """, cancellationToken: cancellationToken));
        return rows.AsList();
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
            var fetched = await connection.QueryAsync<GoldPriceRecord>(
                new CommandDefinition("""
                    SELECT ID AS Id, COMPANY_ID AS CompanyId, CODE AS Code, NAME AS Name, PURITY_RATE AS PurityRate,
                           BUY_PRICE_PER_CHI AS BuyPricePerChi, SELL_PRICE_PER_CHI AS SellPricePerChi,
                           BUY_PRICE_PER_GRAM AS BuyPricePerGram, SELL_PRICE_PER_GRAM AS SellPricePerGram,
                           CURRENCY_CODE AS CurrencyCode, DISPLAY_ORDER AS DisplayOrder, IS_ACTIVE AS IsActive,
                           COALESCE(UPDATED_BY, '') AS UpdatedBy, UPDATED_AT AS UpdatedAt
                    FROM GOLD_PRICES
                    WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, transaction: transaction, cancellationToken: cancellationToken));
            foreach (var record in fetched)
                currentPrices[record.Id] = record;

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

                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE GOLD_PRICES
                        SET BUY_PRICE_PER_CHI = @BUY_CHI,
                            SELL_PRICE_PER_CHI = @SELL_CHI,
                            BUY_PRICE_PER_GRAM = @BUY_GRAM,
                            SELL_PRICE_PER_GRAM = @SELL_GRAM,
                            UPDATED_BY = @UPDATED_BY,
                            UPDATED_AT = @UPDATED_AT
                        WHERE ID = @ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new
                        {
                            BUY_CHI = newBuyChi,
                            SELL_CHI = newSellChi,
                            BUY_GRAM = newBuyGram,
                            SELL_GRAM = newSellGram,
                            UPDATED_BY = userName,
                            UPDATED_AT = now,
                            ID = update.Id
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        INSERT INTO GOLD_PRICE_HISTORY
                            (GOLD_PRICE_ID, PRICE_CODE, GOLD_NAME, UNIT_NAME,
                             OLD_BUY_PRICE, NEW_BUY_PRICE, OLD_SELL_PRICE, NEW_SELL_PRICE,
                             CHANGED_BY, CHANGED_AT, NOTES)
                        VALUES
                            (@PRICE_ID, @CODE, @NAME, @UNIT,
                             @OLD_BUY, @NEW_BUY, @OLD_SELL, @NEW_SELL,
                             @CHANGED_BY, @CHANGED_AT, @NOTES)
                        """,
                        new
                        {
                            PRICE_ID = update.Id,
                            CODE = existing.Code,
                            NAME = existing.Name,
                            UNIT = GoldUnitConverter.GetUnit(update.UnitCode).Name,
                            OLD_BUY = oldBuyInUnit,
                            NEW_BUY = update.BuyPriceInSelectedUnit,
                            OLD_SELL = oldSellInUnit,
                            NEW_SELL = update.SellPriceInSelectedUnit,
                            CHANGED_BY = userName,
                            CHANGED_AT = now,
                            NOTES = note ?? string.Empty
                        },
                        transaction,
                        cancellationToken: cancellationToken));

                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE MATERIALS
                        SET REFERENCE_COST = @NEW_COST
                        WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                          AND (MATERIAL_TYPE = 'GOLD' OR MATERIAL_TYPE = 'SILVER')
                          AND (PURITY_CODE = @CODE OR UPPER(CODE) = UPPER(@CODE))
                        """,
                        new { NEW_COST = newBuyChi, CODE = existing.Code },
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
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("""
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
                """,
                new
                {
                    CODE = code.Trim().ToUpperInvariant(),
                    NAME = name.Trim(),
                    PURITY = purityRate,
                    BUY_CHI = buyChi,
                    SELL_CHI = sellChi,
                    BUY_GRAM = buyGram,
                    SELL_GRAM = sellGram,
                    UPDATED_BY = ActorName(session)
                },
                cancellationToken: cancellationToken));
    }

    public async Task DeleteGoldPriceAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE GOLD_PRICES
                SET IS_ACTIVE = FALSE,
                    UPDATED_BY = @UPDATED_BY,
                    UPDATED_AT = CURRENT_TIMESTAMP
                WHERE ID = @ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { UPDATED_BY = ActorName(session), ID = id },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<GoldPriceHistoryRecord>> GetPriceHistoryAsync(
        long? goldPriceId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<GoldPriceHistoryRecord>(
            new CommandDefinition("""
                SELECT FIRST @LIMIT
                       H.ID AS Id, H.GOLD_PRICE_ID AS GoldPriceId, H.PRICE_CODE AS PriceCode,
                       H.GOLD_NAME AS GoldName, H.UNIT_NAME AS UnitName,
                       H.OLD_BUY_PRICE AS OldBuyPrice, H.NEW_BUY_PRICE AS NewBuyPrice,
                       H.OLD_SELL_PRICE AS OldSellPrice, H.NEW_SELL_PRICE AS NewSellPrice,
                       COALESCE(H.CHANGED_BY, '') AS ChangedBy, H.CHANGED_AT AS ChangedAt,
                       COALESCE(H.NOTES, '') AS Notes
                FROM GOLD_PRICE_HISTORY H
                WHERE (@HAS_FILTER = 0 OR H.GOLD_PRICE_ID = @PRICE_ID)
                ORDER BY H.CHANGED_AT DESC, H.ID DESC
                """,
                new
                {
                    LIMIT = limit <= 0 ? 100 : limit,
                    HAS_FILTER = goldPriceId.HasValue ? 1 : 0,
                    PRICE_ID = goldPriceId ?? 0
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<CurrencyRateRecord>> GetCurrencyRatesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CurrencyRateRecord>(
            new CommandDefinition("""
                SELECT ID AS Id, CURRENCY_CODE AS CurrencyCode, CURRENCY_NAME AS CurrencyName,
                       BUY_RATE AS BuyRate, SELL_RATE AS SellRate, IS_ACTIVE AS IsActive,
                       COALESCE(UPDATED_BY, '') AS UpdatedBy, UPDATED_AT AS UpdatedAt
                FROM CURRENCY_RATES
                WHERE IS_ACTIVE = TRUE
                ORDER BY ID
                """, cancellationToken: cancellationToken));
        return rows.AsList();
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
                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE CURRENCY_RATES
                        SET BUY_RATE = @BUY,
                            SELL_RATE = @SELL,
                            UPDATED_BY = @UPDATED_BY,
                            UPDATED_AT = @UPDATED_AT
                        WHERE ID = @ID
                        """,
                        new
                        {
                            BUY = rate.BuyRate,
                            SELL = rate.SellRate,
                            UPDATED_BY = userName,
                            UPDATED_AT = now,
                            ID = rate.Id
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

        var existingCount = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM CURRENCY_RATES WHERE CURRENCY_CODE = @CODE",
                new { CODE = normalizedCode },
                cancellationToken: cancellationToken));
        if (existingCount > 0)
        {
            await connection.ExecuteAsync(
                new CommandDefinition("""
                    UPDATE CURRENCY_RATES
                    SET CURRENCY_NAME = @NAME,
                        BUY_RATE = @BUY,
                        SELL_RATE = @SELL,
                        IS_ACTIVE = TRUE,
                        UPDATED_BY = @UPDATED_BY,
                        UPDATED_AT = CURRENT_TIMESTAMP
                    WHERE CURRENCY_CODE = @CODE
                    """,
                    new
                    {
                        NAME = name.Trim(),
                        BUY = buyRate,
                        SELL = sellRate,
                        UPDATED_BY = userName,
                        CODE = normalizedCode
                    },
                    cancellationToken: cancellationToken));

            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(
                    "SELECT ID FROM CURRENCY_RATES WHERE CURRENCY_CODE = @CODE",
                    new { CODE = normalizedCode },
                    cancellationToken: cancellationToken));
        }

        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("""
                INSERT INTO CURRENCY_RATES (CURRENCY_CODE, CURRENCY_NAME, BUY_RATE, SELL_RATE, IS_ACTIVE, UPDATED_BY)
                VALUES (@CODE, @NAME, @BUY, @SELL, TRUE, @UPDATED_BY)
                RETURNING ID
                """,
                new
                {
                    CODE = normalizedCode,
                    NAME = name.Trim(),
                    BUY = buyRate,
                    SELL = sellRate,
                    UPDATED_BY = userName
                },
                cancellationToken: cancellationToken));
    }

    public async Task DeleteCurrencyRateAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE CURRENCY_RATES
                SET IS_ACTIVE = FALSE,
                    UPDATED_BY = @UPDATED_BY,
                    UPDATED_AT = CURRENT_TIMESTAMP
                WHERE ID = @ID
                """,
                new { UPDATED_BY = ActorName(session), ID = id },
                cancellationToken: cancellationToken));
    }

    private static string ActorName(AuthenticatedSession session) =>
        string.IsNullOrWhiteSpace(session.DisplayName) ? "Administrator" : session.DisplayName.Trim();
}
