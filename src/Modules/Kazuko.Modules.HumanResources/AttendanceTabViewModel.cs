using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.HumanResources;

/// <summary>Một kỳ chấm công đã lưu, kể cả kỳ chưa có ô chấm công.</summary>
public sealed class AttendancePeriodViewModel
{
    public AttendancePeriodViewModel(int year, int month, bool hasData)
    {
        Year = year;
        Month = month;
        HasData = hasData;
    }

    public int Year { get; }
    public int Month { get; }

    /// <summary>Kỳ đã có record chấm công (không cho xóa kỳ).</summary>
    public bool HasData { get; set; }

    public string DisplayName => AttendanceCalendar.PeriodDisplayName(Year, Month);

    public override string ToString() => DisplayName;
}

/// <summary>
/// Tab Chấm công: lưới nhân viên × ca × ngày trong tháng.
/// Chọn ô bằng click/kéo thả, gán trạng thái hàng loạt qua toolbar.
/// </summary>
public sealed class AttendanceTabViewModel : ObservableObject
{
    private readonly IHumanResourcesRepository _repository;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IShiftEditorService _shiftEditor;

    private readonly List<AttendanceRowViewModel> _flatRows = new();
    private readonly HashSet<AttendanceCellViewModel> _selection = new();
    private HashSet<AttendanceCellViewModel> _dragBase = new();
    private AttendanceCellViewModel? _dragAnchor;
    private CancellationTokenSource? _loadCts;

    private bool _loaded;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private AttendancePeriodViewModel? _selectedPeriod;
    private int _selectedCount;

    public AttendanceTabViewModel(
        IHumanResourcesRepository repository,
        ICurrentSessionAccessor sessionAccessor,
        IShiftEditorService shiftEditor)
    {
        _repository = repository;
        _sessionAccessor = sessionAccessor;
        _shiftEditor = shiftEditor;

        AddPeriodCommand = new AsyncRelayCommand(AddPeriodAsync, () => !IsLoading);
        DeletePeriodCommand = new AsyncRelayCommand(DeletePeriodAsync, () => !IsLoading && SelectedPeriod is not null);
        AddShiftCommand = new RelayCommand(AddShift, () => !IsLoading);
        RefreshCommand = new AsyncRelayCommand(() => LoadPeriodsAsync(), () => !IsLoading);
        SetStatusCommand = new RelayCommand<string>(
            status => _ = SetStatusAsync(status ?? AttendanceStatusCodes.None),
            _ => !IsLoading && SelectedCount > 0);
    }

    public ObservableCollection<AttendancePeriodViewModel> Periods { get; } = new();
    public ObservableCollection<AttendanceDayHeader> Days { get; } = new();
    public ObservableCollection<AttendanceEmployeeGroupViewModel> Groups { get; } = new();

