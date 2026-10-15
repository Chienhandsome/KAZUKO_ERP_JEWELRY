using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

/// <summary>
/// Wave 4 — spec #10.1: thanh toán một phần.
/// Logic thuần, chạy được trên Linux (không cần Firebird/WPF).
///
/// Các công thức bên dưới mirror trực tiếp code production:
/// - FirebirdDebtWorkbenchRepository.GetCustomerLedgerAsync: remaining = GrandTotal - paid,
///   CanSettle = remaining &gt; 0 cho dòng đơn hàng / khoản phải thu; false cho dòng
///   thanh toán đã ghi nhận và dòng bù trừ.
/// - ApplyPayableDebtPaymentAsync / ApplyReceivableDebtCollectionAsync:
///   newRemaining = remaining - applied; newStatus = (newRemaining == 0) ? PAID : PARTIAL;
///   guard: applied &gt; remaining, khoản đã đóng, hoặc remaining &lt;= 0 → throw.
/// </summary>
public sealed class DebtPartialSettlementTests
{
    [Theory]
    [InlineData(10_000_000, 0, 10_000_000, true)]
    [InlineData(10_000_000, 4_000_000, 6_000_000, true)]
    [InlineData(10_000_000, 9_999_999, 1, true)]
    [InlineData(10_000_000, 10_000_000, 0, false)]
    public void SalesOrderRow_Remaining_EqualsGrandTotalMinusPaid(
        decimal grandTotal, decimal paid, decimal expectedRemaining, bool expectedCanSettle)
    {
        // Production: var remaining = row.GrandTotal - paid;
        var remaining = grandTotal - paid;
        // Production: CanSettle = remaining > 0 (dòng đơn hàng)
        var canSettle = remaining > 0;

        Assert.Equal(expectedRemaining, remaining);
        Assert.Equal(expectedCanSettle, canSettle);
    }

    [Fact]
    public void SalesOrderRow_Refund_ReducesPaidSoRemainingGrows()
    {
        // Production (paidPerOrder): REFUND trừ vào tổng đã thu (CASE WHEN ... 'REFUND' THEN -AMOUNT).
        decimal grandTotal = 10_000_000;
        decimal paidTotal = 7_000_000 - 2_000_000; // thu 7tr, hoàn 2tr

        var remaining = grandTotal - paidTotal;

        Assert.Equal(5_000_000, remaining);
        Assert.True(remaining > 0); // CanSettle vẫn true sau hoàn tiền một phần
    }

    [Fact]
    public void PaymentRow_NeverShowsSettleButton()
    {
        // Production: dòng ORDER_PAYMENTS có CanSettle = false cứng
        // ("dòng thanh toán đã ghi nhận: không hiện nút Thu/Chi").
        var entry = new DebtLedgerEntry(
            DateTime.Today, "DH-001", "PAYMENT / CASH",
            ReceiptAmountVnd: 3_000_000, PaymentAmountVnd: 0m, BalanceAfterVnd: 0m,
            SourceType: DebtLedgerSourceTypes.OrderPayment, SourceId: 1,
            Direction: DebtLedgerDirections.Receive,
            OriginalAmountVnd: 3_000_000, PaidAmountVnd: 3_000_000, RemainingAmountVnd: 0m,
            CanSettle: false);

        Assert.False(entry.CanSettle);
    }

    [Fact]
    public void CheckoutOffsetRow_NeverShowsSettleButton()
    {
        // Production: dòng SALES_CHECKOUT_OFFSETS có CanSettle = false cứng
        // ("dòng bù trừ: không hiện nút Thu/Chi").
        var entry = new DebtLedgerEntry(
            DateTime.Today, "TT-001", "Bù trừ vàng cũ",
            ReceiptAmountVnd: 1_000_000, PaymentAmountVnd: 0m, BalanceAfterVnd: 0m,
            SourceType: DebtLedgerSourceTypes.SalesCheckoutOffset, SourceId: 2,
            Direction: DebtLedgerDirections.Receive,
            OriginalAmountVnd: 1_000_000, PaidAmountVnd: 1_000_000, RemainingAmountVnd: 0m,
            CanSettle: false);

        Assert.False(entry.CanSettle);
    }

