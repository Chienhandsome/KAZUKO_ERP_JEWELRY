using Kazuko.Core.Authentication;

namespace Kazuko.Infrastructure.Authentication;

public sealed class DevelopmentAuthenticationService : IAuthenticationService
{
    public Task<AuthenticationResult> SignInAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var isValid = string.Equals(username.Trim(), "admin", StringComparison.OrdinalIgnoreCase)
                      && string.IsNullOrEmpty(password);

        return Task.FromResult(isValid
            ? new AuthenticationResult(true, "Quản trị viên")
            : new AuthenticationResult(false, ErrorMessage: "Tên đăng nhập hoặc mật khẩu không đúng."));
    }
}