    public AttendancePeriodViewModel? SelectedPeriod
    {
        get => _selectedPeriod;
        set
        {
            if (SetProperty(ref _selectedPeriod, value))
            {
                ((AsyncRelayCommand)DeletePeriodCommand).NotifyCanExecuteChanged();
                if (_loaded)
                    _ = LoadGridAsync();
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
                ((AsyncRelayCommand)AddPeriodCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)DeletePeriodCommand).NotifyCanExecuteChanged();
                ((RelayCommand)AddShiftCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)RefreshCommand).NotifyCanExecuteChanged();
                ((RelayCommand<string>)SetStatusCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public int SelectedCount
    {
        get => _selectedCount;
        private set
        {
            if (SetProperty(ref _selectedCount, value))
            {
                OnPropertyChanged(nameof(SelectionText));
                ((RelayCommand<string>)SetStatusCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string SelectionText =>
        SelectedCount > 0 ? $"Đã chọn {SelectedCount} ô" : "Chưa chọn ô nào";

    public bool HasRows => Groups.Count > 0;

    public ICommand AddPeriodCommand { get; }
    public ICommand DeletePeriodCommand { get; }
    public ICommand AddShiftCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand SetStatusCommand { get; }

    // ------------------------------------------------------------------
    // Chọn ô bằng chuột (code-behind gọi các hàm này)
    // ------------------------------------------------------------------

    /// <summary>Bắt đầu kéo chọn từ một ô. additive = giữ vùng chọn cũ (Ctrl).</summary>
    public void BeginDragSelect(AttendanceCellViewModel cell, bool additive)
    {
        _dragAnchor = cell;
        _dragBase = additive
            ? new HashSet<AttendanceCellViewModel>(_selection)
            : new HashSet<AttendanceCellViewModel>();
        ApplyDragSelection(cell);
    }

    /// <summary>Cập nhật vùng chọn khi kéo chuột sang ô khác.</summary>
    public void UpdateDragSelect(AttendanceCellViewModel cell)
    {
        if (_dragAnchor is null)
            return;
        ApplyDragSelection(cell);
    }

    /// <summary>Kết thúc kéo chọn.</summary>
    public void EndDragSelect() => _dragAnchor = null;

    public void ClearSelection()
    {
        foreach (var cell in _selection)
            cell.IsSelected = false;
        _selection.Clear();
        SelectedCount = 0;
    }

    private void ApplyDragSelection(AttendanceCellViewModel current)
    {
        var anchor = _dragAnchor!;
        var rowFrom = Math.Min(anchor.RowIndex, current.RowIndex);
        var rowTo = Math.Max(anchor.RowIndex, current.RowIndex);
        var dayFrom = Math.Min(anchor.DayIndex, current.DayIndex);
        var dayTo = Math.Max(anchor.DayIndex, current.DayIndex);

        var next = new HashSet<AttendanceCellViewModel>(_dragBase);
        for (var r = rowFrom; r <= rowTo && r < _flatRows.Count; r++)
        {
            var cells = _flatRows[r].Cells;
            for (var d = dayFrom; d <= dayTo && d < cells.Count; d++)
                next.Add(cells[d]);
        }

        foreach (var cell in _selection)
        {
            if (!next.Contains(cell))
                cell.IsSelected = false;
        }

        foreach (var cell in next)
            cell.IsSelected = true;

        _selection.Clear();
        foreach (var cell in next)
            _selection.Add(cell);
        SelectedCount = _selection.Count;
    }

    // ------------------------------------------------------------------
    // Tải dữ liệu
    // ------------------------------------------------------------------

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : LoadPeriodsAsync();

    private static string FailureText(Exception ex, string fallback) =>
        ex is InvalidOperationException or UnauthorizedAccessException ? ex.Message : fallback;

    private async Task LoadPeriodsAsync(int? selectYear = null, int? selectMonth = null)
    {
        var selectedYear = selectYear ?? SelectedPeriod?.Year;
        var selectedMonth = selectMonth ?? SelectedPeriod?.Month;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var months = await _repository.GetAttendanceMonthsAsync();
            Periods.Clear();
            foreach (var month in months.OrderByDescending(m => m.Year).ThenByDescending(m => m.Month))
                Periods.Add(new AttendancePeriodViewModel(month.Year, month.Month, month.HasData));
            _selectedPeriod = Periods.FirstOrDefault(p => p.Year == selectedYear && p.Month == selectedMonth)
                              ?? Periods.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedPeriod));
            ((AsyncRelayCommand)DeletePeriodCommand).NotifyCanExecuteChanged();
            _loaded = true;
            if (_selectedPeriod is null)
            {
                ClearSelection();
                Groups.Clear(); Days.Clear(); _flatRows.Clear();
                OnPropertyChanged(nameof(HasRows));
                StatusMessage = "Chưa có kỳ chấm công. Bấm 'Thêm kỳ tháng' để tạo kỳ hiện tại.";
            }
        }
        catch (Exception ex)
        {
            _loaded = false;
            ErrorMessage = FailureText(ex, "Không thể tải danh sách kỳ chấm công. Vui lòng thử Làm mới hoặc kiểm tra kết nối dữ liệu.");
            return;
        }
        finally
        {
            IsLoading = false;
        }
        await LoadGridAsync();
    }

    private async Task LoadGridAsync()
    {
        var period = SelectedPeriod;
        if (period is null)
            return;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Đang tải lưới chấm công...";
        ClearSelection();
        EndDragSelect();

        try
        {
            var employees = await _repository.SearchAsync(
                new EmployeeSearchFilter(string.Empty, string.Empty), token);
            var allShifts = await _repository.GetWorkShiftsAsync(false, token);
            var records = await _repository.GetAttendanceRecordsAsync(period.Year, period.Month, token);
            var recordedShiftIds = records.Select(x => x.ShiftId).ToHashSet();
            var shifts = allShifts.Where(x => x.IsActive || recordedShiftIds.Contains(x.Id)).ToList();
            if (token.IsCancellationRequested)
                return;

            var days = AttendanceCalendar.BuildDays(period.Year, period.Month);
            Days.Clear();
            foreach (var day in days)
                Days.Add(day);

            var activeEmployees = employees.Where(e => e.IsActive).ToList();
            var groups = AttendanceGridBuilder.Build(activeEmployees, shifts, records, days);

            Groups.Clear();
            _flatRows.Clear();
            foreach (var group in groups)
            {
                Groups.Add(group);
                _flatRows.AddRange(group.ShiftRows);
            }

            OnPropertyChanged(nameof(HasRows));
            StatusMessage = activeEmployees.Count == 0
                ? "Chưa có nhân viên đang làm việc."
                : shifts.Count == 0
                    ? "Chưa có ca làm việc. Bấm \"Danh mục ca\" để tạo ca."
                    : $"Kỳ {period.DisplayName}: {activeEmployees.Count} nhân viên × {shifts.Count} ca.";
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            ErrorMessage = FailureText(ex, "Không thể tải lưới chấm công. Vui lòng Làm mới hoặc kiểm tra kết nối dữ liệu.");
            StatusMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    // ------------------------------------------------------------------
    // Thao tác
    // ------------------------------------------------------------------

    private async Task SetStatusAsync(string statusCode)
    {
        if (_selection.Count == 0)
        {
            StatusMessage = "Hãy chọn ít nhất một ô trước khi gán trạng thái.";
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return;
        }

        var cells = _selection.ToList();
        var entries = cells
            .Select(c => new AttendanceEntry(c.EmployeeId, c.ShiftId, c.Date))
            .ToList();

        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            await _repository.SetAttendanceStatusAsync(entries, statusCode, session);
            foreach (var cell in cells)
                cell.StatusCode = statusCode;

            StatusMessage = $"Đã đặt '{AttendanceStatusCodes.ToDisplay(statusCode)}' cho {cells.Count} ô.";

            // Kỳ vừa có dữ liệu: đánh dấu lại để không cho xóa kỳ.
            if (SelectedPeriod is { HasData: false } period && AttendanceStatusCodes.IsStored(statusCode))
                period.HasData = true;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
        }
        catch
        {
            ErrorMessage = "Không thể lưu chấm công. Vui lòng thử lại.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task AddPeriodAsync()
    {
        IsLoading = true;
        ErrorMessage = string.Empty;
        DateTime next;
        try
        {
            var now = DateTime.Today;
            next = Periods.Count > 0
                ? Periods.Max(p => new DateTime(p.Year, p.Month, 1)).AddMonths(1)
                : new DateTime(now.Year, now.Month, 1);
            var session = _sessionAccessor.Current ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
            await _repository.CreateAttendanceMonthAsync(next.Year, next.Month, session);
        }
        catch (Exception ex)
        {
            ErrorMessage = FailureText(ex, "Không thể lưu kỳ chấm công. Vui lòng thử lại.");
            return;
        }
        finally { IsLoading = false; }
        await LoadPeriodsAsync(next.Year, next.Month);
    }

    private async Task DeletePeriodAsync()
    {
        var period = SelectedPeriod;
        if (period is null) return;
        IsLoading = true;
        ErrorMessage = string.Empty;
        try
        {
            var session = _sessionAccessor.Current ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.");
            await _repository.DeleteAttendanceMonthAsync(period.Year, period.Month, session);
        }
        catch (Exception ex)
        {
            ErrorMessage = FailureText(ex, "Không thể xóa kỳ chấm công. Vui lòng thử lại.");
            return;
        }
        finally { IsLoading = false; }
        await LoadPeriodsAsync();
    }

    private void AddShift()
    {
        try
        {
            _shiftEditor.ShowManage();
            _ = LoadGridAsync();
        }
        catch (Exception ex) { ErrorMessage = FailureText(ex, "Không mở được danh mục ca làm việc."); }
    }
}
