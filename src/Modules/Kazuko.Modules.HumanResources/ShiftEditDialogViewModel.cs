using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

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

    public ShiftEditDialogViewModel(
        IHumanResourcesRepository repository,
        ICurrentSessionAccessor sessionAccessor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;

        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsSaving);
        CancelCommand = new RelayCommand(() => RequestClose?.Invoke(null), () => !IsSaving);
    }

    public event Action<WorkShiftItem?>? RequestClose;

    public string DialogTitle => "Thêm ca làm việc";

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
                ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    private async Task SaveAsync()
    {
        if (IsSaving)
            return;

        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Name))
        {
            ErrorMessage = "Tên ca là bắt buộc.";
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

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        IsSaving = true;
        try
        {
            var saved = await _repository.SaveWorkShiftAsync(
                new WorkShiftSaveRequest(null, Name.Trim(), startTime, endTime),
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
