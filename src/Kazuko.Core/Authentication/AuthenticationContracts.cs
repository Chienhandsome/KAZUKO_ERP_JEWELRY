namespace Kazuko.Core.Authentication;

public sealed record AuthenticationResult(bool IsSuccess, string? DisplayName = null, string? ErrorMessage = null);

public interface IAuthenticationService
{
    Task<AuthenticationResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default);
}
