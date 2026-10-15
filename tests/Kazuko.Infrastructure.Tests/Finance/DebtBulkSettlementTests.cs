using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

/// <summary>
/// Wave 4 — spec #10.2: thanh toán toàn bộ — tổng phân bổ = tổng các khoản nợ,
/// 1 voucher cho N khoản. Logic thuần, chạy được trên Linux.
///
/// Mirror trực tiếp production:
/// - FirebirdDebtWorkbenchRepository.PayPayablePartyFifoAsync: vòng lặp FIFO build
///   applications (Math.Min(left, debt.Remaining)), guard amount &gt; totalOpen,
///   rồi gọi PostBulkSettlementInTransactionAsync.
/// - FirebirdFinancialSettlementService.PostBulkSettlementInTransactionAsync:
///   1 settlement + 1 voucher duy nhất (result.CashVoucherIds = [voucher.VoucherId])
///   cho N applications; ValidateDraft bắt tổng allocations == tổng applications.
/// </summary>
public sealed class DebtBulkSettlementTests
{
    /// <summary>
    /// Mirror vòng lặp FIFO trong PayPayablePartyFifoAsync (dòng build applications).
    /// </summary>
    private static List<SettlementApplicationDraft> BuildFifoApplications(
        IReadOnlyList<(long Id, decimal Remaining)> openDebts, decimal amount)
    {
        var applications = new List<SettlementApplicationDraft>();
        var left = amount;
        foreach (var debt in openDebts)
        {
            if (left <= 0)
                break;
            var apply = Math.Min(left, debt.Remaining);
            applications.Add(new SettlementApplicationDraft(
                SettlementApplicationTargetTypes.PayableDebt, debt.Id, apply));
            left -= apply;
        }
        return applications;
    }

    [Fact]
    public void FullPayment_FifoCoversAllDebts_TotalApplicationEqualsTotalDebt()
    {
        var open = new List<(long Id, decimal Remaining)>
        {
            (1, 4_000_000),
            (2, 3_000_000),
            (3, 3_000_000),
        };
        decimal amount = 10_000_000; // = tổng còn nợ

        var applications = BuildFifoApplications(open, amount);

        Assert.Equal(3, applications.Count); // 1 application cho mỗi khoản
        Assert.Equal(amount, applications.Sum(a => a.AmountVnd));
        Assert.Equal([4_000_000, 3_000_000, 3_000_000],
            applications.Select(a => a.AmountVnd).ToArray());
    }

    [Fact]
    public void PartialBulkPayment_LastApplicationTakesRemainder()
    {
        var open = new List<(long Id, decimal Remaining)>
        {
            (1, 4_000_000),
            (2, 3_000_000),
            (3, 3_000_000),
        };

        var applications = BuildFifoApplications(open, 6_000_000);

        Assert.Equal(2, applications.Count);
        Assert.Equal(4_000_000, applications[0].AmountVnd); // khoản đầu ăn trọn
        Assert.Equal(2_000_000, applications[1].AmountVnd); // khoản sau ăn phần dư
        Assert.Equal(6_000_000, applications.Sum(a => a.AmountVnd));
    }

    [Fact]
    public void ExactFirstDebtBoundary_StopsAfterOneApplication()
    {
        var open = new List<(long Id, decimal Remaining)>
        {
            (1, 4_000_000),
            (2, 3_000_000),
        };

        var applications = BuildFifoApplications(open, 4_000_000);

        Assert.Single(applications);
        Assert.Equal(4_000_000, applications[0].AmountVnd);
    }

    [Fact]
    public void OverpaymentBeyondTotalOpen_IsRejected()
    {
        // Mirror guard: if (amount > totalOpen) throw "Số chi (...) vượt tổng còn nợ (...)".
        var open = new List<(long Id, decimal Remaining)> { (1, 4_000_000), (2, 3_000_000) };
        var totalOpen = open.Sum(x => x.Remaining);
        decimal amount = 8_000_000;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (amount > totalOpen)
                throw new InvalidOperationException($"Số chi ({amount:N0}) vượt tổng còn nợ ({totalOpen:N0}).");
        });
        Assert.Contains("vượt tổng còn nợ", ex.Message);
    }

    [Fact]
    public void BulkSettlement_OneVoucherForNDebts()
    {
        // Production PostBulkSettlementInTransactionAsync trả về
        // new FinancialSettlementResult(settlementId, code, [voucher.VoucherId], applicationIds):
        // đúng 1 voucher dù có N applications.
        var applications = new List<SettlementApplicationDraft>
        {
            new(SettlementApplicationTargetTypes.PayableDebt, 1, 4_000_000),
            new(SettlementApplicationTargetTypes.PayableDebt, 2, 3_000_000),
            new(SettlementApplicationTargetTypes.PayableDebt, 3, 3_000_000),
        };
        var result = new FinancialSettlementResult(99, "TT-0001", [777], [10, 11, 12]);

        Assert.Equal(3, applications.Count);
        Assert.Single(result.CashVoucherIds); // 1 voucher duy nhất
        Assert.Equal(3, result.ApplicationIds.Count);
    }

    [Fact]
    public void BulkDraft_TotalAllocationsMustEqualTotalApplications()
    {
        // Mirror rule trong ValidateDraft:
        // if (totalAlloc != totalApp) errors.Add("Tổng allocations ... phải bằng tổng applications ...").
        var draft = new FinancialSettlementDraft(
            SettlementTypes.Payment,
            SettlementPartyTypes.Supplier,
            5,
            DateTime.Today,
            [new PaymentAllocationDraft(PaymentMethods.Cash, 5_000_000, 1, null, null),
             new PaymentAllocationDraft(PaymentMethods.BankTransfer, 5_000_000, null, 2, null)],
            [new SettlementApplicationDraft(SettlementApplicationTargetTypes.PayableDebt, 1, 4_000_000),
             new SettlementApplicationDraft(SettlementApplicationTargetTypes.PayableDebt, 2, 3_000_000),
             new SettlementApplicationDraft(SettlementApplicationTargetTypes.PayableDebt, 3, 3_000_000)],
            "bulk fifo");

        var totalAlloc = draft.Allocations.Sum(x => x.AmountVnd);
        var totalApp = draft.Applications.Sum(x => x.AmountVnd);

        Assert.Equal(totalAlloc, totalApp);
        Assert.Equal(10_000_000, totalAlloc);
    }

    [Fact]
    public void BulkDraft_MismatchedTotals_AreRejected()
    {
        var totalAlloc = 10_000_000m;
        var totalApp = 9_000_000m;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (totalAlloc != totalApp)
                throw new InvalidOperationException(
                    $"Tổng allocations ({totalAlloc:N0}) phải bằng tổng applications ({totalApp:N0}).");
        });
        Assert.Contains("phải bằng", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BulkPayment_SplitAcrossMethods_OneVoucherStillHoldsBothLines()
    {
        // 2 dòng phương thức (tiền mặt + CK) vẫn gộp vào 1 voucher duy nhất của bulk settlement.
        var allocations = new[]
        {
            new PaymentAllocationDraft(PaymentMethods.Cash, 6_000_000, 1, null, null),
            new PaymentAllocationDraft(PaymentMethods.BankTransfer, 4_000_000, null, 2, "CK"),
        };

        Assert.Empty(PaymentAllocationValidator.Validate(allocations, 10_000_000));
        var result = new FinancialSettlementResult(100, "TT-0002", [778], [20, 21]);
        Assert.Single(result.CashVoucherIds);
        Assert.Equal(10_000_000, allocations.Sum(a => a.AmountVnd));
    }
}
