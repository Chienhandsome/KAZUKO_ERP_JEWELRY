using System.Globalization;
using System.Text;
using FirebirdSql.Data.FirebirdClient;

namespace Kazuko.Infrastructure.Warehouse;

internal sealed record WarehouseGoldPriceSnapshot(
    long Id,
    string Code,
    string Name,
    decimal IssuePricePerChi);

internal static class FirebirdWarehouseGoldPricingSupport
{
    public static Task<WarehouseGoldPriceSnapshot> ResolveAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string? purityOrCode,
        CancellationToken cancellationToken) =>
        ResolveAsync(connection, transaction, companyId, goldTypeId: null, purityOrCode, cancellationToken);

    public static async Task<WarehouseGoldPriceSnapshot> ResolveAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long? goldTypeId,
        string? purityOrCode,
        CancellationToken cancellationToken)
    {
        if (goldTypeId is > 0)
        {
            var byId = await TryResolveByGoldTypeIdAsync(
                connection, transaction, companyId, goldTypeId.Value, cancellationToken);
            if (byId is not null)
                return EnsurePositive(byId);
        }

        var requestedToken = PurityToken(purityOrCode);
        var requestedText = Normalize(purityOrCode);
        var candidates = new List<(WarehouseGoldPriceSnapshot Price, string Token, string Text)>();

        await using var command = new FbCommand("""
            SELECT W.ID, W.GOLD_TYPE_CODE, W.GOLD_TYPE_NAME, W.PURITY_RATE, W.ISSUE_PRICE_PER_CHI
            FROM WAREHOUSE_GOLD_PRICES W
            WHERE W.COMPANY_ID = @CID AND W.IS_ACTIVE = TRUE
            ORDER BY W.PURITY_RATE DESC, W.ID
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var price = new WarehouseGoldPriceSnapshot(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(4));
            var combined = $"{price.Code} {price.Name}";
            candidates.Add((price, PurityToken(combined, reader.GetDecimal(3)), Normalize(combined)));
        }

        var match = candidates.FirstOrDefault(x =>
            requestedToken.Length > 0 && x.Token == requestedToken);
        if (match.Price is null && requestedText.Length > 0)
            match = candidates.FirstOrDefault(x =>
                x.Text == requestedText || x.Text.Contains(requestedText) || requestedText.Contains(x.Text));

        if (match.Price is null)
            throw new InvalidOperationException(
                $"Chưa cấu hình giá vàng kho cho loại vàng '{purityOrCode ?? "(trống)"}'.");
        return EnsurePositive(match.Price);
    }

    private static async Task<WarehouseGoldPriceSnapshot?> TryResolveByGoldTypeIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long goldTypeId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT W.ID, W.GOLD_TYPE_CODE, W.GOLD_TYPE_NAME, W.ISSUE_PRICE_PER_CHI
            FROM WAREHOUSE_GOLD_PRICES W
            WHERE W.COMPANY_ID = @CID
              AND W.GOLD_TYPE_ID = @TID
              AND W.IS_ACTIVE = TRUE
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@TID", FbDbType.BigInt).Value = goldTypeId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new WarehouseGoldPriceSnapshot(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3));
    }

    private static WarehouseGoldPriceSnapshot EnsurePositive(WarehouseGoldPriceSnapshot price)
    {
        if (price.IssuePricePerChi <= 0)
            throw new InvalidOperationException($"Giá vàng kho '{price.Code}' phải lớn hơn 0.");
        return price;
    }

    private static string PurityToken(string? value, decimal? purityRate = null)
    {
        var normalized = Normalize(value);
        if (normalized.Contains("9999")) return "9999";
        if (normalized.Contains("999")) return "999";
        if (normalized.Contains("916")) return "916";
        if (normalized.Contains("750")) return "750";
        if (normalized.Contains("680")) return "680";
        if (normalized.Contains("650")) return "650";
        if (normalized.Contains("610")) return "610";
        if (normalized.Contains("585")) return "585";
        if (normalized.Contains("417") || normalized.Contains("416")) return "10K";
        if (normalized.Contains("375")) return "375";
        if (normalized.Contains("925")) return "925";
        if (normalized.Contains("950")) return "950";
        if (normalized.Contains("24K")) return "9999";
        if (normalized.Contains("22K")) return "916";
        if (normalized.Contains("18K")) return "750";
        if (normalized.Contains("16K")) return "680";
        if (normalized.Contains("14K")) return "585";
        if (normalized.Contains("10K")) return "10K";
        if (normalized.Contains("9K")) return "375";

        if (purityRate is > 0)
        {
            var rate = purityRate.Value <= 1 ? purityRate.Value * 1000 : purityRate.Value;
            return decimal.Round(rate, 0).ToString(CultureInfo.InvariantCulture);
        }

        return string.Empty;
    }

    private static string Normalize(string? value)
    {
        var source = (value ?? string.Empty).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToUpperInvariant(ch));
        }
        return builder.ToString();
    }
}
