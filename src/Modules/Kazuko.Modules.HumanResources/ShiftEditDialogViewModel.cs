using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.HumanResources;

public sealed class ShiftEditDialogViewModel : ObservableObject
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private string _name = string.Empty;
    private string _startTimeText = string.Empty;
    private string _endTimeText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isSaving;
    private bool _isActive = true;
    private readonly long? _id;
    private readonly IAuthorizationService? _permissions;

    public ShiftEditDialogViewModel(
        IHumanResourcesRepository repository,
        ICurrentSessionAccessor sessionAccessor, WorkShiftItem? item = null, IAuthorizationService? permissions = null)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _permissions = permissions;
        _id = item?.Id;
        if (item is not null)
        {
            _name = item.Name;
            _startTimeText = item.StartTime?.ToString(@"hh\:mm") ?? string.Empty;
            _endTimeText = item.EndTime?.ToString(@"hh\:mm") ?? string.Empty;
            _isActive = item.IsActive;
        }

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => CanEdit);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null), () => !IsSaving);
    }

    public event Action<WorkShiftItem?>? RequestClose;

    public string DialogTitle => _id is null ? "Thêm ca làm việc" : "Sửa ca làm việc";
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }
    public bool CanEdit => !IsSaving && (_permissions?.Current.Allows("CAT_EMPLOYEES", _id is null ? PermissionAction.Add : PermissionAction.Edit) ?? true);

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    /// <summary>Giờ bắt đầu dạng "HH:mm" (để trống nếu không đặt).</summary>
    public string StartTimeText
    {
        get => _startTimeText;
        set => SetProperty(ref _startTimeText, value);
    }

    /// <summary>Giờ kết thúc dạng "HH:mm" (để trống nếu không đặt).</summary>
    public string EndTimeText
    {
        get => _endTimeText;
        set => SetProperty(ref _endTimeText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public async Task SaveAsync()
    {
        if (IsSaving)
            return;

        ErrorMessage = string.Empty;
        if (!CanEdit) { ErrorMessage = "Bạn không có quyền lưu ca làm việc."; return; }
        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 50)
        {
            ErrorMessage = "Tên ca phải từ 1 đến 50 ký tự.";
            return;
        }

        if (!TryParseTime(StartTimeText, out var startTime))
        {
            ErrorMessage = "Giờ bắt đầu không đúng định dạng HH:mm.";
            return;
        }

        if (!TryParseTime(EndTimeText, out var endTime))
        {
            ErrorMessage = "Giờ kết thúc không đúng định dạng HH:mm.";
            return;
        }
        if (startTime is not null && startTime == endTime)
        {
            ErrorMessage = "Giờ bắt đầu và giờ kết thúc không được trùng nhau.";
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        IsSaving = true;
        try
        {
            if (_permissions is not null)
                await _permissions.DemandAsync([new("CAT_EMPLOYEES", _id is null ? PermissionAction.Add : PermissionAction.Edit)]);
            var saved = await _repository.SaveWorkShiftAsync(
                new WorkShiftSaveRequest(_id, Name.Trim(), startTime, endTime, IsActive),
                session);
            RequestClose?.Invoke(saved);
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch
        {
            ErrorMessage = "Không thể lưu ca làm việc. Vui lòng thử lại.";
        }
        finally
        {
            IsSaving = false;
        }
    }

    /// <summary>Parse "HH:mm" (hoặc "H:mm"); chuỗi rỗng nghĩa là không đặt giờ.</summary>
    public static bool TryParseTime(string? text, out TimeSpan? value)
    {
        var raw = (text ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            value = null;
            return true;
        }

        if (TimeSpan.TryParseExact(raw, @"h\:mm", CultureInfo.InvariantCulture, out var parsed) ||
            TimeSpan.TryParseExact(raw, @"hh\:mm", CultureInfo.InvariantCulture, out parsed))
        {
            if (parsed >= TimeSpan.Zero && parsed < TimeSpan.FromDays(1))
            {
                value = parsed;
                return true;
            }
        }

        value = null;
        return false;
    }
}
