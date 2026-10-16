using Kazuko.Core.Authentication;

namespace Kazuko.Core.Reports;

public sealed record MasterCustomersReportFilter(string SearchText);

public sealed record MasterCustomersReportRow(
    long Id,
    string Code,
    string Name,
    string Phone,
    string Email,
    string Address,
    string GroupName,
    string IdentityNumber,
    decimal CreditLimit);

public sealed record MasterCustomersReportResult(
    IReadOnlyList<MasterCustomersReportRow> Rows,
    int ItemCount,
    decimal TotalCreditLimit);

public interface IMasterCustomersReportService
{
    Task<MasterCustomersReportResult> GetAsync(
        MasterCustomersReportFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
