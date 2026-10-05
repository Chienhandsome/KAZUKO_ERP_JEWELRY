namespace Kazuko.Core.Sales;

public static class SalesCheckoutCalculator
{
    public static OldGoldLineAmounts CalculateOldGold(OldGoldLineDraft line)
    {
        var gold = decimal.Round(line.TotalWeightChi - line.StoneWeightChi - line.ImpurityWeightChi, 4, MidpointRounding.AwayFromZero);
        var converted = decimal.Round(gold * line.ValuePercent / 100m, 4, MidpointRounding.AwayFromZero);
        return new(gold, converted, Money(converted * line.AppliedBuyPricePerChi));
    }

    public static SalesLineAmounts CalculateSaleLine(SalesLineDraft line, SalesLinePricing pricing)
    {
        if (line.FinishedProductId != pricing.FinishedProductId)
            throw new ArgumentException("Product pricing must belong to the sale line.", nameof(pricing));
        var gold = Money(pricing.GoldWeightChi * line.AppliedGoldPricePerChi);
        var labor = Money(line.AppliedLaborAmountVnd);
        var stone = Money(line.AppliedStoneAmountVnd);
        return new(line.FinishedProductId, gold, labor, stone, gold + labor + stone,
            line.AppliedGoldPricePerChi != pricing.SourceGoldPricePerChi ||
            line.AppliedLaborAmountVnd != pricing.SourceLaborAmountVnd ||
            line.AppliedStoneAmountVnd != pricing.SourceStoneAmountVnd);
    }

    public static SalesCheckoutTotals CalculateTotals(SalesCheckoutDraft draft, IReadOnlyList<SalesLineAmounts> saleLines, IReadOnlyList<OldGoldLineAmounts> oldGold)
    {
        var gross = saleLines.Sum(x => x.TotalAmountVnd);
        var discount = Money(draft.DiscountAmountVnd);
        var saleNet = gross - discount;
        var oldTotal = oldGold.Sum(x => x.AmountVnd);
        var net = saleNet - oldTotal;
        var debt = Money(draft.ReceivableAmountVnd);
        return new(gross, discount, saleNet, oldTotal, Math.Min(saleNet, oldTotal), net,
            net > 0 ? net - debt : 0, net < 0 ? -net : 0, debt);
    }

    private static decimal Money(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
}
