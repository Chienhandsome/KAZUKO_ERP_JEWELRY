using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

public sealed class CraftOrderValidatorTests
{
    [Fact]
    public void LineTotal_Multiplies_UnitAmounts_By_Quantity()
    {
        var line = new CraftOrderLineDraft(
            "Nhẫn", "750", 2, 1.5m, 1000m, 200m, 50m, null);

        // (1.5*1000 + 200 + 50) * 2 = 3500
        Assert.Equal(3500m, CraftOrderValidator.LineTotal(line));
    }

    [Fact]
    public void Validate_Requires_Deposit_And_Named_Customer()
    {
        var draft = new CraftOrderDraft(
            0,
            DateTime.Today,
            null,
            [new CraftOrderLineDraft("Nhẫn", "750", 1, 1m, 1000m, 0m, 0m, null)],
            [],
            null);

        var errors = CraftOrderValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("khách hàng", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("đặt cọc", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Rejects_Deposit_Above_Quoted_Total()
    {
        var draft = new CraftOrderDraft(
            10,
            DateTime.Today,
            1,
            [new CraftOrderLineDraft("Nhẫn", "750", 1, 1m, 1000m, 0m, 0m, null)],
            [new PaymentAllocationDraft(PaymentMethods.Cash, 2000m, 1, null, null)],
            null);

        var errors = CraftOrderValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("vượt tổng báo giá", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_Accepts_Valid_Deposit()
    {
        var draft = new CraftOrderDraft(
            10,
            DateTime.Today,
            1,
            [new CraftOrderLineDraft("Nhẫn", "750", 1, 1m, 1000m, 200m, 0m, null)],
            [new PaymentAllocationDraft(PaymentMethods.Cash, 500m, 1, null, null)],
            null);

        Assert.Empty(CraftOrderValidator.Validate(draft));
    }

    [Fact]
    public void ValidateCollectRemaining_Must_Cover_Remaining()
    {
        var cash = new PaymentAllocationDraft(PaymentMethods.Cash, 400_000m, 1, null, null);
        Assert.Empty(CraftOrderValidator.ValidateCollectRemaining(
            1_000_000m,
            new CraftOrderCollectDraft(10, DateTime.Today, [cash], 600_000m, null)));

        var mismatch = CraftOrderValidator.ValidateCollectRemaining(
            1_000_000m,
            new CraftOrderCollectDraft(10, DateTime.Today, [cash], 100_000m, null));
        Assert.Contains(mismatch, e => e.Contains("còn lại", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(CraftOrderValidator.ValidateCollectRemaining(
            500_000m,
            new CraftOrderCollectDraft(10, DateTime.Today, [], 500_000m, null)));
    }

    [Fact]
    public void ValidateDeliver_Requires_Products_And_Covers_Remaining()
    {
        var cash = new PaymentAllocationDraft(PaymentMethods.Cash, 400_000m, 1, null, null);
        Assert.Empty(CraftOrderValidator.ValidateDeliver(
            new CraftOrderDeliverDraft(
                10, DateTime.Now, 1, "A", "B", null, [101], [cash], 600_000m),
            remainingDueVnd: 1_000_000m));

        var noProducts = CraftOrderValidator.ValidateDeliver(
            new CraftOrderDeliverDraft(
                10, DateTime.Now, 1, "A", "B", null, [], null, 0),
            remainingDueVnd: 0);
        Assert.Contains(noProducts, e => e.Contains("thành phẩm", StringComparison.OrdinalIgnoreCase));
    }
}
