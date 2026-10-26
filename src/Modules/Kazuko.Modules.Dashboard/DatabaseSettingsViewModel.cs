using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Settings;
using Microsoft.Win32;

namespace Kazuko.Modules.Dashboard;

/// <summary>
/// ViewModel cho tab "Cơ sở dữ liệu" trong Cài đặt (chỉ admin).
/// Hiển thị thông tin DB hiện tại và các thao tác: đổi DB, backup, tạo mới.
/// </summary>
public sealed class DatabaseSettingsViewModel : ObservableObject
{
    private readonly Kazuko.Core.UserManagement.IAuthorizationService? _permissions;
    private readonly IDatabaseManagementService _database;
    private readonly IPreferencesService _preferences;
    private readonly IAppearanceService _appearance;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    private string _filePath = "—";
    private string _fileSize = "—";
    private string _schemaVersion = "—";
    private string _branchCount = "—";
    private string _userCount = "—";
    private bool _autoBackupEnabled = true;
    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;
    private string _backupHistory = "Chưa có lịch sử sao lưu.";
    private string _operationMessage = string.Empty;
    private CancellationTokenSource? _backupCancellation;

    public DatabaseSettingsViewModel(
        IDatabaseManagementService database,
        IPreferencesService preferences,
        IAppearanceService appearance,
        ICurrentSessionAccessor sessionAccessor,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _appearance = appearance ?? throw new ArgumentNullException(nameof(appearance));
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        _permissions = authorization;

        ChangeDatabaseCommand = new AsyncRelayCommand(ChangeDatabaseAsync, () => !IsBusy);
        BackupCommand = new AsyncRelayCommand(BackupAsync, () => !IsBusy);
        VerifyBackupCommand = new AsyncRelayCommand(VerifyBackupAsync, () => !IsBusy);
        CancelBackupCommand = new RelayCommand(CancelBackup, () => _backupCancellation is { IsCancellationRequested: false });
        CreateNewCommand = new AsyncRelayCommand(CreateNewAsync, () => !IsBusy);
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);

