using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record FundCashReceiptsReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record FundCashReceiptsReportRow(
    long Id,
    string Code,
    DateTime VoucherDate,
    string PartyName,
    decimal AmountVnd,
    string Notes,
    string CreatedByName,
    string FundName);

public sealed record FundCashReceiptsReportResult(
    IReadOnlyList<FundCashReceiptsReportRow> Rows,
    int ItemCount,
    decimal TotalAmount);

public interface IFundCashReceiptsReportService
{
    Task<FundCashReceiptsReportResult> GetAsync(
        FundCashReceiptsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
