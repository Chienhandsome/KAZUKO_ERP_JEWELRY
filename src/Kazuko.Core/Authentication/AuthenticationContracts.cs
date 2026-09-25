namespace Kazuko.Core.Authentication;

public sealed record BranchOption(long Id, string Code, string Name)
{
    public override string ToString() => Name;
}

public sealed record AuthenticatedSession(
    long UserId,
    string Username,
    string DisplayName,
    string RoleCode,
    long BranchId,
    string BranchCode,
    string BranchName);

public sealed record AuthenticationResult(
    bool IsSuccess,
    string? DisplayName = null,
    string? ErrorMessage = null,
    AuthenticatedSession? Session = null);

public interface IAuthenticationService
{
    Task<IReadOnlyList<BranchOption>> GetBranchesAsync(CancellationToken cancellationToken = default);

    Task<AuthenticationResult> SignInAsync(
        string username,
        string password,
        string branchCode,
        CancellationToken cancellationToken = default);
}

public interface ICurrentSessionAccessor
{
    AuthenticatedSession? Current { get; }
    void Set(AuthenticatedSession session);
    void Clear();
}

public sealed class CurrentSessionAccessor : ICurrentSessionAccessor
{
    public AuthenticatedSession? Current { get; private set; }

    public void Set(AuthenticatedSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        Current = session;
    }

    public void Clear() => Current = null;
}
