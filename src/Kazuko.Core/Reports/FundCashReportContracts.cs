using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record FundCashReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record FundCashReportRow(
    DateTime Day,
    decimal TotalIn,
    decimal TotalOut,
    decimal ClosingBalance);

public sealed record FundCashReportResult(
    IReadOnlyList<FundCashReportRow> Rows,
    decimal OpeningBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal ClosingBalance);

public interface IFundCashReportService
{
    Task<FundCashReportResult> GetAsync(
        FundCashReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
