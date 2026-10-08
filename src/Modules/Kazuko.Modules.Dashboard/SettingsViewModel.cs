using System.Windows.Input;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Mvvm;
using Microsoft.Win32;

namespace Kazuko.Modules.Dashboard;

public sealed record SettingsCategory(string Key, string Label);

public sealed class SettingsViewModel : ObservableObject
{
    private readonly ICompanyProfileService _companyProfile;
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

    public SettingsViewModel(
        ICompanyProfileService companyProfile,
        ICurrentSessionAccessor sessionAccessor,
        IAppearanceService appearance)
    {
        _companyProfile = companyProfile ?? throw new ArgumentNullException(nameof(companyProfile));
        _sessionAccessor = sessionAccessor ?? throw new ArgumentNullException(nameof(sessionAccessor));
        Appearance = new AppearanceSettingsViewModel(
            appearance ?? throw new ArgumentNullException(nameof(appearance)));

        Categories =
        [
            new SettingsCategory("general", "Thông tin chung"),
            new SettingsCategory("appearance", "Giao diện")
        ];
        _selectedCategory = Categories[0];

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && IsGeneralSelected);
        ReloadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy && IsGeneralSelected);
        PickLogoCommand = new RelayCommand(PickLogo, () => !IsBusy && IsGeneralSelected);
        ClearLogoCommand = new RelayCommand(ClearLogo, () => !IsBusy && IsGeneralSelected && HasLogo);
        SelectCategoryCommand = new RelayCommand<SettingsCategory>(SelectCategory);

        _ = LoadAsync();
    }

    public IReadOnlyList<SettingsCategory> Categories { get; }

    public AppearanceSettingsViewModel Appearance { get; }

    public SettingsCategory SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is null || !SetProperty(ref _selectedCategory, value))
                return;
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsAppearanceSelected));
            NotifyCommands();
        }
    }

    public bool IsGeneralSelected =>
        string.Equals(SelectedCategory.Key, "general", StringComparison.Ordinal);

    public bool IsAppearanceSelected =>
        string.Equals(SelectedCategory.Key, "appearance", StringComparison.Ordinal);

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

    public ICommand SaveCommand { get; }
    public ICommand ReloadCommand { get; }
    public ICommand PickLogoCommand { get; }
    public ICommand ClearLogoCommand { get; }
    public ICommand SelectCategoryCommand { get; }

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
        var dialog = new OpenFileDialog
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
    }

    private static string Sanitize(string message) =>
        string.IsNullOrWhiteSpace(message) ? "Đã xảy ra lỗi." : message.Trim();
}
