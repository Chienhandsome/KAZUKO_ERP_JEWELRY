using Kazuko.Core.Authentication;

namespace Kazuko.Infrastructure.Authentication;

/// <summary>
/// Local-only stub retained for reference. Composition must use <see cref="FirebirdAuthenticationService"/>.
/// </summary>
public sealed class DevelopmentAuthenticationService : IAuthenticationService
{
    public Task<IReadOnlyList<BranchOption>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<BranchOption>>(
            [new BranchOption(1, "CN_TRUNG_TAM", "Chi nhánh trung tâm")]);
    }

    public Task<AuthenticationResult> SignInAsync(
        string username,
        string password,
        string branchCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isValid = string.Equals(username.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                      && string.IsNullOrEmpty(password)
                      && !string.IsNullOrWhiteSpace(branchCode);

        return Task.FromResult(isValid
            ? new AuthenticationResult(true, "Quản trị viên")
            : new AuthenticationResult(false, ErrorMessage: "Tên đăng nhập hoặc mật khẩu không đúng."));
    }
}
