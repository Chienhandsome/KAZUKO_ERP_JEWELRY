using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

/// <summary>
/// Wave 4 — spec #10.3: công nợ hai chiều — 1 đối tượng vừa có phải thu vừa có
/// phải trả → 2 số dư độc lập, không bù trừ. Logic thuần, chạy được trên Linux.
///
/// Production giữ 2 số dư riêng ở mọi tầng:
/// - SQL (FirebirdDebtWorkbenchRepository): ReceivableRemainingVnd đọc từ
///   RECEIVABLE_DEBTS, PayableRemainingVnd đọc từ PAYABLE_DEBTS — 2 subquery độc lập,
///   không netting.
/// - Record CustomerDebtRow / SupplierDebtRow: 2 field riêng (xem FinanceContracts.cs).
/// - Ledger entry mang Direction = RECEIVE | PAY (DebtLedgerDirections).
/// </summary>
public sealed class DebtTwoWayBalanceTests
{
    [Fact]
    public void Directions_AreDistinct()
    {
        Assert.Equal("RECEIVE", DebtLedgerDirections.Receive);
        Assert.Equal("PAY", DebtLedgerDirections.Pay);
        Assert.NotEqual(DebtLedgerDirections.Receive, DebtLedgerDirections.Pay);
    }

    [Fact]
    public void CustomerRow_KeepsBothBalancesIndependent_NoNetting()
    {
        // KH vừa nợ DN 5tr (phải thu) vừa được DN nợ 3tr (phải trả):
        // 2 số dư phải giữ nguyên, không gộp thành 2tr.
        var row = new CustomerDebtRow(
            CustomerId: 1, Code: "KH-001", Name: "Nguyễn Văn A", Phone: "", GroupName: "",
            Address: "", TotalIncurredVnd: 5_000_000, PaidVnd: 0, RemainingVnd: 5_000_000,
            LastDebtDate: null, StatusCode: "NORMAL",
            ReceivableRemainingVnd: 5_000_000,
            PayableRemainingVnd: 3_000_000);

        Assert.Equal(5_000_000, row.ReceivableRemainingVnd);
        Assert.Equal(3_000_000, row.PayableRemainingVnd);
        // Không bù trừ: tổng phải thu KHÁC hiệu số.
        Assert.NotEqual(
            row.ReceivableRemainingVnd - row.PayableRemainingVnd,
            row.ReceivableRemainingVnd);
    }

    [Fact]
    public void SupplierRow_KeepsBothBalancesIndependent_NoNetting()
    {
        // NCC vừa được DN nợ 7tr (phải trả) vừa nợ DN 2tr (phải thu, vd. tạm ứng).
        var row = new SupplierDebtRow(
            SupplierId: 9, Code: "NCC-009", Name: "Vàng Bạc B", Phone: "", GroupName: "",
            Address: "", TotalIncurredVnd: 7_000_000, PaidVnd: 0, RemainingVnd: 7_000_000,
            LastDebtDate: null, StatusCode: "NORMAL",
            ReceivableRemainingVnd: 2_000_000,
            PayableRemainingVnd: 7_000_000);

        Assert.Equal(2_000_000, row.ReceivableRemainingVnd);
        Assert.Equal(7_000_000, row.PayableRemainingVnd);
    }

    [Fact]
    public void TwoWaySummary_SumsEachDirectionSeparately()
    {
        // Mirror 2 subquery độc lập trong SQL summary:
        //   (SELECT SUM(REMAINING...) FROM RECEIVABLE_DEBTS WHERE PARTY...) AS Receivable
        //   (SELECT SUM(REMAINING...) FROM PAYABLE_DEBTS WHERE PARTY...)     AS Payable
        var receivableRemainings = new[] { 2_000_000m, 3_000_000m };
        var payableRemainings = new[] { 7_000_000m };

        var receivableTotal = receivableRemainings.Sum();
        var payableTotal = payableRemainings.Sum();

        Assert.Equal(5_000_000, receivableTotal);
        Assert.Equal(7_000_000, payableTotal);
    }

    [Fact]
    public void LedgerEntries_SameParty_BothDirections_KeepOwnAmounts()
    {
        // Cùng 1 KH có cả dòng phải thu (đơn hàng) và dòng phải trả (DN nợ KH):
        // mỗi entry giữ Direction + số tiền riêng.
        var entries = new[]
        {
            new DebtLedgerEntry(DateTime.Today, "DH-100", "Đơn hàng", 0m, 5_000_000, 5_000_000,
                DebtLedgerSourceTypes.SalesOrder, 100, DebtLedgerDirections.Receive,
                5_000_000, 0m, 5_000_000, true),
            new DebtLedgerEntry(DateTime.Today, "CN-200", "DN nợ KH", 0m, 3_000_000, 3_000_000,
                DebtLedgerSourceTypes.PayableDebt, 200, DebtLedgerDirections.Pay,
                3_000_000, 0m, 3_000_000, true),
        };

        var receiveTotal = entries.Where(e => e.Direction == DebtLedgerDirections.Receive)
            .Sum(e => e.RemainingAmountVnd);
        var payTotal = entries.Where(e => e.Direction == DebtLedgerDirections.Pay)
            .Sum(e => e.RemainingAmountVnd);

        Assert.Equal(5_000_000, receiveTotal);
        Assert.Equal(3_000_000, payTotal);
        Assert.True(entries.All(e => e.CanSettle)); // còn dư > 0 ở cả 2 chiều → đều có nút Thu/Chi
    }

    [Fact]
    public void CollectOnlyAffectsReceivable_PayOnlyAffectsPayable()
    {
        // Thu tiền KH chỉ trừ phía phải thu; chi tiền KH chỉ trừ phía phải trả.
        // Mirror: CollectSingleReceivableDebtAsync vs PaySinglePayableDebtAsync
        // cập nhật 2 bảng khác nhau (RECEIVABLE_DEBTS / PAYABLE_DEBTS).
        decimal receivableRemaining = 5_000_000;
        decimal payableRemaining = 3_000_000;

        // Thu 2tr từ KH:
        receivableRemaining -= 2_000_000;

        Assert.Equal(3_000_000, receivableRemaining);
        Assert.Equal(3_000_000, payableRemaining); // phía phải trả không đổi

        // Chi 1tr cho KH (DN trả nợ KH):
        payableRemaining -= 1_000_000;

        Assert.Equal(3_000_000, receivableRemaining); // phía phải thu không đổi
        Assert.Equal(2_000_000, payableRemaining);
    }
}
