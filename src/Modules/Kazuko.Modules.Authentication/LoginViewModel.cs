using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Authentication;

public sealed class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private string _username = "admin";
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel(IAuthenticationService authentication)
    {
        _authentication = authentication;
        SignInCommand = new AsyncRelayCommand(SignInAsync);
    }

    public event EventHandler<string>? SignedIn;

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Password
    {
        get => _password;
        set => SetProperty(ref _password, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public ICommand SignInCommand { get; }

    private async Task SignInAsync()
    {
        ErrorMessage = string.Empty;
        IsBusy = true;
        try
        {
            var result = await _authentication.SignInAsync(Username, Password);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.ErrorMessage ?? "Không thể đăng nhập.";
                return;
            }

            SignedIn?.Invoke(this, result.DisplayName ?? Username);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
