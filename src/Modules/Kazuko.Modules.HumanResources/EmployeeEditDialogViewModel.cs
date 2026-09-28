using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Microsoft.Win32;

namespace Kazuko.Modules.HumanResources;

public sealed class EmployeeEditDialogViewModel : ObservableObject
{
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
    private string _baseSalaryText = "0";
    private string _allowanceText = "0";
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

    public string BaseSalaryText
    {
        get => _baseSalaryText;
        set => SetProperty(ref _baseSalaryText, value);
    }

    public string AllowanceText
    {
        get => _allowanceText;
        set => SetProperty(ref _allowanceText, value);
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
                BaseSalaryText = detail.BaseSalary.ToString("0.##");
                AllowanceText = detail.Allowance.ToString("0.##");
                Notes = detail.Notes;
                _selectedStatusCode = detail.StatusCode;
                IsActive = detail.IsActive;
                OnPropertyChanged(nameof(SelectedStatusLabel));
            }
            else
            {
                Title = "THÊM NHÂN VIÊN";
                CodeDisplay = "(Tự sinh)";
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

        decimal baseSalary = 0;
        decimal allowance = 0;
        var ok = true;

        if (string.IsNullOrWhiteSpace(FullName))
        {
            FullNameError = "Họ và tên là bắt buộc.";
            ok = false;
        }

        if (!decimal.TryParse(BaseSalaryText.Replace(",", ""), out baseSalary) || baseSalary < 0)
        {
            SalaryError = "Lương cơ bản không hợp lệ hoặc âm.";
            ok = false;
        }

        if (!decimal.TryParse(AllowanceText.Replace(",", ""), out allowance) || allowance < 0)
        {
            SalaryError = string.IsNullOrEmpty(SalaryError)
                ? "Phụ cấp không hợp lệ hoặc âm."
                : SalaryError + " Phụ cấp không hợp lệ hoặc âm.";
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
            baseSalary,
            allowance,
            Notes?.Trim() ?? string.Empty,
            _selectedStatusCode,
            IsActive);

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
