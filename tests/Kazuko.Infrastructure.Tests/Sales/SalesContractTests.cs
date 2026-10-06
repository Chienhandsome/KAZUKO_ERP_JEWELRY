using Kazuko.Core.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

public sealed class SalesContractTests
{
    [Fact]
    public void Posting_result_distinguishes_new_checkout_from_idempotent_replay()
    {
        var totals = new SalesCheckoutTotals(100, 0, 100, 0, 0, 100, 100, 0, 0);
        var posted = new SalesCheckoutResult(1, "MB26/00001", SalesCheckoutTypes.Sale, totals, 2, null, false);
        var replay = posted with { AlreadyPosted = true };
        Assert.False(posted.AlreadyPosted);
        Assert.True(replay.AlreadyPosted);
        Assert.Equal(posted.CheckoutId, replay.CheckoutId);
        Assert.Equal(posted.Totals, replay.Totals);
    }

    [Fact]
    public void Product_lookup_carries_purity_version_and_all_pricing_weights()
    {
        var product = new SalesProductOption(1, "SP001", "Ring", 2, "Gold", 3,
            "750", 4, 3.2m, .2m, 3m, 100, 20, 10);
        Assert.Equal(2L, product.RetailGoldPriceId);
        Assert.Equal("Gold", product.RetailGoldPriceName);
        Assert.Equal("750", product.GoldPurity);
        Assert.Equal(4L, product.RowVersion);
        Assert.Equal(3.2m, product.TotalWeightChi);
        Assert.Equal(.2m, product.StoneWeightChi);
        var pricing = new SalesLinePricing(product.FinishedProductId, product.GoldWeightChi,
            product.SourceGoldPricePerChi, product.SourceLaborAmountVnd, product.SourceStoneAmountVnd);
        var amounts = SalesCheckoutCalculator.CalculateSaleLine(new(product.FinishedProductId, 100, 20, 10, null), pricing);
        Assert.Equal(330m, amounts.TotalAmountVnd);
    }

    [Fact]
    public void Old_gold_material_lookup_carries_explicit_price_source_identity()
    {
        var material = new OldGoldMaterialOption(1, "GOLD_750", "Gold 750", 2, 75m, 77m);

        Assert.Equal(2L, material.OldGoldPriceId);
        Assert.Equal(75m, material.ValuePercent);
        Assert.Equal(77m, material.SourceBuyPricePerChi);
    }

    [Fact]
    public void Receipt_preserves_cashier_snapshot_independently_from_salesperson()
    {
        var detail = new SalesCheckoutDetail(1, "MB26/00001", SalesCheckoutTypes.Sale, new DateTime(2026, 10, 5),
            2, "Customer", "0123456789", 3, "Salesperson", 4, "Cashier at posting", 5, null,
            [], [], [], new(100, 0, 100, 0, 0, 100, 100, 0, 0), null);
        var receipt = new SalesReceiptDto("Company", "Branch", "Address", "Phone", detail);
        Assert.Equal("Cashier at posting", receipt.Checkout.CreatedByDisplayName);
        Assert.Equal("Salesperson", receipt.Checkout.SalespersonName);
        Assert.Equal(4L, receipt.Checkout.CreatedBy);
    }

    [Fact]
    public void Old_gold_only_history_can_preserve_missing_source_location()
    {
        var detail = new SalesCheckoutDetail(1, "MVC26/00001", SalesCheckoutTypes.OldGoldPurchase,
            new DateTime(2026, 10, 5), 2, "Customer", null, 3, "Salesperson", 4, "Cashier",
            null, 5, [], [], [], new(0, 0, 0, 100, 0, -100, 0, 100, 0), null);

        Assert.Null(detail.SourceLocationId);
    }

    [Fact]
    public void Validation_context_requires_explicit_trusted_override_evidence()
    {
        var constructor = typeof(SalesValidationContext).GetConstructors()
            .Single(x => x.GetParameters().Any(p => p.Name == "HasPriceOverride"));
        var evidence = constructor.GetParameters().Single(p => p.Name == "HasPriceOverride");
        Assert.False(evidence.IsOptional);
        Assert.False(evidence.HasDefaultValue);
    }
}
