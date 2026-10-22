using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Microsoft.Win32;

namespace Kazuko.Modules.HumanResources;

public sealed class EmployeeEditDialogViewModel : ObservableObject
{
    private static readonly CultureInfo MoneyCulture = CultureInfo.GetCultureInfo("vi-VN");

    public static readonly string[] GenderOptions = ["", "Nam", "Nữ", "Khác"];
    public static readonly string[] ContractTypeOptions =
    [
        "",
        "Thử việc",
        "Chính thức",
        "Thời vụ",
        "Cộng tác viên"
    ];

    public static readonly (string Code, string Label)[] StatusChoices =
    [
        (EmployeeStatusCodes.Working, "Đang làm việc"),
        (EmployeeStatusCodes.OnLeave, "Nghỉ phép"),
        (EmployeeStatusCodes.Terminated, "Ngừng làm việc")
    ];

    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly long? _employeeId;
    private bool _isBusy;
    private bool _isLoading;
    private string _title = "THÊM NHÂN VIÊN";
    private string _codeDisplay = "(Tự sinh)";
    private string _fullName = string.Empty;
    private string? _photoPath;
    private string? _gender;
    private DateTime? _dateOfBirth;
    private string _attendanceCode = string.Empty;
    private string _phone = string.Empty;
    private string _email = string.Empty;
    private string _address = string.Empty;
    private DateTime? _hireDate = DateTime.Today;
    private string? _contractType;
    private string _salaryMethod = SalaryMethodCodes.MonthlyByDay;
    private string _shiftSalaryText = "0";
    private string _monthlySalaryText = "0";
    private bool _restOnSunday = true;
    private bool _restOnSaturday;
    private string _notes = string.Empty;
    private string _selectedStatusCode = EmployeeStatusCodes.Working;
    private bool _isActive = true;
    private string _fullNameError = string.Empty;
    private string _emailError = string.Empty;
    private string _salaryError = string.Empty;
    private string _formError = string.Empty;

    public EmployeeEditDialogViewModel(
        IHumanResourcesRepository repository,
        ICurrentSessionAccessor sessionAccessor,
        long? employeeId)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _employeeId = employeeId;

