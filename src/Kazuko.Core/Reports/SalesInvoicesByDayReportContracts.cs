using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesInvoicesByDayReportFilter(
    DateTime FromDate,
    DateTime ToDate);

public sealed record SalesInvoicesByDayReportRow(
    DateTime DocumentDate,
    int InvoiceCount,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal Profit);

public sealed record SalesInvoicesByDayReportResult(
    IReadOnlyList<SalesInvoicesByDayReportRow> Rows,
    int TotalInvoices,
    decimal TotalRevenue,
    decimal TotalCost,
    decimal TotalProfit);

public interface ISalesInvoicesByDayReportService
{
    Task<SalesInvoicesByDayReportResult> GetAsync(
        SalesInvoicesByDayReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
