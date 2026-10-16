using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Reports;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdFundCashPaymentsReportService : IFundCashPaymentsReportService
{
    public async Task<FundCashPaymentsReportResult> GetAsync(
        FundCashPaymentsReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var rawRows = await FundCashVoucherQueryHelper.QueryAsync(
            "OUT", filter.FromDate, filter.ToDate, session, cancellationToken);

        var rows = new List<FundCashPaymentsReportRow>(rawRows.Count);
        decimal total = 0m;
        foreach (var raw in rawRows)
        {
            total += raw.AmountVnd;
            var (partyName, content) = CashVoucherNoteFormat.Parse(raw.RawNotes);
            rows.Add(new FundCashPaymentsReportRow(
                raw.Id,
                raw.Code,
                raw.VoucherDate,
                partyName ?? string.Empty,
                raw.AmountVnd,
                content,
                raw.CreatedByName,
                raw.FundName));
        }

        return new FundCashPaymentsReportResult(rows, rows.Count, total);
    }
}
