namespace Kazuko.Core.Authentication;

public sealed record BranchOption(long Id, string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record AuthenticationResult(bool IsSuccess, string? DisplayName = null, string? ErrorMessage = null);

public interface IAuthenticationService
{
    Task<IReadOnlyList<BranchOption>> GetBranchesAsync(CancellationToken cancellationToken = default);

    Task<AuthenticationResult> SignInAsync(
        string username,
        string password,
        string branchCode,
        CancellationToken cancellationToken = default);
}