        foreach (var g in GenderOptions)
            Genders.Add(g);
        foreach (var c in ContractTypeOptions)
            ContractTypes.Add(c);
        foreach (var s in StatusChoices)
            StatusLabels.Add(s.Label);

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && !IsLoading);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null), () => !IsBusy);
        PickPhotoCommand = new RelayCommand(PickPhoto, () => !IsBusy && !IsLoading);
        ClearPhotoCommand = new RelayCommand(ClearPhoto, () => !IsBusy && !IsLoading && HasPhoto);

        _ = InitializeAsync();
    }

    public event Action<EmployeeEditResult?>? RequestClose;

    public ObservableCollection<string> Genders { get; } = new();
    public ObservableCollection<string> ContractTypes { get; } = new();
    public ObservableCollection<string> StatusLabels { get; } = new();

    public bool IsCreate => _employeeId is null or 0;
    public bool IsFormEnabled => !IsBusy && !IsLoading;

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

    public string FullName
    {
        get => _fullName;
        set => SetProperty(ref _fullName, value);
    }

    public string? PhotoPath
    {
        get => _photoPath;
        set
        {
            if (SetProperty(ref _photoPath, value))
            {
                OnPropertyChanged(nameof(HasPhoto));
                ((RelayCommand)ClearPhotoCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoPath);

    public string? Gender
    {
        get => _gender;
        set => SetProperty(ref _gender, value);
    }

    public DateTime? DateOfBirth
    {
        get => _dateOfBirth;
        set => SetProperty(ref _dateOfBirth, value);
    }

    public string AttendanceCode
    {
        get => _attendanceCode;
        set => SetProperty(ref _attendanceCode, value);
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

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public DateTime? HireDate
    {
        get => _hireDate;
        set => SetProperty(ref _hireDate, value);
    }

    public string? ContractType
    {
        get => _contractType;
        set => SetProperty(ref _contractType, value);
    }

    /// <summary>Mã cách tính lương đang chọn (PER_SHIFT / MONTHLY_BY_SHIFT / MONTHLY_BY_DAY).</summary>
    public string SalaryMethod
    {
        get => _salaryMethod;
        private set
        {
            if (SetProperty(ref _salaryMethod, value))
            {
                OnPropertyChanged(nameof(IsSalaryPerShift));
                OnPropertyChanged(nameof(IsSalaryMonthlyByShift));
                OnPropertyChanged(nameof(IsSalaryMonthlyByDay));
                OnPropertyChanged(nameof(IsMonthlySalaryEnabled));
            }
        }
    }

    public bool IsSalaryPerShift
    {
        get => _salaryMethod == SalaryMethodCodes.PerShift;
        set
        {
            if (value)
                SalaryMethod = SalaryMethodCodes.PerShift;
        }
    }

    public bool IsSalaryMonthlyByShift
    {
        get => _salaryMethod == SalaryMethodCodes.MonthlyByShift;
        set
        {
            if (value)
                SalaryMethod = SalaryMethodCodes.MonthlyByShift;
        }
    }

    public bool IsSalaryMonthlyByDay
    {
        get => _salaryMethod == SalaryMethodCodes.MonthlyByDay;
        set
        {
            if (value)
                SalaryMethod = SalaryMethodCodes.MonthlyByDay;
        }
    }

    /// <summary>Ô Lương tháng dùng cho cả hai cách tính theo tháng.</summary>
    public bool IsMonthlySalaryEnabled => _salaryMethod != SalaryMethodCodes.PerShift;

    public string ShiftSalaryText
    {
        get => _shiftSalaryText;
        set => SetProperty(ref _shiftSalaryText, value);
    }

    public string MonthlySalaryText
    {
        get => _monthlySalaryText;
        set => SetProperty(ref _monthlySalaryText, value);
    }

    public bool RestOnSunday
    {
        get => _restOnSunday;
        set => SetProperty(ref _restOnSunday, value);
    }

    public bool RestOnSaturday
    {
        get => _restOnSaturday;
        set => SetProperty(ref _restOnSaturday, value);
    }

    /// <summary>Định dạng lại lương ca/lương tháng với dấu ngăn nghìn sau LostFocus.</summary>
    public void FormatSalaryTexts()
    {
        FormatMoneyField(ref _shiftSalaryText, nameof(ShiftSalaryText));
        FormatMoneyField(ref _monthlySalaryText, nameof(MonthlySalaryText));
    }

    private void FormatMoneyField(ref string field, string propertyName)
    {
        if (!TryParseMoney(field, out var value))
            return;

        var formatted = FormatMoney(value);
        if (!string.Equals(field, formatted, StringComparison.Ordinal))
        {
            field = formatted;
            OnPropertyChanged(propertyName);
        }
    }

    public string Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string SelectedStatusLabel
    {
        get => StatusChoices.FirstOrDefault(s => s.Code == _selectedStatusCode).Label
               ?? "Đang làm việc";
        set
        {
            var match = StatusChoices.FirstOrDefault(s => s.Label == value);
            if (!string.IsNullOrEmpty(match.Code))
            {
                _selectedStatusCode = match.Code;
                IsActive = match.Code != EmployeeStatusCodes.Terminated;
                OnPropertyChanged();
            }
        }
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
                OnPropertyChanged(nameof(IsFormEnabled));
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
                ((RelayCommand)PickPhotoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearPhotoCommand).NotifyCanExecuteChanged();
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
                OnPropertyChanged(nameof(IsFormEnabled));
                ((RelayCommand)PickPhotoCommand).NotifyCanExecuteChanged();
                ((RelayCommand)ClearPhotoCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string FullNameError
    {
        get => _fullNameError;
        private set => SetProperty(ref _fullNameError, value);
    }

    public string EmailError
    {
        get => _emailError;
        private set => SetProperty(ref _emailError, value);
    }

    public string SalaryError
    {
        get => _salaryError;
        private set => SetProperty(ref _salaryError, value);
    }

    public string FormError
    {
        get => _formError;
        private set => SetProperty(ref _formError, value);
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand PickPhotoCommand { get; }
    public ICommand ClearPhotoCommand { get; }

    private void PickPhoto()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn ảnh nhân viên",
            Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|Tất cả|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var previous = PhotoPath;
            var imported = EmployeePhotoStore.Import(dialog.FileName, CodeDisplay);
            PhotoPath = imported;
            if (!string.Equals(previous, imported, StringComparison.OrdinalIgnoreCase))
                EmployeePhotoStore.TryDeleteManagedFile(previous);
        }
        catch (Exception ex)
        {
            FormError = Sanitize(ex.Message);
        }
    }

    private void ClearPhoto()
    {
        var previous = PhotoPath;
        PhotoPath = null;
        EmployeePhotoStore.TryDeleteManagedFile(previous);
    }

    private async Task InitializeAsync()
    {
        IsLoading = true;
        FormError = string.Empty;
        try
        {
            if (_employeeId is long id and > 0)
            {
                Title = "SỬA NHÂN VIÊN";
                var detail = await _repository.GetByIdAsync(id);
                if (detail is null)
                {
                    FormError = "Không tìm thấy hồ sơ nhân viên.";
                    return;
                }

                CodeDisplay = detail.Code;
                FullName = detail.FullName;
                PhotoPath = detail.PhotoPath;
                Gender = detail.Gender;
                DateOfBirth = detail.DateOfBirth;
                AttendanceCode = detail.AttendanceCode;
                Phone = detail.Phone;
                Email = detail.Email;
                Address = detail.Address;
                HireDate = detail.HireDate;
                ContractType = detail.ContractType;
                SalaryMethod = SalaryMethodCodes.IsValid(detail.SalaryMethod)
                    ? detail.SalaryMethod
                    : SalaryMethodCodes.MonthlyByDay;
                ShiftSalaryText = FormatMoney(detail.ShiftSalary);
                MonthlySalaryText = FormatMoney(detail.MonthlySalary);
                RestOnSunday = detail.RestOnSunday;
                RestOnSaturday = detail.RestOnSaturday;
                Notes = detail.Notes;
                _selectedStatusCode = detail.StatusCode;
                IsActive = detail.IsActive;
                OnPropertyChanged(nameof(SelectedStatusLabel));
            }
            else
            {
                Title = "THÊM NHÂN VIÊN";
                CodeDisplay = "(Tự sinh)";
                AttendanceCode = await _repository.SuggestNextAttendanceCodeAsync();
            }
        }
        catch
        {
            FormError = "Không thể tải dữ liệu nhân viên.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task SaveAsync()
    {
        if (!ValidateLocal(out var request))
            return;

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            FormError = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        IsBusy = true;
        FormError = string.Empty;
        try
        {
            if (!string.IsNullOrWhiteSpace(request.AttendanceCode) &&
                await _repository.IsAttendanceCodeTakenAsync(request.AttendanceCode, request.Id))
            {
                FormError = "Mã chấm công đã tồn tại.";
                return;
            }

            var result = await _repository.SaveAsync(request, session);
            RequestClose?.Invoke(result);
        }
        catch (InvalidOperationException ex)
        {
            FormError = Sanitize(ex.Message);
        }
        catch (Exception ex)
        {
            FormError = MapSaveFailure(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool ValidateLocal(out EmployeeSaveRequest request)
    {
        FullNameError = string.Empty;
        EmailError = string.Empty;
        SalaryError = string.Empty;
        FormError = string.Empty;

        decimal shiftSalary = 0;
        decimal monthlySalary = 0;
        var ok = true;

        if (string.IsNullOrWhiteSpace(FullName))
        {
            FullNameError = "Họ và tên là bắt buộc.";
            ok = false;
        }

        if (!TryParseMoney(ShiftSalaryText, out shiftSalary) || shiftSalary < 0)
        {
            SalaryError = string.IsNullOrEmpty(SalaryError)
                ? "Lương ca không hợp lệ hoặc âm."
                : SalaryError + " Lương ca không hợp lệ hoặc âm.";
            ok = false;
        }

        if (!TryParseMoney(MonthlySalaryText, out monthlySalary) || monthlySalary < 0)
        {
            SalaryError = string.IsNullOrEmpty(SalaryError)
                ? "Lương tháng không hợp lệ hoặc âm."
                : SalaryError + " Lương tháng không hợp lệ hoặc âm.";
            ok = false;
        }

        request = new EmployeeSaveRequest(
            _employeeId,
            FullName?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(PhotoPath) ? null : PhotoPath.Trim(),
            Gender?.Trim() ?? string.Empty,
            DateOfBirth,
            AttendanceCode?.Trim() ?? string.Empty,
            Phone?.Trim() ?? string.Empty,
            Email?.Trim() ?? string.Empty,
            Address?.Trim() ?? string.Empty,
            HireDate,
            ContractType?.Trim() ?? string.Empty,
            Notes?.Trim() ?? string.Empty,
            _selectedStatusCode,
            IsActive,
            SalaryMethod,
            shiftSalary,
            monthlySalary,
            RestOnSunday,
            RestOnSaturday);

        var shared = EmployeeValidator.Validate(request);
        if (shared is not null)
        {
            if (shared.Contains("Email", StringComparison.OrdinalIgnoreCase))
                EmailError = shared;
            else
                FormError = shared;
            ok = false;
        }

        return ok;
    }

    private static string FormatMoney(decimal value) =>
        value.ToString("N0", MoneyCulture);

    private static bool TryParseMoney(string? text, out decimal value)
    {
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            value = 0;
            return true;
        }

        return decimal.TryParse(raw, NumberStyles.Number, MoneyCulture, out value)
               || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
               || decimal.TryParse(raw.Replace(",", "").Replace(".", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Có lỗi xảy ra.";
        return message.Length > 300 ? message[..300] + "…" : message;
    }

    private static string MapSaveFailure(Exception ex)
    {
        var text = ex.Message ?? string.Empty;
        if (text.Contains("HR_EMPLOYEES", StringComparison.OrdinalIgnoreCase) &&
            (text.Contains("Table unknown", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("not exist", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("does not exist", StringComparison.OrdinalIgnoreCase)))
        {
            return "Schema nhân sự chưa sẵn sàng. Vui lòng chạy migration 31–32 rồi thử lại.";
        }

        return "Không thể lưu nhân viên. Vui lòng thử lại.";
    }
}
