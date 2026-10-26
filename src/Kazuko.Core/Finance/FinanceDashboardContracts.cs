using Kazuko.Core.Authentication;

namespace Kazuko.Core.Finance;

public sealed record FinanceKpi(
    decimal TotalIncomeMonth, decimal IncomeGrowthPct,
    decimal TotalExpenseMonth, decimal ExpenseGrowthPct,
    decimal CashFundBalance, decimal BankBalanceTotal);

public sealed record MonthlyCashFlow(string MonthLabel, decimal Income, decimal Expense);

public sealed record ExpenseBreakdown(string Category, decimal Amount, decimal Percent);

public sealed record FundBalance(string FundName, decimal Balance, string Kind); // Kind: CASH/BANK

public sealed record CashVoucherListItem(
    long Id, string Code, DateTime VoucherDate, string VoucherType, // IN/OUT
    string Content, string PartyName, string PaymentMethod,
    decimal AmountVnd, string CreatedByName, string StatusCode, long? CategoryId = null, string? CategoryName = null);

public sealed record CashVoucherDetail(
    long Id, string Code, DateTime VoucherDate, string VoucherType,
    string PaymentMethod, decimal AmountVnd,
    string? PartyName, string? Notes, string? ReferenceNumber,
    string? SourceDocumentType, long? SourceDocumentId,
    string CreatedByName, string StatusCode, long? CategoryId = null, string? CategoryName = null);

public sealed record ManualVoucherRequest(
    string VoucherType, // IN/OUT
    DateTime VoucherDate,
    string PaymentMethod, // CASH/BANK_TRANSFER/CARD
    decimal AmountVnd,
    string? PartyName,
    string? Notes,
    string? ReferenceNumber,
    long? CategoryId = null);

public interface IFinanceDashboardRepository
{
    Task<FinanceKpi> GetKpiAsync(AuthenticatedSession session, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyCashFlow>> GetMonthlyCashFlowAsync(int months, AuthenticatedSession session, CancellationToken ct = default);
    Task<IReadOnlyList<ExpenseBreakdown>> GetExpenseBreakdownAsync(AuthenticatedSession session, CancellationToken ct = default);
    Task<IReadOnlyList<FundBalance>> GetFundBalancesAsync(AuthenticatedSession session, CancellationToken ct = default);
    Task<IReadOnlyList<CashVoucherListItem>> SearchVouchersAsync(string? voucherType, DateTime? from, DateTime? to, string? keyword, AuthenticatedSession session, CancellationToken ct = default);
    Task<CashVoucherDetail?> GetVoucherDetailAsync(long id, AuthenticatedSession session, CancellationToken ct = default);
}

public interface IFinanceVoucherService
{
    // Tạo phiếu thu/chi thủ công (dùng chung cho dialog và module khác gọi)
    Task<CashVoucherDetail> CreateManualVoucherAsync(ManualVoucherRequest request, AuthenticatedSession session, CancellationToken ct = default);
    Task CancelVoucherAsync(long id, string reason, AuthenticatedSession session, CancellationToken ct = default);
}

/// <summary>
/// Quy ước lưu/tách "đối tượng thu/chi" trong cột CASH_VOUCHERS.NOTES (spec v1:
/// không thêm cột mới). Phiếu tạo tay ghi đối tượng ở đầu NOTES dưới dạng marker
/// "[Đối tượng: Tên]"; khi đọc, marker được tách ra thành PartyName, phần còn
/// lại là Content/Notes. Phiếu do module khác tự động sinh không có marker thì
/// PartyName = null và Content = toàn bộ NOTES.
/// </summary>
public static class CashVoucherNoteFormat
{
    public const string PartyMarkerPrefix = "[Đối tượng: ";
    private const char PartyMarkerSuffix = ']';

    public static string Format(string? partyName, string? notes)
    {
        var party = (partyName ?? string.Empty).Trim();
        var body = (notes ?? string.Empty).Trim();
        if (party.Length == 0)
            return body;
        var marker = $"{PartyMarkerPrefix}{party}{PartyMarkerSuffix}";
        return body.Length == 0 ? marker : $"{marker} {body}";
    }

    public static (string? PartyName, string Content) Parse(string? notes)
    {
        var text = notes ?? string.Empty;
        if (!text.StartsWith(PartyMarkerPrefix, StringComparison.Ordinal))
            return (null, text);
        var end = text.IndexOf(PartyMarkerSuffix, PartyMarkerPrefix.Length);
        if (end < 0)
            return (null, text);
        var party = text.Substring(PartyMarkerPrefix.Length, end - PartyMarkerPrefix.Length).Trim();
        var content = text.Substring(end + 1).Trim();
        return (party.Length == 0 ? null : party, content);
    }
}