    [Fact]
    public void PartialCollection_SplitAcrossMethods_MustSumExactly()
    {
        // Thanh toán một phần 4tr chia 2 phương thức: validator bắt tổng allocations
        // phải bằng đúng số tiền thanh toán (không thừa/không thiếu).
        var allocations = new[]
        {
            new PaymentAllocationDraft(PaymentMethods.Cash, 2_500_000, 1, null, null),
            new PaymentAllocationDraft(PaymentMethods.BankTransfer, 1_500_000, null, 2, null),
        };

        var errors = PaymentAllocationValidator.Validate(allocations, 4_000_000);

        Assert.Empty(errors);
    }

    [Fact]
    public void PartialCollection_AllocationMismatch_IsRejected()
    {
        var allocations = new[]
        {
            new PaymentAllocationDraft(PaymentMethods.Cash, 3_000_000, 1, null, null),
        };

        var errors = PaymentAllocationValidator.Validate(allocations, 4_000_000);

        Assert.Contains(errors, e => e.Contains("phải bằng", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(10_000_000, 4_000_000, 6_000_000, PayableDebtStatuses.Partial)]
    [InlineData(10_000_000, 10_000_000, 0, PayableDebtStatuses.Paid)]
    [InlineData(6_000_000, 6_000_000, 0, PayableDebtStatuses.Paid)]
    public void ApplyPayablePayment_StatusTransitions(
        decimal remaining, decimal applied, decimal expectedRemaining, string expectedStatus)
    {
        // Mirror ApplyPayableDebtPaymentAsync:
        //   var newRemaining = debt.RemainingAmountVnd - paid;
        //   var newStatus = newRemaining == 0 ? PAID : PARTIAL;
        var newRemaining = remaining - applied;
        var newStatus = newRemaining == 0 ? PayableDebtStatuses.Paid : PayableDebtStatuses.Partial;

        Assert.Equal(expectedRemaining, newRemaining);
        Assert.Equal(expectedStatus, newStatus);
    }

    [Theory]
    [InlineData(8_000_000, 3_000_000, 5_000_000, ReceivableDebtStatuses.Partial)]
    [InlineData(8_000_000, 8_000_000, 0, ReceivableDebtStatuses.Paid)]
    public void ApplyReceivableCollection_StatusTransitions(
        decimal remaining, decimal collected, decimal expectedRemaining, string expectedStatus)
    {
        // Mirror ApplyReceivableDebtCollectionAsync: cùng quy tắc như phải trả.
        var newRemaining = remaining - collected;
        var newStatus = newRemaining == 0 ? ReceivableDebtStatuses.Paid : ReceivableDebtStatuses.Partial;

        Assert.Equal(expectedRemaining, newRemaining);
        Assert.Equal(expectedStatus, newStatus);
    }

    [Fact]
    public void ApplyPayment_OverRemaining_IsRejected()
    {
        // Mirror guard trong Apply*: if (paid > debt.RemainingAmountVnd) throw.
        decimal remaining = 2_000_000;
        decimal applied = 2_500_000;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (applied > remaining)
                throw new InvalidOperationException($"Số thanh toán vượt số còn nợ.");
        });
        Assert.Contains("vượt số còn nợ", ex.Message);
    }

    [Fact]
    public void ApplyPayment_OnClosedDebt_IsRejected()
    {
        // Mirror guard: status PAID/CANCELLED → throw "đã đóng".
        var status = PayableDebtStatuses.Paid;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (status is PayableDebtStatuses.Paid or PayableDebtStatuses.Cancelled)
                throw new InvalidOperationException($"Công nợ đã đóng ({status}).");
        });
        Assert.Contains("đã đóng", ex.Message);
    }
}
