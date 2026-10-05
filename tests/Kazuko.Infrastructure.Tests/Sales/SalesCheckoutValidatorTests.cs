using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

public sealed class SalesCheckoutValidatorTests
{
    private static readonly SalesPermissionSnapshot AllPermissions = new(true, true, true, true, true, true);
    private static SalesValidationContext Context(bool walkIn = false, bool hasOverride = false) => new(walkIn, 1, AllPermissions, hasOverride);
    private static SalesCheckoutDraft Sale() => SalesCheckoutCalculatorTests.Draft() with
    {
        SaleLines = [new(1, 100, 0, 0, null)],
        Allocations = [new(PaymentMethods.Cash, 100, 1, null, null)]
    };
    private static SalesCheckoutTotals Totals(decimal net = 100, decimal debt = 0) => new(100, 0, 100, 100 - net, Math.Min(100, 100 - net), net, net > 0 ? net - debt : 0, net < 0 ? -net : 0, debt);
    private static OldGoldLineDraft OldGold() => new(1, 1, 1, 0, 0, 100, 100, null, null);

    [Fact]
    public void Accepts_valid_sale_trade_in_and_old_gold_purchase()
    {
        Assert.Empty(SalesCheckoutValidator.Validate(Sale(), Totals(), Context()));
        var trade = Sale() with { OldGoldLines = [OldGold()], OldGoldDestinationLocationId = 2, Allocations = [] };
        Assert.Empty(SalesCheckoutValidator.Validate(trade, Totals(0), Context()));
        var purchase = trade with { SaleLines = [], SourceLocationId = 0, Allocations = [new(PaymentMethods.BankTransfer, 100, null, 1, null)] };
        Assert.Empty(SalesCheckoutValidator.Validate(purchase, new(0, 0, 0, 100, 0, -100, 0, 100, 0), Context()));
    }

    [Fact]
    public void Rejects_walk_in_customer_for_old_gold()
    {
        Assert.Contains(SalesCheckoutValidator.Validate(Sale() with { OldGoldLines = [OldGold()], OldGoldDestinationLocationId = 2 }, Totals(), Context(true)), x => x.Contains("khách lẻ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_debt_without_named_customer_or_permission()
    {
        var draft = Sale() with { ReceivableAmountVnd = 100, Allocations = [] };
        Assert.Contains(SalesCheckoutValidator.Validate(draft, Totals(100, 100), Context(true)), x => x.Contains("khách lẻ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(SalesCheckoutValidator.Validate(draft, Totals(100, 100), Context() with { Permissions = AllPermissions with { CanApproveDebt = false } }), x => x.Contains("quyền"));
        Assert.Empty(SalesCheckoutValidator.Validate(draft, Totals(100, 100), Context()));
    }

    [Fact]
    public void Rejects_override_without_reason_or_permission()
    {
        Assert.Contains(SalesCheckoutValidator.Validate(Sale(), Totals(), Context(hasOverride: true)), x => x.Contains("lý do"));
        var draft = Sale() with { SaleLines = [new(1, 100, 0, 0, "approved")] };
        Assert.Contains(SalesCheckoutValidator.Validate(draft, Totals(), Context(hasOverride: true) with { Permissions = AllPermissions with { CanOverridePrice = false } }), x => x.Contains("quyền"));
        Assert.Empty(SalesCheckoutValidator.Validate(draft, Totals(), Context(hasOverride: true)));
    }

    [Fact]
    public void Rejects_changed_salesperson_without_permission()
    {
        Assert.Contains(SalesCheckoutValidator.Validate(Sale() with { SalespersonId = 2 }, Totals(), Context() with { Permissions = AllPermissions with { CanChangeSalesperson = false } }), x => x.Contains("nhân viên"));
    }

    [Fact]
    public void Rejects_card_for_outgoing_payment()
    {
        var draft = Sale() with { Allocations = [new(PaymentMethods.Card, 100, null, 1, null)] };
        Assert.Contains(SalesCheckoutValidator.Validate(draft, Totals(-100), Context()), x => x.Contains("thẻ"));
        Assert.Empty(SalesCheckoutValidator.Validate(draft, Totals(), Context()));
    }

    [Theory]
    [InlineData(-1, 0, 0, 100)]
    [InlineData(1, -1, 0, 100)]
    [InlineData(1, 0, -1, 100)]
    [InlineData(1, .6, .5, 100)]
    [InlineData(1, 0, 0, -1)]
    [InlineData(1, 0, 0, 101)]
    public void Rejects_negative_or_overdrawn_weights(decimal total, decimal stone, decimal impurity, decimal percent)
    {
        var draft = Sale() with { OldGoldDestinationLocationId = 2, OldGoldLines = [OldGold() with { TotalWeightChi = total, StoneWeightChi = stone, ImpurityWeightChi = impurity, ValuePercent = percent }] };
        Assert.NotEmpty(SalesCheckoutValidator.Validate(draft, Totals(), Context()));
    }

    [Theory]
    [InlineData(100, 99, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(-100, 99, 0)]
    [InlineData(-100, 100, 1)]
    [InlineData(100, 110, 0)]
    public void Rejects_payment_mismatch(decimal net, decimal payment, decimal debt)
    {
        var draft = Sale() with { ReceivableAmountVnd = debt, Allocations = [new(PaymentMethods.Cash, payment, 1, null, null)] };
        Assert.NotEmpty(SalesCheckoutValidator.Validate(draft, Totals(net, debt), Context()));
    }

    [Fact]
    public void Rejects_missing_post_permission_and_empty_or_duplicate_products()
    {
        Assert.NotEmpty(SalesCheckoutValidator.Validate(Sale(), Totals(), Context() with { Permissions = AllPermissions with { CanPost = false } }));
        Assert.NotEmpty(SalesCheckoutValidator.Validate(Sale() with { SaleLines = [] }, Totals(), Context()));
        Assert.NotEmpty(SalesCheckoutValidator.Validate(Sale() with { SaleLines = [new(1, 100, 0, 0, null), new(1, 100, 0, 0, null)] }, Totals(), Context()));
    }

    [Fact]
    public void Rejects_missing_ids_negative_prices_and_excess_discount()
    {
        var draft = Sale() with { ClientRequestId = Guid.Empty, CustomerId = 0, SalespersonId = 0, SourceLocationId = 0, DiscountAmountVnd = 101, ReceivableAmountVnd = -1, SaleLines = [new(0, -1, -1, -1, null)] };
        Assert.True(SalesCheckoutValidator.Validate(draft, Totals(), Context()).Count >= 7);
    }
}
