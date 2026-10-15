using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierEditDialogViewModel : ObservableObject
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static readonly string[] RegionOptions =
    [
        "TP. Hồ Chí Minh",
        "Hà Nội",
        "Đà Nẵng",
        "Cần Thơ",
        "Hải Phòng",
        "Bình Dương",
        "Đồng Nai",
        "Khác"
    ];

    private readonly ISupplierRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly long? _supplierId;
    private bool _isBusy;
    private bool _isLoading;
    private string _title = "THÊM NHÀ CUNG CẤP";
    private string _codeDisplay = "(Tự sinh)";
    private string _name = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _taxCode = string.Empty;
    private string _address = string.Empty;
    private string _notes = string.Empty;
    private string? _region;
    private string _contactName = string.Empty;
    private string _contactTitle = string.Empty;
    private string _brandCode = string.Empty;
    private string _tccs = string.Empty;
    private SupplierGroupOption? _selectedGroup;
    private bool _isActive = true;
    private string _nameError = string.Empty;
    private string _phoneError = string.Empty;
    private string _emailError = string.Empty;
    private string _groupError = string.Empty;
    private string _formError = string.Empty;

    public SupplierEditDialogViewModel(
        ISupplierRepository repository,
        ICurrentSessionAccessor sessionAccessor,
        long? supplierId)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _supplierId = supplierId;

        Regions.Add(string.Empty);
        foreach (var region in RegionOptions)
            Regions.Add(region);

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && !IsLoading);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null), () => !IsBusy);

        _ = InitializeAsync();
    }

    public event Action<SupplierEditResult?>? RequestClose;

    public ObservableCollection<SupplierGroupOption> Groups { get; } = new();
    public ObservableCollection<string> Regions { get; } = new();

    public bool IsCreate => _supplierId is null or 0;
    public bool IsCodeReadOnly => !IsCreate;

    public string Title
    {
        get => _title;
        private set => SetProperty(ref _title, value);
    }

    public string CodeDisplay
    {
        get => _codeDisplay;
        private set => SetProperty(ref _codeDisplay, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string Email
    {
        get => _email;
        set => SetProperty(ref _email, value);
    }

    public string TaxCode
    {
        get => _taxCode;
        set => SetProperty(ref _taxCode, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string? Region
    {
        get => _region;
        set => SetProperty(ref _region, value);
    }

    public string ContactName
    {
        get => _contactName;
        set => SetProperty(ref _contactName, value);
    }

    public string ContactTitle
    {
        get => _contactTitle;
        set => SetProperty(ref _contactTitle, value);
    }

    public string BrandCode
    {
        get => _brandCode;
        set => SetProperty(ref _brandCode, value);
    }

    public string Tccs
    {
        get => _tccs;
        set => SetProperty(ref _tccs, value);
    }

    public SupplierGroupOption? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CommandManager.InvalidateRequerySuggested();
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsFormEnabled));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                CommandManager.InvalidateRequerySuggested();
                OnPropertyChanged(nameof(IsFormEnabled));
            }
        }
    }

    public bool IsFormEnabled => !IsBusy && !IsLoading;

    public string NameError
    {
        get => _nameError;
        private set => SetProperty(ref _nameError, value);
    }

    public string PhoneError
    {
        get => _phoneError;
        private set => SetProperty(ref _phoneError, value);
    }

    public string EmailError
    {
        get => _emailError;
        private set => SetProperty(ref _emailError, value);
    }

    public string GroupError
    {
        get => _groupError;
        private set => SetProperty(ref _groupError, value);
    }

    public string FormError
    {
        get => _formError;
        private set => SetProperty(ref _formError, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        FormError = string.Empty;

        try
        {
            var groups = await _repository.GetGroupsAsync(activeOnly: false);
            Groups.Clear();
            foreach (var group in groups)
                Groups.Add(group);

            if (_supplierId is long id and > 0)
            {
                Title = "SỬA NHÀ CUNG CẤP";
                var detail = await _repository.GetByIdAsync(id);
                if (detail is null)
                {
                    FormError = "Không tìm thấy thông tin nhà cung cấp.";
                    return;
                }

                CodeDisplay = detail.Code;
                Name = detail.Name;
                Phone = detail.Phone;
                Email = detail.Email;
                TaxCode = detail.TaxCode;
                Address = detail.Address;
                Notes = detail.Notes;
                Region = string.IsNullOrWhiteSpace(detail.Region) ? null : detail.Region;
                ContactName = detail.ContactName;
                ContactTitle = detail.ContactTitle;
                BrandCode = detail.BrandCode;
                Tccs = detail.Tccs;
                IsActive = detail.IsActive;

                SelectedGroup = Groups.FirstOrDefault(g => g.Id == detail.SupplierGroupId) ?? Groups.FirstOrDefault();
            }
            else
            {
                Title = "THÊM NHÀ CUNG CẤP";
                CodeDisplay = "(Tự sinh)";
                IsActive = true;
                SelectedGroup = Groups.FirstOrDefault(g => g.IsDefault && g.IsActive)
                    ?? Groups.FirstOrDefault(g => g.IsActive)
                    ?? Groups.FirstOrDefault();
            }
        }
        catch (Exception)
        {
            FormError = "Không thể tải dữ liệu nhà cung cấp.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (!ValidateLocal())
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            FormError = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        var request = BuildRequest();
        try
        {
            var warning = await _repository.FindDuplicateWarningAsync(request);
            if (!string.IsNullOrWhiteSpace(warning))
            {
                var continueSave = MessageBox.Show(
                    warning + Environment.NewLine + Environment.NewLine + "Bạn có muốn tiếp tục lưu?",
                    "Cảnh báo trùng dữ liệu",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (continueSave != MessageBoxResult.Yes)
                    return;
            }
        }
        catch (Exception)
        {
            FormError = "Không thể kiểm tra dữ liệu trùng.";
            return;
        }

        IsBusy = true;
        FormError = string.Empty;
        try
        {
            var result = await _repository.SaveAsync(request, session);
            RequestClose?.Invoke(result);
        }
        catch (InvalidOperationException ex)
        {
            FormError = Sanitize(ex.Message);
        }
        catch (Exception)
        {
            FormError = "Không thể lưu nhà cung cấp. Vui lòng thử lại.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateLocal()
    {
        NameError = string.Empty;
        PhoneError = string.Empty;
        EmailError = string.Empty;
        GroupError = string.Empty;
        FormError = string.Empty;

        var ok = true;
        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Tên nhà cung cấp là bắt buộc.";
            ok = false;
        }

        if (string.IsNullOrWhiteSpace(Phone))
        {
            PhoneError = "Số điện thoại là bắt buộc.";
            ok = false;
        }

        if (SelectedGroup is null || SelectedGroup.Id <= 0 || !SelectedGroup.IsActive)
        {
            GroupError = "Phải chọn nhóm nhà cung cấp đang hoạt động.";
            ok = false;
        }

        var email = Email.Trim();
        if (email.Length > 0 && !EmailRegex.IsMatch(email))
        {
            EmailError = "Email không đúng định dạng.";
            ok = false;
        }

        return ok;
    }

    private SupplierSaveRequest BuildRequest() =>
        new(
            _supplierId,
            Name,
            SelectedGroup?.Id ?? 0,
            ContactName,
            ContactTitle,
            Phone,
            Email,
            Address,
            Region ?? string.Empty,
            TaxCode,
            Notes,
            IsActive,
            BrandCode,
            Tccs);

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Không thể lưu nhà cung cấp.";
        if (message.Contains("SQL", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Firebird", StringComparison.OrdinalIgnoreCase) ||
            message.Contains('\\') ||
            message.Contains(".fdb", StringComparison.OrdinalIgnoreCase))
            return "Không thể lưu nhà cung cấp. Vui lòng thử lại.";
        return message;
    }
}
