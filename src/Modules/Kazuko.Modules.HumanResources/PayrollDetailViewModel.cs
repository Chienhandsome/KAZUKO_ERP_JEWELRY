using System.Globalization;
using System.Text.Json;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.HumanResources;

public sealed record PayrollAttendanceDisplay(DateTime Date, string Shift, string Status);
public sealed class PayrollDetailViewModel : ObservableObject
{
    private readonly PayrollPeriod _period;
    private readonly bool _canAdjust;
    private readonly PayrollLine _line;
    private readonly Func<PayrollAdjustment, Task> _save;
    private string _bonus, _deduction, _bonusNote, _deductionNote, _error = "";
    private bool _busy;
    public PayrollDetailViewModel(PayrollPeriod period, PayrollLine line, Func<PayrollAdjustment, Task> save, bool canAdjust = true)
    {
        _period = period; _line = line; _save = save; _canAdjust = canAdjust;
        _bonus = line.Bonus.ToString("N0", CultureInfo.GetCultureInfo("vi-VN")); _deduction = line.Deduction.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        _bonusNote = line.BonusNote; _deductionNote = line.DeductionNote;
        var snapshot = JsonSerializer.Deserialize<PayrollSnapshot>(line.SnapshotJson) ?? throw new InvalidOperationException("Thiếu dữ liệu chi tiết kỳ lương.");
        Attendance = snapshot.Input.Attendance.Select(a => new PayrollAttendanceDisplay(a.WorkDate, a.ShiftName, AttendanceStatusCodes.ToDisplay(a.StatusCode))).ToArray();
        Description = $"{line.MethodLabel} • Công chuẩn: {line.StandardUnits:N0} • Công tính lương: {line.ActualUnits:N0}\n" +
            $"Đơn giá ca/tháng: {line.Rate:N0} VND • Lương tính được: {line.Salary:N0} VND\n" +
            (line.SalaryMethod == SalaryMethodCodes.PerShift ? $"{line.ActualUnits:N0} × {line.Rate:N0} = {line.Salary:N0} VND" : $"{line.ActualUnits:N0} × {line.Rate:N0} ÷ {line.StandardUnits:N0} = {line.Salary:N0} VND") +
            $"\nCa chuẩn/ngày: {snapshot.Settings.ShiftsPerDay}; nghỉ có phép: {(snapshot.Settings.IncludePaidLeave ? "có tính lương" : "không tính lương")}.\n" +
            $"Nghỉ tuần: CN {(snapshot.Input.Employee.RestOnSunday ? "có" : "không")}, T7 {(snapshot.Input.Employee.RestOnSaturday ? "có" : "không")}." +
            $"\nNgày vào làm: {snapshot.Input.Employee.HireDate:dd/MM/yyyy}; công trước ngày vào làm không tính lương." +
            $"\nKhoản đã ghi nhận: thưởng {line.RecordedBonus:N0} · khấu trừ {line.RecordedDeduction:N0} VND.\n{line.RecordedNotes}" +
            $"\nThanh toán: {line.PaymentStatus} · Phiếu {line.PaymentVoucherCode ?? "—"}" +
            (line.Warning.Length > 0 ? "\nCảnh báo: " + line.Warning : "") + (period.IsClosed ? "\nBảng đã chốt: chi tiết được giữ theo thời điểm tính." : "");
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => IsEditable);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => !IsBusy);
    }
    public event Action? CloseRequested;
    public string Title => $"{_line.Code} — {_line.FullName} • {_period.Month}/{_period.Year}";
    public string Description { get; }
    public IReadOnlyList<PayrollAttendanceDisplay> Attendance { get; }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(IsEditable)); SaveCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } } }
    public bool IsEditable => _canAdjust && !_period.IsClosed && !IsBusy;
    public string BonusText { get => _bonus; set { if (SetProperty(ref _bonus, value)) OnPropertyChanged(nameof(NetText)); } }
    public string DeductionText { get => _deduction; set { if (SetProperty(ref _deduction, value)) OnPropertyChanged(nameof(NetText)); } }
    public string BonusNote { get => _bonusNote; set => SetProperty(ref _bonusNote, value); }
    public string DeductionNote { get => _deductionNote; set => SetProperty(ref _deductionNote, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string NetText => decimal.TryParse(BonusText, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var b) && decimal.TryParse(DeductionText, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var d) ? $"Thực nhận: {_line.Salary + _line.RecordedBonus + b - _line.RecordedDeduction - d:N0} VND" : "Số tiền không hợp lệ";
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    private async Task SaveAsync()
    {
        IsBusy = true; Error = "";
        try {
            if (!decimal.TryParse(BonusText, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var b) || !decimal.TryParse(DeductionText, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var d)) throw new InvalidOperationException("Số tiền không hợp lệ.");
            var request = new PayrollAdjustment(_period.Id, _period.Revision, _line.Id, b, BonusNote, d, DeductionNote);
            PayrollCalculator.ValidateAdjustment(_line.Salary + _line.RecordedBonus, request);
            if (d + _line.RecordedDeduction > _line.Salary + b + _line.RecordedBonus) throw new InvalidOperationException("Tổng khấu trừ vượt lương và thưởng.");
            await _save(request); CloseRequested?.Invoke();
        } catch (Exception ex) { Error = PayrollTabViewModel.ErrorText(ex); }
        finally { IsBusy = false; }
    }
}