        _ = LoadAsync();
    }

    public string FilePath
    {
        get => _filePath;
        private set => SetProperty(ref _filePath, value);
    }

    public string FileSize
    {
        get => _fileSize;
        private set => SetProperty(ref _fileSize, value);
    }

    public string SchemaVersion
    {
        get => _schemaVersion;
        private set => SetProperty(ref _schemaVersion, value);
    }

    public string BranchCount
    {
        get => _branchCount;
        private set => SetProperty(ref _branchCount, value);
    }

    public string UserCount
    {
        get => _userCount;
        private set => SetProperty(ref _userCount, value);
    }

    public bool AutoBackupEnabled
    {
        get => _autoBackupEnabled;
        set
        {
            if (!SetProperty(ref _autoBackupEnabled, value))
                return;
            SaveAutoBackupPreference();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCommands();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand ChangeDatabaseCommand { get; }
    public ICommand BackupCommand { get; }
    public ICommand VerifyBackupCommand { get; }
    public ICommand CancelBackupCommand { get; }
    public string BackupHistory { get => _backupHistory; private set => SetProperty(ref _backupHistory, value); }
    public string OperationMessage { get => _operationMessage; private set => SetProperty(ref _operationMessage, value); }
    public ICommand CreateNewCommand { get; }
    public ICommand ReloadCommand { get; }

    private AuthenticatedSession? RequireSession()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
            ErrorMessage = "Chưa đăng nhập.";
        return session;
    }

    private static bool IsAdmin(AuthenticatedSession session) =>
        string.Equals(session.RoleCode, "ADMIN", StringComparison.OrdinalIgnoreCase);

    private async Task LoadAsync()
    {
        if (_permissions is not null && !_permissions.Current.Allows("SYS_DATABASE")) return;
        var session = RequireSession();
        if (session is null)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            // Local history remains visible even if the database/server cannot be reached.
            LoadBackupHistory();
            var info = await _database.GetInfoAsync(session);
            FilePath = info.FilePath;
            FileSize = info.FileSizeDisplay;
            SchemaVersion = info.SchemaVersionDisplay;
            BranchCount = info.BranchCount.ToString("N0");
            UserCount = info.UserCount.ToString("N0");

            try { AutoBackupEnabled = _preferences.Load().AutoBackupEnabled; }
            catch { /* giữ giá trị mặc định */ }
        }
        catch (Exception ex)
        {
            ErrorMessage = Sanitize(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SaveAutoBackupPreference()
    {
        try
        {
            var current = _preferences.Load();
            _preferences.Save(current with { AutoBackupEnabled = _autoBackupEnabled });
            StatusMessage = _autoBackupEnabled
                ? "Đã bật tự động backup khi khởi động."
                : "Đã tắt tự động backup khi khởi động.";
        }
        catch (Exception ex)
        {
            ErrorMessage = Sanitize(ex.Message);
        }
    }

    private async Task ChangeDatabaseAsync()
    {
        var session = RequireSession();
        if (session is null || !EnsureAdmin(session))
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn file cơ sở dữ liệu",
            Filter = "Firebird (*.fdb)|*.fdb|Tất cả|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var validation = await _database.ValidateDatabaseFileAsync(dialog.FileName);
            if (!validation.IsValid)
            {
                ErrorMessage = validation.ErrorMessage ?? "File cơ sở dữ liệu không hợp lệ.";
                return;
            }

            var confirm = MessageBox.Show(
                $"Chuyển sang dùng cơ sở dữ liệu:\n{dialog.FileName}\n\n" +
                $"Phiên bản schema: {(validation.SchemaVersion > 0 ? $"v{validation.SchemaVersion}" : "không xác định")}\n\n" +
                "Ứng dụng sẽ cần khởi động lại để áp dụng. Dữ liệu chưa lưu có thể bị mất.\n\n" +
                "Bạn có chắc chắn muốn tiếp tục?",
                "Xác nhận đổi cơ sở dữ liệu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            _database.SwitchDatabase(dialog.FileName);
            StatusMessage = "Đã lưu cấu hình. Vui lòng khởi động lại ứng dụng để dùng cơ sở dữ liệu mới.";

            var restart = MessageBox.Show(
                "Đã đổi cơ sở dữ liệu. Ứng dụng cần khởi động lại ngay bây giờ.\n\nKhởi động lại ngay?",
                "Khởi động lại",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (restart == MessageBoxResult.Yes)
            {
                RestartApplication();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = Sanitize(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task BackupAsync()
    {
        var session = RequireSession();
        if (session is null || !EnsureAdmin(session))
            return;

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Chọn thư mục lưu bản sao lưu .fbk",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        BeginBackupOperation();
        try
        {
            var backupPath = await _database.BackupAsync(dialog.SelectedPath, session,
                _backupCancellation!.Token, new Progress<string>(message => OperationMessage = message));
            StatusMessage = $"Đã sao lưu thành công:\n{backupPath}\nCó thể bấm Kiểm tra bản sao lưu để khôi phục thử vào database tạm.";
            AskToOpenFolder(Path.GetDirectoryName(backupPath));
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy sao lưu."; }
        catch (Exception ex)
        {
            ErrorMessage = Sanitize(ex.Message);
        }
        finally
        {
            EndBackupOperation();
            IsBusy = false;
        }
    }

    private async Task VerifyBackupAsync()
    {
        var session = RequireSession();
        if (session is null || !EnsureAdmin(session)) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Kiểm tra bản sao lưu", Filter = "Bản sao lưu Firebird (*.fbk)|*.fbk", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        BeginBackupOperation();
        try
        {
            var result = await _database.VerifyBackupAsync(dialog.FileName, session, _backupCancellation!.Token,
                new Progress<string>(message => OperationMessage = message));
            StatusMessage = $"Bản sao lưu đã khôi phục thử thành công (schema v{result.SchemaVersion}, {result.CompanyCount} công ty). Database tạm đã được dọn dẹp.";
        }
        catch (OperationCanceledException) { StatusMessage = "Đã hủy kiểm tra bản sao lưu."; }
        catch (Exception ex) { ErrorMessage = Sanitize(ex.Message); }
        finally { EndBackupOperation(); IsBusy = false; }
    }

    private void BeginBackupOperation()
    {
        _backupCancellation = new CancellationTokenSource();
        OperationMessage = "Đang chuẩn bị…";
        ((RelayCommand)CancelBackupCommand).NotifyCanExecuteChanged();
    }

    private void CancelBackup()
    {
        OperationMessage = "Đang hủy; chờ Firebird kết thúc thao tác và dọn file tạm…";
        _backupCancellation?.Cancel();
        ((RelayCommand)CancelBackupCommand).NotifyCanExecuteChanged();
    }

    private void EndBackupOperation()
    {
        _backupCancellation?.Dispose();
        _backupCancellation = null;
        OperationMessage = string.Empty;
        ((RelayCommand)CancelBackupCommand).NotifyCanExecuteChanged();
        try { LoadBackupHistory(); }
        catch (Exception ex) { ErrorMessage = Sanitize(ex.Message); }
    }

    private void LoadBackupHistory()
    {
        var status = _database.GetBackupStatus();
        BackupHistory = status.LastSuccessAt is { } success
            ? $"Sao lưu thành công gần nhất: {success.LocalDateTime:dd/MM/yyyy HH:mm:ss}\n{status.LastSuccessPath}"
            : "Chưa có bản sao lưu thành công được ghi nhận.";
        if (status.LastVerificationAt is { } verified)
            BackupHistory += $"\nKhôi phục thử thành công: {verified.LocalDateTime:dd/MM/yyyy HH:mm:ss}\n{status.LastVerifiedPath}";
        if (!string.IsNullOrEmpty(status.LastError))
            BackupHistory += $"\nTác vụ gần nhất ({status.LastAttemptAt?.LocalDateTime:dd/MM/yyyy HH:mm:ss}) chưa hoàn tất: {status.LastError}";
    }

    private async Task CreateNewAsync()
    {
        var session = RequireSession();
        if (session is null || !EnsureAdmin(session))
            return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Tạo cơ sở dữ liệu trắng mới",
            Filter = "Firebird (*.fdb)|*.fdb|Tất cả|*.*",
            FileName = "KAZUKO_new.fdb",
            AddExtension = true,
            DefaultExt = ".fdb",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var confirm = MessageBox.Show(
                $"Tạo cơ sở dữ liệu trắng mới tại:\n{dialog.FileName}\n\n" +
                "DB mới sẽ có đầy đủ cấu trúc, dữ liệu giao dịch trống, " +
                "giữ lại danh mục (công ty, chi nhánh, tài khoản, loại vàng, quỹ).\n\n" +
                "Bạn có chắc chắn muốn tiếp tục?",
                "Xác nhận tạo cơ sở dữ liệu mới",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;

            await _database.CreateBlankDatabaseAsync(dialog.FileName, session);
            StatusMessage = $"Đã tạo cơ sở dữ liệu mới:\n{dialog.FileName}";
            AskToOpenFolder(Path.GetDirectoryName(dialog.FileName));

            var switchNow = MessageBox.Show(
                "Bạn có muốn chuyển sang dùng cơ sở dữ liệu mới ngay không?\n\n" +
                "Ứng dụng sẽ cần khởi động lại để áp dụng.",
                "Chuyển sang DB mới",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (switchNow == MessageBoxResult.Yes)
            {
                _database.SwitchDatabase(dialog.FileName);
                var restart = MessageBox.Show(
                    "Đã chuyển sang cơ sở dữ liệu mới. Khởi động lại ngay bây giờ?",
                    "Khởi động lại",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (restart == MessageBoxResult.Yes)
                    RestartApplication();
                else
                    StatusMessage = "Đã lưu cấu hình. Vui lòng khởi động lại ứng dụng để dùng cơ sở dữ liệu mới.";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = Sanitize(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Hỏi người dùng có muốn mở thư mục chứa file vừa tạo không.
    /// </summary>
    private static void AskToOpenFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return;

        var result = MessageBox.Show(
            $"File đã được tạo tại:\n{folderPath}\n\nBạn có muốn mở thư mục chứa file vừa tạo không?",
            "Mở thư mục",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folderPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không mở được thư mục: {Sanitize(ex.Message)}",
                "Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static void RestartApplication()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable))
        {
            MessageBox.Show(
                "Không xác định được file chạy. Vui lòng tắt và mở lại ứng dụng thủ công.",
                "Khởi động lại",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Không khởi động lại được: {Sanitize(ex.Message)}\nVui lòng tắt và mở lại thủ công.",
                "Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        Application.Current.Shutdown();
    }

    private bool EnsureAdmin(AuthenticatedSession session)
    {
        if (_permissions?.Current.Allows("SYS_DATABASE", Kazuko.Core.UserManagement.PermissionAction.Edit) ?? IsAdmin(session))
            return true;
        ErrorMessage = "Chỉ tài khoản quản trị (admin) mới được thao tác cơ sở dữ liệu.";
        return false;
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)ChangeDatabaseCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)BackupCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)VerifyBackupCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CreateNewCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ReloadCommand).NotifyCanExecuteChanged();
    }

    private static string Sanitize(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "Đã xảy ra lỗi." : message.Trim();
}
