using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record MasterSuppliersReportFilter(string SearchText);

public sealed record MasterSuppliersReportRow(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Email,
    string Address,
    string TaxCode,
    string ContactName);

public sealed record MasterSuppliersReportResult(
    IReadOnlyList<MasterSuppliersReportRow> Rows,
    int ItemCount);

public interface IMasterSuppliersReportService
{
    Task<MasterSuppliersReportResult> GetAsync(
        MasterSuppliersReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
