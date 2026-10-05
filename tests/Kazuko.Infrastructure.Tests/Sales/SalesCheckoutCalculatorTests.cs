using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

public sealed class SalesCheckoutCalculatorTests
{
    [Fact]
    public void CalculateOldGold_subtracts_stone_and_impurity_then_converts()
    {
        var result = SalesCheckoutCalculator.CalculateOldGold(new(1, 5, 3.2m, .1m, .1m, 75, 1_000_000, null, null));
        Assert.Equal(3m, result.GoldWeightChi);
        Assert.Equal(2.25m, result.ConvertedWeightChi);
        Assert.Equal(2_250_000m, result.AmountVnd);
    }

    [Fact]
    public void CalculateOldGold_rounds_weight_to_four_decimals_and_money_away_from_zero()
    {
        var result = SalesCheckoutCalculator.CalculateOldGold(new(1, 1, 1.23445m, 0, 0, 50, 10_000, null, null));
        Assert.Equal(1.2345m, result.GoldWeightChi);
        Assert.Equal(.6173m, result.ConvertedWeightChi);
        Assert.Equal(6173m, result.AmountVnd);
        Assert.Equal(1m, SalesCheckoutCalculator.CalculateOldGold(new(1, 1, 1, 0, 0, 100, .5m, null, null)).AmountVnd);
    }

    [Fact]
    public void CalculateSaleLine_uses_trusted_weight_and_identifies_price_override()
    {
        var pricing = new SalesLinePricing(1, 2.5m, 100m, 20m, 10m);
        var unchanged = SalesCheckoutCalculator.CalculateSaleLine(new(1, 100, 20, 10, null), pricing);
        Assert.Equal(250m, unchanged.GoldAmountVnd);
        Assert.Equal(280m, unchanged.TotalAmountVnd);
        Assert.False(unchanged.IsPriceOverride);
        var changed = SalesCheckoutCalculator.CalculateSaleLine(new(1, 101, 20, 10, "approved"), pricing);
        Assert.Equal(253m, changed.GoldAmountVnd);
        Assert.Equal(283m, changed.TotalAmountVnd);
        Assert.True(changed.IsPriceOverride);
    }

    [Fact]
    public void CalculateTotals_sums_already_rounded_old_gold_lines()
    {
        var line = SalesCheckoutCalculator.CalculateOldGold(new(1, 1, 1, 0, 0, 100, .5m, null, null));
        var result = SalesCheckoutCalculator.CalculateTotals(Draft(), [], [line, line]);
        Assert.Equal(2m, result.OldGoldTotalVnd);
    }

    [Theory]
    [InlineData(100, 30, 10, 60, 30, 60, 0)]
    [InlineData(100, 90, 10, 0, 90, 0, 0)]
    [InlineData(100, 130, 10, -40, 90, 0, 40)]
    public void CalculateTotals_handles_positive_zero_and_negative_net(decimal gross, decimal oldGold, decimal discount, decimal net, decimal offset, decimal receipt, decimal payout)
    {
        var result = SalesCheckoutCalculator.CalculateTotals(Draft() with { DiscountAmountVnd = discount },
            [new(1, gross, 0, 0, gross, false)], [new(1, 1, oldGold)]);
        Assert.Equal(gross, result.SaleGrossVnd);
        Assert.Equal(90m, result.SaleNetVnd);
        Assert.Equal(net, result.NetAmountVnd);
        Assert.Equal(offset, result.OffsetAmountVnd);
        Assert.Equal(receipt, result.ReceiptAmountVnd);
        Assert.Equal(payout, result.PayoutAmountVnd);
    }

    [Fact]
    public void CalculateTotals_subtracts_receivable_from_receipt()
    {
        var result = SalesCheckoutCalculator.CalculateTotals(Draft() with { ReceivableAmountVnd = 20 },
            [new(1, 100, 0, 0, 100, false)], []);
        Assert.Equal(80m, result.ReceiptAmountVnd);
        Assert.Equal(20m, result.ReceivableAmountVnd);
    }

    internal static SalesCheckoutDraft Draft() => new(Guid.NewGuid(), DateTime.Today, 1, 1, 1, null, 0, [], [], [], 0, null);
}
