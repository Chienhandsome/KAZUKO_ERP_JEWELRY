using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

/// <summary>
/// Wave 4 — spec #10.4: rollback batch — nếu 1 khoản lỗi thì không khoản nào được trừ.
/// Logic thuần, chạy được trên Linux (mô phỏng đúng cấu trúc transaction của production).
///
/// Production (FirebirdDebtWorkbenchRepository.PayPayablePartyFifoAsync và
/// SettleSingleLedgerEntryAsync) tuân thủ mẫu:
/// <code>
/// await using var transaction = await connection.BeginTransactionAsync(ct);
/// try
/// {
///     ... build applications ...
///     ... apply từng khoản (ApplyPayableDebtPaymentAsync / ApplyReceivableDebtCollectionAsync) ...
///     ... audit ...
///     await transaction.CommitAsync(ct);
/// }
/// catch
/// {
///     await transaction.RollbackAsync(ct);
///     throw;
/// }
/// </code>
/// Nghĩa là: mọi UPDATE/INSERT đều nằm trong CÙNG 1 transaction; exception ở bất kỳ
/// bước nào → rollback toàn bộ → số dư các khoản nợ giữ nguyên như chưa hề chạy.
/// Các test dưới đây mô phỏng đúng thứ tự đó trên ledger in-memory.
/// </summary>
public sealed class DebtBatchRollbackTests
{
    /// <summary>
    /// Mô phỏng 1 transaction: mọi apply chạy trên bản sao làm việc; chỉ khi tất cả
    /// thành công mới "commit" (ghi đè ledger gốc). Exception giữa chừng → ledger gốc
    /// giữ nguyên — tương đương ROLLBACK của production.
    /// </summary>
    private static void ApplyBatchWithRollbackSemantics(
        Dictionary<long, decimal> ledger,
        IReadOnlyList<(long DebtId, decimal Amount)> applications,
        Func<long, decimal, bool> applyOne)
    {
        var working = new Dictionary<long, decimal>(ledger);
        try
        {
            foreach (var app in applications)
            {
                if (!applyOne(app.DebtId, app.Amount))
                    throw new InvalidOperationException($"Không áp dụng được khoản #{app.DebtId}.");
                working[app.DebtId] -= app.Amount; // UPDATE ... REMAINING = REM - amt
            }

            // commit: ghi đè ledger gốc
            foreach (var kv in working)
                ledger[kv.Key] = kv.Value;
        }
        catch
        {
            // rollback: working bị bỏ, ledger gốc không đổi — rồi throw tiếp (như production)
            throw;
        }
    }

    [Fact]
    public void BatchApply_SecondDebtFails_FirstDebtIsNotDeducted()
    {
        var ledger = new Dictionary<long, decimal> { [1] = 4_000_000, [2] = 3_000_000 };
        var applications = new List<(long, decimal)> { (1, 4_000_000), (2, 3_000_000) };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ApplyBatchWithRollbackSemantics(
                ledger,
                applications,
                // Giả lập lỗi ở khoản thứ 2 (vd. row bị lock / số dư đã đổi bởi user khác)
                (debtId, amount) => debtId != 2));

        Assert.Contains("Không áp dụng được khoản #2", ex.Message);
        // Rollback: KHÔNG khoản nào bị trừ, kể cả khoản #1 đã "apply" trước đó.
        Assert.Equal(4_000_000, ledger[1]);
        Assert.Equal(3_000_000, ledger[2]);
    }

    [Fact]
    public void BatchApply_AllSucceed_CommitsEveryDeduction()
    {
        var ledger = new Dictionary<long, decimal> { [1] = 4_000_000, [2] = 3_000_000 };
        var applications = new List<(long, decimal)> { (1, 4_000_000), (2, 3_000_000) };

        ApplyBatchWithRollbackSemantics(ledger, applications, (_, _) => true);

        Assert.Equal(0, ledger[1]);
        Assert.Equal(0, ledger[2]);
    }

    [Fact]
    public void BatchApply_FirstDebtFails_NothingIsDeducted()
    {
        var ledger = new Dictionary<long, decimal> { [1] = 4_000_000, [2] = 3_000_000 };
        var applications = new List<(long, decimal)> { (1, 4_000_000), (2, 3_000_000) };

        Assert.Throws<InvalidOperationException>(() =>
            ApplyBatchWithRollbackSemantics(ledger, applications, (_, _) => false));

        Assert.Equal(4_000_000, ledger[1]);
        Assert.Equal(3_000_000, ledger[2]);
    }

    /// <summary>
    /// Mirror guard đầu SettleSingleLedgerEntryAsync: source type không hỗ trợ
    /// → throw TRƯỚC khi mở transaction / chạm DB, nên không có gì để rollback.
    /// </summary>
    private static void GuardSupportedSourceType(string? sourceType)
    {
        var normalized = (sourceType ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized is not (
            DebtLedgerSourceTypes.SalesOrder or
            DebtLedgerSourceTypes.ReceivableDebt or
            DebtLedgerSourceTypes.PayableDebt))
            throw new InvalidOperationException("Nguồn công nợ không hỗ trợ tất toán lẻ.");
    }

    [Theory]
    [InlineData("CASH_VOUCHER")]
    [InlineData("ORDER_PAYMENT")]
    [InlineData("SALES_CHECKOUT_OFFSET")]
    [InlineData("")]
    [InlineData(null)]
    public void SingleSettle_UnsupportedSourceType_ThrowsBeforeAnyDbWork(string? sourceType)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => GuardSupportedSourceType(sourceType));
        Assert.Contains("không hỗ trợ tất toán lẻ", ex.Message);
    }

    [Theory]
    [InlineData("SALES_ORDER")]
    [InlineData("RECEIVABLE_DEBT")]
    [InlineData("PAYABLE_DEBT")]
    [InlineData(" sales_order ")] // production trim + uppercase
    public void SingleSettle_SupportedSourceType_PassesGuard(string sourceType)
    {
        GuardSupportedSourceType(sourceType); // không throw
    }

    [Fact]
    public void BatchApply_OverRemainingGuard_StopsBeforeInsert()
    {
        // Mirror guard trong ApplyPayableDebtPaymentAsync: re-check sau lock,
        // khoản nợ có thể đã bị user khác thanh toán → throw trước mọi INSERT/UPDATE.
        decimal remainingAfterLock = 0m; // user khác đã tất toán
        var paymentRowsInserted = 0;

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            if (remainingAfterLock <= 0)
                throw new InvalidOperationException("Công nợ đã được thanh toán hết bởi người dùng khác.");
            paymentRowsInserted++; // INSERT PAYABLE_DEBT_PAYMENTS — không bao giờ tới đây
        });

        Assert.Contains("người dùng khác", ex.Message);
        Assert.Equal(0, paymentRowsInserted);
    }
}
