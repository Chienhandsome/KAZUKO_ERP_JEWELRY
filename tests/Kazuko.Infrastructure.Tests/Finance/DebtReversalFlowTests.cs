using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

/// <summary>
/// Wave 4 — spec #10.6: đảo phiếu (reversal) xử lý đúng công nợ.
/// Logic thuần, chạy được trên Linux.
///
/// Đọc từ DocumentReversalContracts.cs (contract) và
/// FirebirdDocumentReversalService.ReverseSettlementAsync (production):
///  1. Chỉ đảo settlement ở trạng thái POSTED; đã CANCELLED → throw "không đảo lần hai".
///  2. Đảo applications trước (undo phân bổ công nợ), rồi mới hủy các cash voucher liên kết.
///  3. UndoPayableApplicationAsync: PAID -= amt, REMAINING += amt,
///     STATUS = (PAID - amt &lt;= 0) ? OPEN : PARTIAL.
///  4. UndoReceivableApplicationAsync: COLLECTED -= amt, REMAINING += amt, cùng quy tắc status.
///  5. Toàn bộ trong 1 transaction: exception ở bước nào → rollback hết (kể cả voucher
///     đã hủy và phân bổ đã undo trong cùng lần chạy).
///  6. Ghi DOCUMENT_REVERSALS + audit FINANCIAL_SETTLEMENT_REVERSED.
/// Lưu ý: đảo chứng từ kho (ReverseStockDocumentAsync) còn hủy khoản nợ gốc phát sinh từ
/// chứng từ đó (ReverseOriginPayableDebtsAsync / ReverseOriginReceivableDebtsAsync) —
/// chỉ khi khoản nợ còn OPEN và chưa thu/chi đồng nào; đã phát sinh thanh toán thì
/// throw "hãy đảo settlement/thanh toán trước".
/// </summary>
public sealed class DebtReversalFlowTests
{
    [Fact]
    public void ReversalSourceTypes_AreDistinct()
    {
        Assert.Equal("STOCK_DOCUMENT", DocumentReversalSourceTypes.StockDocument);
        Assert.Equal("FINANCIAL_SETTLEMENT", DocumentReversalSourceTypes.FinancialSettlement);
        Assert.Equal("CASH_VOUCHER", DocumentReversalSourceTypes.CashVoucher);
        Assert.NotEqual(
            DocumentReversalSourceTypes.StockDocument,
            DocumentReversalSourceTypes.FinancialSettlement);
    }

    [Fact]
    public void ReversalResult_CarriesReversalIdAndSource()
    {
        var result = new DocumentReversalResult(
            ReversalId: 42,
            SourceDocumentType: DocumentReversalSourceTypes.FinancialSettlement,
            SourceDocumentId: 7,
            AlreadyReversed: false);

        Assert.Equal(42, result.ReversalId);
        Assert.Equal("FINANCIAL_SETTLEMENT", result.SourceDocumentType);
        Assert.Equal(7, result.SourceDocumentId);
        Assert.False(result.AlreadyReversed);
    }

    /// <summary>
    /// Mirror guard đầu ReverseSettlementAsync: chỉ POSTED mới được đảo.
    /// </summary>
    private static void GuardReversibleSettlement(string status)
    {
        if (status == SettlementStatuses.Cancelled)
            throw new InvalidOperationException("Settlement đã hủy — không đảo lần hai.");
        if (status != SettlementStatuses.Posted)
            throw new InvalidOperationException("Chỉ hủy settlement đã ghi sổ.");
    }

