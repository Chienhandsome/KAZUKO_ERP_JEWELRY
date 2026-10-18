using System.IO;
using System.Windows.Input;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;
using Kazuko.Core.Settings;
using Microsoft.Win32;

namespace Kazuko.Modules.Dashboard;

public sealed record SettingsCategory(string Key, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly Kazuko.Core.UserManagement.IAuthorizationService? _permissions;
    private readonly ICompanyProfileService _companyProfile;
    private readonly IPrintTemplateStore _printTemplates;
    private readonly ICurrentSessionAccessor _sessionAccessor;

    private SettingsCategory _selectedCategory;
    private string _companyName = string.Empty;
    private string _phone = string.Empty;
    private string _address = string.Empty;
    private string? _logoPath;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _isDirty;
    private string _receiptTemplateName = "—";
    private string _receiptTemplateCode = "—";
    private string _receiptTemplateUpdatedAt = "—";
    private string _receiptTemplateHash = "—";
    private string _receiptTemplateSize = "—";
    private ReceiptTemplateKindOption _selectedReceiptTemplateKind;

    public SettingsViewModel(
        ICompanyProfileService companyProfile,
        ICurrentSessionAccessor sessionAccessor,
        IAppearanceService appearance,
        IPrintTemplateStore printTemplates,
        IDatabaseManagementService databaseManagement,
        IPreferencesService preferences,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null)
    {
        _companyProfile = companyProfile ?? throw new ArgumentNullException(nameof(companyProfile));
        _permissions = authorization;
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        _printTemplates = printTemplates ?? throw new ArgumentNullException(nameof(printTemplates));
        Appearance = new AppearanceSettingsViewModel(
            appearance ?? throw new ArgumentNullException(nameof(appearance)));
        Database = new DatabaseSettingsViewModel(
            databaseManagement ?? throw new ArgumentNullException(nameof(databaseManagement)),
            preferences ?? throw new ArgumentNullException(nameof(preferences)),
            appearance,
            sessionAccessor, authorization);

        var categories = new List<SettingsCategory>
        {
            new("general", "Thông tin chung"),
            new("receipt_template", "Mẫu in bill"),
            new("appearance", "Giao diện")
        };
        if (IsAdminUser())
            categories.Add(new SettingsCategory("database", "Cơ sở dữ liệu"));
        Categories = categories;
        _selectedCategory = Categories[0];

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && IsGeneralSelected);
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && IsGeneralSelected);
        PickLogoCommand = new RelayCommand(PickLogo, () => !IsBusy && IsGeneralSelected);
        ClearLogoCommand = new RelayCommand(ClearLogo, () => !IsBusy && IsGeneralSelected && HasLogo);
        SelectCategoryCommand = new RelayCommand<SettingsCategory>(SelectCategory);
        ExportReceiptTemplateCommand = new AsyncRelayCommand(
            ExportReceiptTemplateAsync, () => !IsBusy && IsReceiptTemplateSelected);
        ImportReceiptTemplateCommand = new AsyncRelayCommand(
            ImportReceiptTemplateAsync, () => !IsBusy && IsReceiptTemplateSelected);
        ReloadReceiptTemplateCommand = new AsyncRelayCommand(
            LoadReceiptTemplateSummaryAsync, () => !IsBusy && IsReceiptTemplateSelected);

        ReceiptTemplateKinds =
        [
            new ReceiptTemplateKindOption(
                PrintTemplateCodes.SalesReceiptA5,
                "A5 ngang",
                "phoi_a5_ngang.frx",
                "Bill bán hàng A5 ngang"),
            new ReceiptTemplateKindOption(
                PrintTemplateCodes.SalesReceiptA5Portrait,
                "A5 dọc",
                "phoi_a5_doc.frx",
                "Bill bán hàng A5 dọc")
        ];
        _selectedReceiptTemplateKind = ReceiptTemplateKinds[0];

        _ = LoadAsync();
    }

    public IReadOnlyList<ReceiptTemplateKindOption> ReceiptTemplateKinds { get; }

    public ReceiptTemplateKindOption SelectedReceiptTemplateKind
    {
        get => _selectedReceiptTemplateKind;
        set
        {
            if (value is null || !SetProperty(ref _selectedReceiptTemplateKind, value))
                return;
            if (IsReceiptTemplateSelected)
                _ = LoadReceiptTemplateSummaryAsync();
        }
    }

    public IReadOnlyList<SettingsCategory> Categories { get; }

    public AppearanceSettingsViewModel Appearance { get; }

    public DatabaseSettingsViewModel Database { get; }

    /// <summary>Tab Cơ sở dữ liệu chỉ hiện với tài khoản admin.</summary>
    public bool IsDatabaseVisible => _permissions?.Current.Allows("SYS_DATABASE") ?? IsAdminUser();

    private bool IsAdminUser()
    {
        if (_permissions is not null) return _permissions.Current.Allows("SYS_DATABASE");
        var session = _sessionAccessor.Current;
        return session is not null &&
               string.Equals(session.RoleCode, "ADMIN", StringComparison.OrdinalIgnoreCase);
    }

    public SettingsCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is null || !SetProperty(ref _selectedCategory, value))
                return;
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAppearanceSelected));
            OnPropertyChanged(nameof(IsReceiptTemplateSelected));
            OnPropertyChanged(nameof(IsDatabaseSelected));
            NotifyCommands();
            if (IsReceiptTemplateSelected)
                _ = LoadReceiptTemplateSummaryAsync();
        }
    }

    public bool IsGeneralSelected =>
        string.Equals(SelectedCategory.Key, "general", StringComparison.Ordinal);

    public bool IsAppearanceSelected =>
        string.Equals(SelectedCategory.Key, "appearance", StringComparison.Ordinal);

    public bool IsReceiptTemplateSelected =>
        string.Equals(SelectedCategory.Key, "receipt_template", StringComparison.Ordinal);

    public bool IsDatabaseSelected =>
        string.Equals(SelectedCategory.Key, "database", StringComparison.Ordinal);

    public string CompanyName
    {
        get => _companyName;
        set
        {
            if (SetProperty(ref _companyName, value ?? string.Empty))
                MarkDirty();
        }
    }

    public string Phone
    {
        get => _phone;
        set
        {
            if (SetProperty(ref _phone, value ?? string.Empty))
                MarkDirty();
        }
    }

    public string Address
    {
        get => _address;
        set
        {
            if (SetProperty(ref _address, value ?? string.Empty))
                MarkDirty();
        }
    }

    public string? LogoPath
    {
        get => _logoPath;
        private set
        {
            if (SetProperty(ref _logoPath, value))
            {
                OnPropertyChanged(nameof(HasLogo));
                NotifyCommands();
            }
        }
    }

    public bool HasLogo => !string.IsNullOrWhiteSpace(LogoPath);

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
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
                NotifyCommands();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public string ReceiptTemplateName
    {
        get => _receiptTemplateName;
        private set => SetProperty(ref _receiptTemplateName, value);
    }

    public string ReceiptTemplateCode
    {
        get => _receiptTemplateCode;
        private set => SetProperty(ref _receiptTemplateCode, value);
    }

    public string ReceiptTemplateUpdatedAt
    {
        get => _receiptTemplateUpdatedAt;
        private set => SetProperty(ref _receiptTemplateUpdatedAt, value);
    }

    public string ReceiptTemplateHash
    {
        get => _receiptTemplateHash;
        private set => SetProperty(ref _receiptTemplateHash, value);
    }

    public string ReceiptTemplateSize
    {
        get => _receiptTemplateSize;
        private set => SetProperty(ref _receiptTemplateSize, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand PickLogoCommand { get; }
    public ICommand ClearLogoCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand ExportReceiptTemplateCommand { get; }
    public ICommand ImportReceiptTemplateCommand { get; }
    public ICommand ReloadReceiptTemplateCommand { get; }

    private void SelectCategory(SettingsCategory? category)
    {
        if (category is not null)
            SelectedCategory = category;
    }

    private async Task LoadAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var profile = await _companyProfile.GetAsync(session);
            _companyName = profile.Name;
            _phone = profile.Phone ?? string.Empty;
            _address = profile.Address ?? string.Empty;
            _logoPath = profile.LogoPath;
            OnPropertyChanged(nameof(CompanyName));
            OnPropertyChanged(nameof(Phone));
            OnPropertyChanged(nameof(Address));
            OnPropertyChanged(nameof(LogoPath));
            OnPropertyChanged(nameof(HasLogo));
            IsDirty = false;
            StatusMessage = "Đã tải thông tin doanh nghiệp.";
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

    private async Task SaveAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            await _companyProfile.SaveAsync(
                new CompanyProfileUpdateRequest(
                    CompanyName,
                    Phone,
                    Address,
                    LogoPath),
                session);
            IsDirty = false;
            StatusMessage = "Đã lưu thông tin doanh nghiệp.";
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

    private void PickLogo()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Chọn logo doanh nghiệp",
            Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Tất cả|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var previous = LogoPath;
            var imported = CompanyLogoStore.Import(dialog.FileName);
            LogoPath = imported;
            if (!string.Equals(previous, imported, StringComparison.OrdinalIgnoreCase))
                CompanyLogoStore.TryDeleteManagedFile(previous);
            MarkDirty();
            StatusMessage = "Đã chọn logo mới. Nhấn Lưu để áp dụng.";
            ErrorMessage = string.Empty;
        }
        catch (Exception ex)
        {
            ErrorMessage = Sanitize(ex.Message);
        }
    }

    private void ClearLogo()
    {
        var previous = LogoPath;
        LogoPath = null;
        CompanyLogoStore.TryDeleteManagedFile(previous);
        MarkDirty();
        StatusMessage = "Đã xóa logo. Nhấn Lưu để áp dụng.";
        ErrorMessage = string.Empty;
    }

    private async Task LoadReceiptTemplateSummaryAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ClearReceiptTemplateInfo("Chưa đăng nhập.");
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        try
        {
            var kind = SelectedReceiptTemplateKind;
            var template = await _printTemplates.GetByCodeAsync(kind.Code, session);
            ApplyReceiptTemplateInfo(template);
        }
        catch (Exception ex)
        {
            ClearReceiptTemplateInfo("Không tải được mẫu in.");
            ErrorMessage = Sanitize(ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyReceiptTemplateInfo(PrintTemplateDto template)
    {
        ReceiptTemplateName = template.Name;
        ReceiptTemplateCode = template.Code;
        ReceiptTemplateUpdatedAt = template.UpdatedAt.ToString("dd/MM/yyyy HH:mm");
        ReceiptTemplateHash = template.ContentHash.Length <= 16
            ? template.ContentHash
            : $"{template.ContentHash[..16]}…";
        ReceiptTemplateSize = $"{template.Content.Length:N0} bytes";
    }

    private void ClearReceiptTemplateInfo(string title)
    {
        ReceiptTemplateName = title;
        ReceiptTemplateCode = "—";
        ReceiptTemplateUpdatedAt = "—";
        ReceiptTemplateHash = "—";
        ReceiptTemplateSize = "—";
    }

    private async Task ExportReceiptTemplateAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var kind = SelectedReceiptTemplateKind;
            var template = await _printTemplates.GetByCodeAsync(kind.Code, session);
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Xuất mẫu bill {kind.Label}",
                Filter = "FastReport (*.frx)|*.frx|Tất cả|*.*",
                FileName = kind.DefaultFileName,
                AddExtension = true,
                DefaultExt = ".frx"
            };
            if (dialog.ShowDialog() != true)
                return;

            await File.WriteAllBytesAsync(dialog.FileName, template.Content);
            StatusMessage = $"Đã xuất mẫu tới {dialog.FileName}. Mở bằng FastReport Designer Community để chỉnh.";
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

    private async Task ImportReceiptTemplateAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"Import mẫu bill {SelectedReceiptTemplateKind.Label}",
            Filter = "FastReport (*.frx)|*.frx|Tất cả|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        IsBusy = true;
        ErrorMessage = string.Empty;
        try
        {
            var kind = SelectedReceiptTemplateKind;
            var bytes = await File.ReadAllBytesAsync(dialog.FileName);
            var saved = await _printTemplates.SaveAsync(
                kind.Code,
                kind.DisplayName,
                bytes,
                $"Import từ {Path.GetFileName(dialog.FileName)}",
                session);
            ApplyReceiptTemplateInfo(saved);
            StatusMessage = string.Empty;
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

    private void MarkDirty()
    {
        IsDirty = true;
        StatusMessage = string.Empty;
    }

    private void NotifyCommands()
    {
        ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ReloadCommand).NotifyCanExecuteChanged();
        ((RelayCommand)PickLogoCommand).NotifyCanExecuteChanged();
        ((RelayCommand)ClearLogoCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ExportReceiptTemplateCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ImportReceiptTemplateCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)ReloadReceiptTemplateCommand).NotifyCanExecuteChanged();
    }

    private static string Sanitize(string message) =>
        string.IsNullOrWhiteSpace(message) ? "Đã xảy ra lỗi." : message.Trim();
}

public sealed record ReceiptTemplateKindOption(
    string Code,
    string Label,
    string DefaultFileName,
    string DisplayName)
{
    public override string ToString() => Label;
}
