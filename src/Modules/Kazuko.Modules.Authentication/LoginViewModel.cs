using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Authentication;

public sealed class LoginViewModel : ObservableObject
{
    private readonly IAuthenticationService _authentication;
    private readonly Func<Task>? _chooseDatabase;
    private readonly Func<Task>? _createDatabase;
    private string _username = "admin";
    private string _password = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _isLoadingBranches;
    private bool _isConnected;
    private bool _rememberUsername = true;
    private BranchOption? _selectedBranch;

    public LoginViewModel(IAuthenticationService authentication, Func<Task>? chooseDatabase = null,
        string databaseDisplayName = "", Func<Task>? createDatabase = null)
    {
        _authentication = authentication;
        _chooseDatabase = chooseDatabase;
        _createDatabase = createDatabase;
        DatabaseDisplayName = databaseDisplayName;
        SignInCommand = new AsyncRelayCommand(SignInAsync);
        ChooseDatabaseCommand = new AsyncRelayCommand(() => RunDatabaseActionAsync(_chooseDatabase), () => _chooseDatabase is not null && IsFormEnabled);
        CreateDatabaseCommand = new AsyncRelayCommand(() => RunDatabaseActionAsync(_createDatabase), () => _createDatabase is not null && IsFormEnabled);
        _ = LoadBranchesAsync();
    }

    public event EventHandler<AuthenticatedSession>? SignedIn;

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
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsFormEnabled));
                ((AsyncRelayCommand)ChooseDatabaseCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CreateDatabaseCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsLoadingBranches
    {
        get => _isLoadingBranches;
        private set
        {
            if (SetProperty(ref _isLoadingBranches, value))
            {
                OnPropertyChanged(nameof(IsFormEnabled));
                ((AsyncRelayCommand)ChooseDatabaseCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CreateDatabaseCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsFormEnabled => !IsBusy && !IsLoadingBranches;

    public bool IsConnected => _isConnected;
    public string ConnectionStatusText => IsLoadingBranches
        ? "Đang kết nối…"
        : IsConnected ? "● Đã kết nối" : "Chưa kết nối";
    public string ConnectionDetails => IsLoadingBranches
        ? "Đang kiểm tra kết nối bằng cách tải danh sách chi nhánh."
        : IsConnected
            ? $"Dịch vụ đăng nhập đã phản hồi. Đã tải {Branches.Count} chi nhánh hoạt động."
            : "Không thể tải danh sách chi nhánh. Vui lòng kiểm tra cấu hình kết nối.";

    public bool RememberUsername
    {
        get => _rememberUsername;
        set => SetProperty(ref _rememberUsername, value);
    }

    public ICommand SignInCommand { get; }
    public ICommand ChooseDatabaseCommand { get; }
    public ICommand CreateDatabaseCommand { get; }
    public string DatabaseDisplayName { get; }
    public string DatabaseFileName => DatabaseDisplayName[(DatabaseDisplayName.LastIndexOfAny(['\\', '/', ':']) + 1)..];

    private async Task RunDatabaseActionAsync(Func<Task>? action)
    {
        if (!IsFormEnabled || action is null) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        try { await action(); }
        catch (Exception)
        {
            ErrorMessage = "Chưa hoàn tất thao tác với cơ sở dữ liệu. Vui lòng thử lại.";
        }
        finally { IsBusy = false; }
    }

    private async Task LoadBranchesAsync()
    {
        IsLoadingBranches = true;
        ErrorMessage = string.Empty;
        try
        {
            var branches = await _authentication.GetBranchesAsync();
            _isConnected = true;
            Branches.Clear();
            foreach (var branch in branches)
                Branches.Add(branch);

            SelectedBranch = Branches.Count > 0 ? Branches[0] : null;
            if (Branches.Count == 0)
                ErrorMessage = "Chưa có chi nhánh hoạt động trong hệ thống.";
        }
        catch (InvalidOperationException ex) when (IsConfigurationError(ex.Message))
        {
            Branches.Clear();
            SelectedBranch = null;
            ErrorMessage = "Chưa cấu hình kết nối Firebird. Liên hệ quản trị viên.";
        }
        catch (Exception)
        {
            Branches.Clear();
            SelectedBranch = null;
            ErrorMessage = "Không thể tải danh sách chi nhánh từ database.";
        }
        finally
        {
            IsLoadingBranches = false;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(ConnectionStatusText));
            OnPropertyChanged(nameof(ConnectionDetails));
        }
    }

    private async Task SignInAsync()
    {
        if (!IsFormEnabled)
            return;

        ErrorMessage = string.Empty;

        if (SelectedBranch is null)
        {
            ErrorMessage = Branches.Count == 0
                ? "Chưa cấu hình kết nối Firebird. Liên hệ quản trị viên."
                : "Vui lòng chọn chi nhánh.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authentication.SignInAsync(Username, Password, SelectedBranch.Code);
            if (!result.IsSuccess)
            {
                ErrorMessage = SanitizeError(result.ErrorMessage) ?? "Không thể đăng nhập.";
                return;
            }

            var session = result.Session ?? new AuthenticatedSession(
                0,
                Username.Trim(),
                result.DisplayName ?? Username.Trim(),
                string.Empty,
                SelectedBranch.Id,
                SelectedBranch.Code,
                SelectedBranch.Name);

            SignedIn?.Invoke(this, session);
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

    private static bool IsConfigurationError(string message) =>
        message.StartsWith("Cấu hình", StringComparison.Ordinal) ||
        message.Contains("cấu hình", StringComparison.OrdinalIgnoreCase);

    private static string? SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        if (IsConfigurationError(message))
            return "Chưa cấu hình kết nối Firebird. Liên hệ quản trị viên.";

        return message;
    }
}
