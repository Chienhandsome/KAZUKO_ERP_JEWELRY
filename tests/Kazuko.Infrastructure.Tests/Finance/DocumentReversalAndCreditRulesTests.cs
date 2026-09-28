using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

public sealed class DocumentReversalAndCreditRulesTests
{
    [Fact]
    public void PartyCredit_IsNotCashVoucherKind()
    {
        Assert.Equal("SUPPLIER_RETURN_HOLD", PartyCreditKinds.SupplierReturnHold);
        Assert.Equal("CUSTOMER_RETURN_HOLD", PartyCreditKinds.CustomerReturnHold);
        Assert.DoesNotContain("CASH", PartyCreditKinds.SupplierReturnHold);
        Assert.DoesNotContain("VOUCHER", PartyCreditKinds.CustomerReturnHold);
    }

    [Fact]
    public void ReversalSourceTypes_AreDistinctFromPaymentMethods()
    {
        Assert.Equal("STOCK_DOCUMENT", DocumentReversalSourceTypes.StockDocument);
        Assert.Equal("FINANCIAL_SETTLEMENT", DocumentReversalSourceTypes.FinancialSettlement);
        Assert.NotEqual(PaymentMethods.Cash, DocumentReversalSourceTypes.CashVoucher);
    }

    [Fact]
    public void PartyCreditDraft_RequiresPositiveAmount()
    {
        var draft = new PartyCreditDraft(
            SettlementPartyTypes.Supplier,
            1,
            PartyCreditKinds.SupplierReturnHold,
            0,
            "STOCK_DOCUMENT",
            10,
            "hold");
        Assert.True(draft.AmountVnd <= 0);
    }
}