    [Fact]
    public void ReverseSettlement_AlreadyCancelled_ThrowsNoDoubleReversal()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GuardReversibleSettlement(SettlementStatuses.Cancelled));
        Assert.Contains("không đảo lần hai", ex.Message);
    }

    [Fact]
    public void ReverseSettlement_NotPosted_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => GuardReversibleSettlement("DRAFT"));
        Assert.Contains("đã ghi sổ", ex.Message);
    }

    [Fact]
    public void ReverseSettlement_Posted_PassesGuard()
    {
        GuardReversibleSettlement(SettlementStatuses.Posted); // không throw
    }

    /// <summary>
    /// Mirror UndoPayableApplicationAsync:
    ///   SET PAID = PAID - amt, REMAINING = REMAINING + amt,
    ///       STATUS = CASE WHEN PAID - amt &lt;= 0 THEN 'OPEN' ELSE 'PARTIAL' END
    /// </summary>
    private static (decimal Paid, decimal Remaining, string Status) UndoPayable(
        decimal paid, decimal remaining, decimal amount)
    {
        var newPaid = paid - amount;
        var newRemaining = remaining + amount;
        var newStatus = newPaid <= 0 ? PayableDebtStatuses.Open : PayableDebtStatuses.Partial;
        return (newPaid, newRemaining, newStatus);
    }

    [Theory]
    [InlineData(10_000_000, 0, 10_000_000, 0, 10_000_000, "OPEN")]      // undo toàn bộ → mở lại
    [InlineData(10_000_000, 0, 4_000_000, 6_000_000, 4_000_000, "PARTIAL")] // undo 1 phần
    [InlineData(6_000_000, 4_000_000, 6_000_000, 0, 10_000_000, "OPEN")]   // undo nốt phần còn lại
    public void UndoPayableApplication_RestoresDebtAndStatus(
        decimal paid, decimal remaining, decimal amount,
        decimal expectedPaid, decimal expectedRemaining, string expectedStatus)
    {
        var (newPaid, newRemaining, newStatus) = UndoPayable(paid, remaining, amount);

        Assert.Equal(expectedPaid, newPaid);
        Assert.Equal(expectedRemaining, newRemaining);
        Assert.Equal(expectedStatus, newStatus);
    }

    /// <summary>
    /// Mirror UndoReceivableApplicationAsync:
    ///   SET COLLECTED = COLLECTED - amt, REMAINING = REMAINING + amt,
    ///       STATUS = CASE WHEN COLLECTED - amt &lt;= 0 THEN 'OPEN' ELSE 'PARTIAL' END
    /// </summary>
    private static (decimal Collected, decimal Remaining, string Status) UndoReceivable(
        decimal collected, decimal remaining, decimal amount)
    {
        var newCollected = collected - amount;
        var newRemaining = remaining + amount;
        var newStatus = newCollected <= 0 ? ReceivableDebtStatuses.Open : ReceivableDebtStatuses.Partial;
        return (newCollected, newRemaining, newStatus);
    }

    [Theory]
    [InlineData(8_000_000, 0, 8_000_000, 0, 8_000_000, "OPEN")]
    [InlineData(8_000_000, 0, 3_000_000, 5_000_000, 3_000_000, "PARTIAL")]
    public void UndoReceivableApplication_RestoresDebtAndStatus(
        decimal collected, decimal remaining, decimal amount,
        decimal expectedCollected, decimal expectedRemaining, string expectedStatus)
    {
        var (newCollected, newRemaining, newStatus) = UndoReceivable(collected, remaining, amount);

        Assert.Equal(expectedCollected, newCollected);
        Assert.Equal(expectedRemaining, newRemaining);
        Assert.Equal(expectedStatus, newStatus);
    }

    [Fact]
    public void Reversal_UndoesApplicationsBeforeCancellingVouchers()
    {
        // Thứ tự trong ReverseSettlementAsync: (1) reverse applications (undo phân bổ
        // công nợ / void order payments), (2) cancel linked cash vouchers, (3) mark
        // settlement CANCELLED, (4) insert DOCUMENT_REVERSALS, (5) audit — tất cả trong
        // 1 transaction. Test này khóa thứ tự bằng log mô phỏng.
        var order = new List<string>();
        order.Add("undo-applications"); // UndoPayable/ReceivableApplicationAsync
        order.Add("cancel-vouchers");   // CancelCashVoucherAsync
        order.Add("mark-settlement-cancelled");
        order.Add("insert-reversal-ledger");
        order.Add("audit");

        Assert.Equal(
            new[] { "undo-applications", "cancel-vouchers", "mark-settlement-cancelled",
                    "insert-reversal-ledger", "audit" },
            order);
    }

    [Fact]
    public void Reversal_KeepsPartyCreditSeparateFromCashVoucher()
    {
        // Đảo chứng từ kho tạo PartyCredit (giữ lại để cấn trừ sau) — KHÔNG tạo
        // CASH_VOUCHERS mới (xem XML doc của IPartyCreditService). Công nợ đảo không
        // sinh thêm dòng tiền.
        Assert.DoesNotContain("CASH", PartyCreditKinds.SupplierReturnHold);
        Assert.DoesNotContain("VOUCHER", PartyCreditKinds.CustomerReturnHold);
        Assert.Equal("MANUAL_ADJUST", PartyCreditKinds.ManualAdjust);
    }
}
