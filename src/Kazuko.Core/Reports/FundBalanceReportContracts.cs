using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record FundBalanceReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record FundBalanceReportRow(
    string FundKind, // CASH / BANK
    long FundId,
    string FundName,
    decimal OpeningBalance,
    decimal TotalIn,
    decimal TotalOut,
    decimal ClosingBalance);

public sealed record FundBalanceReportResult(
    IReadOnlyList<FundBalanceReportRow> Rows,
    decimal TotalOpening,
    decimal TotalIn,
    decimal TotalOut,
    decimal TotalClosing);

public interface IFundBalanceReportService
{
    Task<FundBalanceReportResult> GetAsync(
        FundBalanceReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
