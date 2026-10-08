using Kazuko.Core.Authentication;

namespace Kazuko.Core.Company;

public sealed record CompanyProfileDto(
    long CompanyId,
    string Name,
    string? Phone,
    string? Address,
    string? LogoPath);

public sealed record CompanyProfileUpdateRequest(
    string Name,
    string? Phone,
    string? Address,
    string? LogoPath);

public interface ICompanyProfileService
{
    Task<CompanyProfileDto> GetAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CompanyProfileUpdateRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
