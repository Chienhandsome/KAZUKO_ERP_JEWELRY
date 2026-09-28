using Kazuko.Core.Finance;
using Kazuko.Core.Inventory;
using Kazuko.Core.Warehouse;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Warehouse;

public sealed class StockPostingValidatorTests
{
    [Fact]
    public void MaterialReceipt_RejectsMissingPartner()
    {
        var draft = new MaterialReceiptDraft(
            DateTime.Today,
            1,
            StockSourceTypes.Supplier,
            null,
            null,
            null,
            [new MaterialReceiptLineDraft(1, 1, 1000, "VND", 1, null)],
            0,
            Array.Empty<PaymentAllocationDraft>());

        var errors = MaterialReceiptValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("nhà cung cấp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MaterialReceipt_RejectsPaymentOverTotal()
    {
        var draft = new MaterialReceiptDraft(
            DateTime.Today,
            1,
            StockSourceTypes.Supplier,
            5,
            null,
            null,
            [new MaterialReceiptLineDraft(1, 1, 1_000_000, "VND", 1, null)],
            2_000_000,
            [new PaymentAllocationDraft(PaymentMethods.Cash, 2_000_000, 1, null, null)]);

        var errors = MaterialReceiptValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("vượt tổng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PaymentAllocation_MustEqualPaidAmount()
    {
        var errors = PaymentAllocationValidator.Validate(
            [
                new PaymentAllocationDraft(PaymentMethods.Cash, 5_000_000, 1, null, null),
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 5_000_000, null, 2, null)
            ],
            paidAmountVnd: 9_000_000);

        Assert.Contains(errors, e => e.Contains("phải bằng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PaymentAllocation_SplitTenMillion_IsValid()
    {
        var errors = PaymentAllocationValidator.Validate(
            [
                new PaymentAllocationDraft(PaymentMethods.Cash, 5_000_000, 1, null, null),
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 5_000_000, null, 2, null)
            ],
            paidAmountVnd: 10_000_000);

        Assert.Empty(errors);
    }

    [Fact]
    public void CraftSettlement_RejectsOverReturnWithoutOverride()
    {
        var draft = new CraftReturnReceiptDraft(
            10,
            DateTime.Today,
            1,
            "A",
            "B",
            100_000,
            0,
            Array.Empty<PaymentAllocationDraft>(),
            null,
            [new CraftSettlementLineDraft(1, 0, 20, 0, 0, null)],
            AllowOverReturn: false);

        var errors = CraftSettlementValidator.Validate(draft, new Dictionary<long, decimal> { [1] = 10m });
        Assert.Contains(errors, e => e.Contains("vượt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FinishedGoods_RejectsUnconfirmedOrder()
    {
        Assert.False(FinishedGoodsIssueValidator.IsConfirmedSalesOrderStatus("DRAFT"));
        Assert.False(FinishedGoodsIssueValidator.IsConfirmedSalesOrderStatus("CANCELLED"));
        Assert.True(FinishedGoodsIssueValidator.IsConfirmedSalesOrderStatus("CONFIRMED"));
        Assert.True(FinishedGoodsIssueValidator.IsConfirmedSalesOrderStatus("DEPOSITED"));
    }

    [Fact]
    public void ManualFinishedReceipt_IsRejected()
    {
        Assert.True(FinishedGoodsIssueValidator.RejectManualFinishedReceipt(
            StockDocumentTypes.Receipt, "FINISHED_RECEIPT", null));
        Assert.Throws<InvalidOperationException>(() =>
            FinishedGoodsIssueValidator.EnsureNotManualFinishedGoodsReceipt(
                StockDocumentTypes.Receipt, "FINISHED_RECEIPT", null, isMaterialOnlyReceipt: false));
    }

    [Fact]
    public void GoldUnit_ConvertsGramToChi()
    {
        var qty = StockUnitConverter.ToStandardQuantity(3.75m, "g", MaterialTypes.Gold);
        Assert.Equal(1m, qty);
        var price = StockUnitConverter.ToStandardUnitPrice(100_000m, "GRAM", MaterialTypes.Gold);
        Assert.Equal(375_000m, price);
    }

    [Fact]
    public void Settlement_LossAndVariance_Math()
    {
        var loss = CraftSettlementValidator.ComputeLoss(10, 7, 1, 0.5m);
        Assert.Equal(1.5m, loss);
        Assert.Equal(0.5m, CraftSettlementValidator.ComputeVariance(loss, 1m));
    }
}
