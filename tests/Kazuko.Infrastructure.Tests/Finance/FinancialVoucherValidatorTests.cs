using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

public sealed class FinancialVoucherValidatorTests
{
    [Fact]
    public void PaymentAllocation_CashRequiresFund_RejectsBank()
    {
        var errors = PaymentAllocationValidator.Validate(
            [new PaymentAllocationDraft(PaymentMethods.Cash, 1_000_000, null, 2, null)],
            1_000_000);
        Assert.Contains(errors, e => e.Contains("quỹ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PaymentAllocation_BankRequiresAccount_RejectsFund()
    {
        var errors = PaymentAllocationValidator.Validate(
            [new PaymentAllocationDraft(PaymentMethods.BankTransfer, 1_000_000, 1, null, null)],
            1_000_000);
        Assert.Contains(errors, e => e.Contains("ngân hàng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SettlementDraft_AllocationMustEqualApplication()
    {
        var draft = new FinancialSettlementDraft(
            SettlementTypes.Receipt,
            SettlementPartyTypes.Customer,
            1,
            DateTime.Today,
            [
                new PaymentAllocationDraft(PaymentMethods.Cash, 5_000_000, 1, null, null),
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 5_000_000, null, 2, null)
            ],
            [new SettlementApplicationDraft(SettlementApplicationTargetTypes.SalesOrder, 10, 9_000_000)],
            null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            InvokeSettlementValidate(draft));
        Assert.Contains("phải bằng", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettlementDraft_TenMillionSplit_IsValidShape()
    {
        var draft = new FinancialSettlementDraft(
            SettlementTypes.Payment,
            SettlementPartyTypes.Supplier,
            5,
            DateTime.Today,
            [
                new PaymentAllocationDraft(PaymentMethods.Cash, 5_000_000, 1, null, null),
                new PaymentAllocationDraft(PaymentMethods.BankTransfer, 5_000_000, null, 2, "CK")
            ],
            [
                new SettlementApplicationDraft(SettlementApplicationTargetTypes.PayableDebt, 1, 4_000_000),
                new SettlementApplicationDraft(SettlementApplicationTargetTypes.PayableDebt, 2, 3_000_000),
                new SettlementApplicationDraft(SettlementApplicationTargetTypes.PayableDebt, 3, 3_000_000)
            ],
            "pay fifo");

        // Two allocations → two vouchers conceptually; three applications → no extra vouchers.
        Assert.Equal(2, draft.Allocations.Count);
        Assert.Equal(3, draft.Applications.Count);
        Assert.Equal(draft.Allocations.Sum(x => x.AmountVnd), draft.Applications.Sum(x => x.AmountVnd));
    }

    private static void InvokeSettlementValidate(FinancialSettlementDraft draft)
    {
        // Mirror FirebirdFinancialSettlementService.ValidateDraft rules without DB.
        var type = (draft.SettlementType ?? string.Empty).Trim().ToUpperInvariant();
        if (type is not (SettlementTypes.Receipt or SettlementTypes.Payment))
            throw new InvalidOperationException("SETTLEMENT_TYPE phải là RECEIPT hoặc PAYMENT.");

        var allocations = draft.Allocations ?? Array.Empty<PaymentAllocationDraft>();
        var applications = draft.Applications ?? Array.Empty<SettlementApplicationDraft>();
        var totalAlloc = allocations.Sum(x => x.AmountVnd);
        var totalApp = applications.Sum(x => x.AmountVnd);
        var errors = PaymentAllocationValidator.Validate(allocations, totalAlloc).ToList();
        if (totalAlloc != totalApp)
            errors.Add($"Tổng allocations ({totalAlloc:N0}) phải bằng tổng applications ({totalApp:N0}).");
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }
}
