using Dapper;
using Kazuko.Core.PriceBoard;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.PriceBoard;

/// <summary>Read-only retail price source for the detached display process.</summary>
public sealed class FirebirdTvPriceSource : IPriceBoardTvPriceSource
{
    public async Task<IReadOnlyList<GoldPriceRecord>> LoadAsync(long companyId, CancellationToken cancellationToken = default)
    {
        if (companyId <= 0) throw new ArgumentOutOfRangeException(nameof(companyId));
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<GoldPriceRecord>(new CommandDefinition("""
            SELECT ID AS Id, COMPANY_ID AS CompanyId, CODE AS Code, NAME AS Name, PURITY_RATE AS PurityRate,
                   BUY_PRICE_PER_CHI AS BuyPricePerChi, SELL_PRICE_PER_CHI AS SellPricePerChi,
                   BUY_PRICE_PER_GRAM AS BuyPricePerGram, SELL_PRICE_PER_GRAM AS SellPricePerGram,
                   CURRENCY_CODE AS CurrencyCode, DISPLAY_ORDER AS DisplayOrder, IS_ACTIVE AS IsActive,
                   COALESCE(UPDATED_BY, '') AS UpdatedBy, UPDATED_AT AS UpdatedAt
            FROM GOLD_PRICES WHERE COMPANY_ID=@companyId AND IS_ACTIVE=TRUE AND CURRENCY_CODE='VND'
            ORDER BY DISPLAY_ORDER, ID
            """, new { companyId }, cancellationToken: cancellationToken));
        return rows.AsList();
    }
}
