using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record FundCashPaymentsReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record FundCashPaymentsReportRow(
    long Id,
    string Code,
    DateTime VoucherDate,
    string PartyName,
    decimal AmountVnd,
    string Notes,
    string CreatedByName,
    string FundName);

public sealed record FundCashPaymentsReportResult(
    IReadOnlyList<FundCashPaymentsReportRow> Rows,
    int ItemCount,
    decimal TotalAmount);

public interface IFundCashPaymentsReportService
{
    Task<FundCashPaymentsReportResult> GetAsync(
        FundCashPaymentsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
