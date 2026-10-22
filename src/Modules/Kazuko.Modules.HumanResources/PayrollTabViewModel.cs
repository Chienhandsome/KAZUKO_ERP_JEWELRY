using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;
using Kazuko.Shared.UI.Services;

namespace Kazuko.Modules.HumanResources;

public sealed class PayrollTabViewModel : ObservableObject
{
    private readonly IPayrollService _service;
    private readonly IAuthorizationService? _authorization;
    private readonly ICurrentSessionAccessor _session;
    private PayrollPeriod? _period;
    private bool _busy, _loaded;
    private string _year = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture), _search = "", _message = "", _error = "", _reason = "";
    private int _month = DateTime.Today.Month;
    private string _shifts = "1";
    private bool _paidLeave;
    private PayrollLine? _selected;
    public PayrollTabViewModel(IPayrollService service, ICurrentSessionAccessor session, IAuthorizationService? authorization = null)
    {
        _service = service; _session = session; _authorization = authorization;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CalculateCommand = new AsyncRelayCommand(CalculateAsync, () => !IsBusy && !IsClosed);
        CloseCommand = new AsyncRelayCommand(CloseAsync, () => !IsBusy && _period is { IsClosed: false } && SettingsMatch);
        ReopenCommand = new AsyncRelayCommand(ReopenAsync, () => !IsBusy && IsClosed && !string.IsNullOrWhiteSpace(ReopenReason));
        DetailsCommand = new RelayCommand(ShowDetails, () => !IsBusy && SelectedLine is not null);
        ExportCommand = new RelayCommand<object?>(Export, _ => !IsBusy && Rows.Count > 0);
    }
    public ObservableCollection<PayrollLine> Rows { get; } = new();
    public IReadOnlyList<int> Months { get; } = Enumerable.Range(1, 12).ToArray();
    public string YearText { get => _year; set { if (SetProperty(ref _year, value)) ClearPeriod(); } }
    public int Month { get => _month; set { if (SetProperty(ref _month, value)) ClearPeriod(); } }
    public string ShiftsPerDayText { get => _shifts; set { if (SetProperty(ref _shifts, value)) Changed(); } }
    private int ShiftsPerDay => int.TryParse(ShiftsPerDayText, out var value) ? value : 0;
    public bool IncludePaidLeave { get => _paidLeave; set { if (SetProperty(ref _paidLeave, value)) Changed(); } }
    public string SearchText { get => _search; set { if (SetProperty(ref _search, value)) Filter(); } }
    public string ReopenReason { get => _reason; set { if (SetProperty(ref _reason, value)) Changed(); } }
    public PayrollLine? SelectedLine { get => _selected; set { if (SetProperty(ref _selected, value)) DetailsCommand.NotifyCanExecuteChanged(); } }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) Changed(); } }
    public bool CanSelectPeriod => !IsBusy;
    public bool CanEditSettings => !IsBusy && !IsClosed;
    public bool IsClosed => _period?.IsClosed == true;
    public string StatusText => _period is null ? "Chưa tính" : IsClosed ? $"Đã chốt • {_period.ClosedAt:dd/MM/yyyy HH:mm}" : "Bản nháp";
    public string Summary => _period is null ? "Chọn kỳ rồi nhấn Tải kỳ hoặc Tính lại." :
        $"Tổng kỳ: {_period.Lines.Count} nhân viên • Lương: {_period.Lines.Sum(l => l.Salary):N0} • Thưởng: {_period.Lines.Sum(l => l.Bonus):N0} • Khấu trừ: {_period.Lines.Sum(l => l.Deduction):N0} • Thực nhận: {_period.Lines.Sum(l => l.NetSalary):N0} VND";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    private bool SettingsMatch => _period is not null && _period.ShiftsPerDay == ShiftsPerDay && _period.IncludePaidLeave == IncludePaidLeave;
    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand CalculateCommand { get; }
    public AsyncRelayCommand CloseCommand { get; }
    public AsyncRelayCommand ReopenCommand { get; }
    public RelayCommand DetailsCommand { get; }
    public RelayCommand<object?> ExportCommand { get; }
    public Task EnsureLoadedAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return LoadAsync(); }
    private void Changed()
    {
        OnPropertyChanged(nameof(CanSelectPeriod)); OnPropertyChanged(nameof(CanEditSettings)); OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(Summary));
        LoadCommand.NotifyCanExecuteChanged(); CalculateCommand.NotifyCanExecuteChanged(); CloseCommand.NotifyCanExecuteChanged(); ReopenCommand.NotifyCanExecuteChanged(); DetailsCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
    }
    private void ClearPeriod() { _period = null; Rows.Clear(); SelectedLine = null; Error = ""; Message = "Nhấn Tải kỳ để xem bảng đã lưu hoặc Tính lại để tạo bản nháp."; Changed(); }
    private int Year() => int.TryParse(YearText, out var year) && year is >= 1900 and <= 9998 ? year : throw new InvalidOperationException("Năm phải từ 1900 đến 9998.");
    private AuthenticatedSession Session => _session.Current ?? throw new InvalidOperationException("Vui lòng đăng nhập lại.");
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy) return; IsBusy = true; Error = ""; Message = "";
        try { await action(); }
        catch (Exception ex) { Error = ErrorText(ex); }
        finally { IsBusy = false; }
    }
    public static string ErrorText(Exception ex) => ex is InvalidOperationException or UnauthorizedAccessException ? ex.Message : "Không thể thực hiện thao tác. Kiểm tra kết nối/migration và tải lại kỳ lương.";
    private Task LoadAsync() => RunAsync(async () => { Apply(await _service.GetAsync(Year(), Month)); Message = _period is null ? "Kỳ này chưa có bảng lương. Nhấn Tính lại để tạo." : "Đã tải bảng lương đã lưu."; });
    private Task CalculateAsync() => RunAsync(async () => { Apply(await _service.CalculateAsync(Year(), Month, new(ShiftsPerDay, IncludePaidLeave), _period?.Revision, Session)); Message = $"Đã lưu bản nháp. Có {_period!.Lines.Count(l => l.Warning.Length > 0)} dòng cảnh báo cần kiểm tra trước khi chốt."; });
    private Task CloseAsync() => RunAsync(async () =>
    {
        var p = _period ?? throw new InvalidOperationException("Chưa có bảng lương.");
        var warnings = p.Lines.Count(l => l.Warning.Length > 0);
        if (MessageBox.Show($"Chốt bảng lương {p.Month}/{p.Year} cho {p.Lines.Count} nhân viên?" + (warnings > 0 ? $"\nCó {warnings} dòng cảnh báo cần kiểm tra." : ""), "Chốt bảng lương", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Apply(await _service.CloseAsync(p.Id, p.Revision, Session)); Message = "Đã chốt bảng lương.";
    });
    private Task ReopenAsync() => RunAsync(async () => { var p = _period ?? throw new InvalidOperationException("Chưa có bảng lương."); Apply(await _service.ReopenAsync(p.Id, p.Revision, ReopenReason, Session)); ReopenReason = ""; Message = "Đã mở chốt. Tính lại để cập nhật chấm công/đơn giá mới."; });
    private void Apply(PayrollPeriod? period)
    {
        _period = period;
        if (period is not null) { _shifts = period.ShiftsPerDay.ToString(CultureInfo.InvariantCulture); _paidLeave = period.IncludePaidLeave; OnPropertyChanged(nameof(ShiftsPerDayText)); OnPropertyChanged(nameof(IncludePaidLeave)); }
        Filter(); Changed();
    }
    private void Filter()
    {
        var id = SelectedLine?.EmployeeId; Rows.Clear();
        foreach (var row in _period?.Lines ?? Array.Empty<PayrollLine>())
            if (string.IsNullOrWhiteSpace(SearchText) || (row.Code + " " + row.FullName).Contains(SearchText.Trim(), StringComparison.CurrentCultureIgnoreCase)) Rows.Add(row);
        SelectedLine = Rows.FirstOrDefault(l => l.EmployeeId == id) ?? Rows.FirstOrDefault();
        ExportCommand.NotifyCanExecuteChanged();
    }
    private void ShowDetails()
    {
        if (_period is null || SelectedLine is null) return;
        try
        {
        var p = _period;
        var vm = new PayrollDetailViewModel(p, SelectedLine, async request => { Apply(await _service.AdjustAsync(request, Session)); }, _authorization?.Current.Allows("HR_PAYROLL", PermissionAction.Edit) ?? true);
        var window = new PayrollDetailDialog { DataContext = vm, Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) };
        vm.CloseRequested += () => window.Close(); window.ShowDialog();
        }
        catch (Exception ex) { Error = ErrorText(ex); }
    }
    private void Export(object? parameter)
    {
        ExcelExportService.Export("Xuất bảng lương", $"BangLuong_{YearText}_{Month:00}_{(IsClosed ? "DaChot" : "Nhap")}", parameter as DataGrid, Rows,
            new ExcelExportColumn<PayrollLine>[] {
                new("Code","Mã NV",x=>x.Code), new("FullName","Nhân viên",x=>x.FullName), new("MethodLabel","Cách tính",x=>x.MethodLabel),
                new("StandardUnits","Công chuẩn",x=>x.StandardUnits,ExcelCellKind.Quantity), new("ActualUnits","Công tính lương",x=>x.ActualUnits,ExcelCellKind.Quantity),
                new("Rate","Lương ca/tháng",x=>x.Rate,ExcelCellKind.Money), new("Salary","Lương tính được",x=>x.Salary,ExcelCellKind.Money),
                new("Bonus","Thưởng",x=>x.Bonus,ExcelCellKind.Money),new("BonusNote","Diễn giải thưởng",x=>x.BonusNote),
                new("Deduction","Khấu trừ",x=>x.Deduction,ExcelCellKind.Money),new("DeductionNote","Diễn giải khấu trừ",x=>x.DeductionNote),
                new("NetSalary","Thực nhận",x=>x.NetSalary,ExcelCellKind.Money),new("Warning","Cảnh báo",x=>x.Warning)
            });
    }
}
