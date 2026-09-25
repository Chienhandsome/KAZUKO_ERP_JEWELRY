using System.Collections.ObjectModel;
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
    private BranchOption? _selectedBranch;

    public LoginViewModel(IAuthenticationService authentication)
    {
        _authentication = authentication;
        SignInCommand = new AsyncRelayCommand(SignInAsync);
        _ = LoadBranchesAsync();
    }

    public event EventHandler<string>? SignedIn;

    public ObservableCollection<BranchOption> Branches { get; } = new();

    public BranchOption? SelectedBranch
    {
        get => _selectedBranch;
        set => SetProperty(ref _selectedBranch, value);
    }

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

    private async Task LoadBranchesAsync()
    {
        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var branches = await _authentication.GetBranchesAsync();
            Branches.Clear();
            foreach (var branch in branches)
                Branches.Add(branch);

            SelectedBranch = Branches.Count > 0 ? Branches[0] : null;
            if (Branches.Count == 0)
                ErrorMessage = "Chưa có chi nhánh hoạt động trong hệ thống.";
        }
        catch (Exception)
        {
            Branches.Clear();
            SelectedBranch = null;
            ErrorMessage = "Không thể tải danh sách chi nhánh từ database.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SignInAsync()
    {
        if (IsBusy)
            return;

        ErrorMessage = string.Empty;

        if (SelectedBranch is null)
        {
            ErrorMessage = "Vui lòng chọn chi nhánh.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authentication.SignInAsync(Username, Password, SelectedBranch.Code);
            if (!result.IsSuccess)
            {
                ErrorMessage = result.ErrorMessage ?? "Không thể đăng nhập.";
                return;
            }

            SignedIn?.Invoke(this, result.DisplayName ?? Username);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Yêu cầu đăng nhập đã bị hủy.";
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể kết nối dịch vụ đăng nhập. Vui lòng thử lại.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
