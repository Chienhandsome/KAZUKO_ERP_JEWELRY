using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record SalesInvoicesReportFilter(
    DateTime FromDate,
    DateTime ToDate,
    string CustomerText);

public sealed record SalesInvoicesReportRow(
    long Id,
    string Code,
    DateTime DocumentDate,
    string CustomerName,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal RemainingAmount);

public sealed record SalesInvoicesReportResult(
    IReadOnlyList<SalesInvoicesReportRow> Rows,
    int InvoiceCount,
    decimal TotalAmount,
    decimal TotalPaid,
    decimal TotalRemaining);

public interface ISalesInvoicesReportService
{
    Task<SalesInvoicesReportResult> GetAsync(
        SalesInvoicesReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
