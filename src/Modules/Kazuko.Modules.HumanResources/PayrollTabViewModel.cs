using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
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
    private readonly IEmployeeMoneyService? _money;
    private readonly ICashFundRepository? _funds;
    private readonly IBankAccountRepository? _banks;
    private readonly IVoucherCategoryService? _categories;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private PayrollPeriod? _period;
    private bool _busy, _loaded;
    private string _year = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture), _search = "", _message = "", _error = "", _reason = "";
    private int _month = DateTime.Today.Month;
    private string _shifts = "1";
    private bool _paidLeave;
    private PayrollLine? _selected;
    public PayrollTabViewModel(IPayrollService service, ICurrentSessionAccessor session, IAuthorizationService? authorization = null,
        IEmployeeMoneyService? money = null, ICashFundRepository? funds = null, IBankAccountRepository? banks = null, IVoucherCategoryService? categories = null)
    {
        _service = service; _session = session; _authorization = authorization;
        _money = money; _funds = funds; _banks = banks; _categories = categories;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        CalculateCommand = new AsyncRelayCommand(CalculateAsync, () => !IsBusy && !IsClosed);
        CloseCommand = new AsyncRelayCommand(CloseAsync, () => !IsBusy && _period is { IsClosed: false } && SettingsMatch);
        ReopenCommand = new AsyncRelayCommand(ReopenAsync, () => !IsBusy && IsClosed && _period?.Lines.Any(x => x.IsPaid) != true && !string.IsNullOrWhiteSpace(ReopenReason));
        DetailsCommand = new RelayCommand(ShowDetails, () => !IsBusy && SelectedLine is not null);
        ExportCommand = new RelayCommand<object?>(Export, _ => !IsBusy && Rows.Count > 0);
        PayCommand = new AsyncRelayCommand(PayAsync, () => PaymentBlockedReason.Length == 0);
        CancelPaymentCommand = new AsyncRelayCommand(CancelPaymentAsync, () => !IsBusy && SelectedLine is { IsPaid: true } && !string.IsNullOrWhiteSpace(ReopenReason) && _money is not null && Allows("HR_PAYROLL", PermissionAction.Edit) && Allows("FIN_REVERSE", PermissionAction.Delete));
        if (authorization is not null) WeakEventManager<IAuthorizationService, EventArgs>.AddHandler(authorization, nameof(IAuthorizationService.Changed), AuthorizationChanged);
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
    public PayrollLine? SelectedLine { get => _selected; set { if (SetProperty(ref _selected, value)) { DetailsCommand.NotifyCanExecuteChanged(); PayCommand.NotifyCanExecuteChanged(); CancelPaymentCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(PaymentHint)); } } }
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) Changed(); } }
    public bool CanSelectPeriod => !IsBusy;
    public bool CanEditSettings => !IsBusy && !IsClosed;
    public bool IsClosed => _period?.IsClosed == true;
    public string StatusText => _period is null ? "Chưa tính" : IsClosed ? $"Đã chốt • {_period.ClosedAt:dd/MM/yyyy HH:mm}" : "Bản nháp";
    public string Summary => _period is null ? "Chọn kỳ rồi nhấn Tải kỳ hoặc Tính lại." :
        $"Tổng kỳ: {_period.Lines.Count} nhân viên • Lương: {_period.Lines.Sum(l => l.Salary):N0} • Thưởng: {_period.Lines.Sum(l => l.TotalBonus):N0} • Khấu trừ: {_period.Lines.Sum(l => l.TotalDeduction):N0} • Thực nhận: {_period.Lines.Sum(l => l.NetSalary):N0} VND • Đã chi: {_period.Lines.Count(l => l.IsPaid)}/{_period.Lines.Count}";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    private string PaymentBlockedReason => IsBusy ? "Đang xử lý bảng lương, vui lòng đợi."
        : _period is null ? "Tải kỳ lương trước khi chi."
        : !IsClosed ? "Kỳ đang là bản nháp. Chốt bảng lương để bật Chi lương nhân viên; Mở chốt sẽ đưa kỳ về bản nháp."
        : SelectedLine is null ? "Chọn dòng nhân viên cần chi lương."
        : SelectedLine.IsPaid ? $"{SelectedLine.FullName} đã được chi lương bằng phiếu {SelectedLine.PaymentVoucherCode}."
        : SelectedLine.NetSalary <= 0 ? $"{SelectedLine.FullName} có thực nhận bằng 0, không phát sinh phiếu chi."
        : !Allows("HR_PAYROLL", PermissionAction.Edit) ? "Thiếu quyền Tính lương / Sửa để chi lương."
        : !Allows("FIN_CASH_OUT", PermissionAction.Add) ? "Thiếu quyền Phiếu chi / Thêm để tạo phiếu chi lương."
        : _money is null || _funds is null || _banks is null || _categories is null ? "Chưa nạp đủ dịch vụ chi lương. Hãy khởi động lại ứng dụng bản mới."
        : string.Empty;
    public string PaymentHint => PaymentBlockedReason.Length > 0 ? PaymentBlockedReason
        : $"Sẵn sàng chi cho {SelectedLine!.Code} · {SelectedLine.FullName}: {SelectedLine.NetSalary:N0} VND. Bấm Chi lương nhân viên để lập phiếu chi.";
    public Func<PayrollPeriod, bool> ConfirmClose { get; set; } = p => MessageBox.Show(
        $"Chốt bảng lương {p.Month}/{p.Year} cho {p.Lines.Count} nhân viên?" + (p.Lines.Any(l => l.Warning.Length > 0) ? "\nCó dòng cảnh báo cần kiểm tra." : ""),
        "Chốt bảng lương", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    public Func<PayrollPeriod, bool> ConfirmReopen { get; set; } = p => MessageBox.Show(
        $"Mở chốt bảng lương {p.Month}/{p.Year}?\nKỳ sẽ trở về Bản nháp và không thể chi lương cho đến khi chốt lại.",
        "Xác nhận mở chốt", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
    private bool SettingsMatch => _period is not null && _period.ShiftsPerDay == ShiftsPerDay && _period.IncludePaidLeave == IncludePaidLeave;
    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand CalculateCommand { get; }
    public AsyncRelayCommand CloseCommand { get; }
    public AsyncRelayCommand ReopenCommand { get; }
    public RelayCommand DetailsCommand { get; }
    public RelayCommand<object?> ExportCommand { get; }
    public AsyncRelayCommand PayCommand { get; }
    public AsyncRelayCommand CancelPaymentCommand { get; }
    private bool Allows(string function, PermissionAction action) => _authorization?.Current.Allows(function, action) ?? true;
    public Task EnsureLoadedAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return LoadAsync(); }
    private void Changed()
    {
        OnPropertyChanged(nameof(CanSelectPeriod)); OnPropertyChanged(nameof(CanEditSettings)); OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PaymentHint));
        LoadCommand.NotifyCanExecuteChanged(); CalculateCommand.NotifyCanExecuteChanged(); CloseCommand.NotifyCanExecuteChanged(); ReopenCommand.NotifyCanExecuteChanged(); DetailsCommand.NotifyCanExecuteChanged(); ExportCommand.NotifyCanExecuteChanged();
        PayCommand.NotifyCanExecuteChanged(); CancelPaymentCommand.NotifyCanExecuteChanged();
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
        if (!ConfirmClose(p)) return;
        Apply(await _service.CloseAsync(p.Id, p.Revision, Session)); Message = "Đã chốt bảng lương. Chọn nhân viên rồi bấm Chi lương nhân viên; chỉ Mở chốt khi cần sửa lại kỳ.";
    });
    private Task ReopenAsync() => RunAsync(async () => { var p = _period ?? throw new InvalidOperationException("Chưa có bảng lương."); if (!ConfirmReopen(p)) return; Apply(await _service.ReopenAsync(p.Id, p.Revision, ReopenReason, Session)); ReopenReason = ""; Message = "Đã mở chốt, kỳ trở về bản nháp. Tính lại nếu cần cập nhật rồi Chốt bảng lương trước khi chi."; });
    private Task PayAsync() => RunAsync(async () =>
    {
        if (_period is null || SelectedLine is null || _money is null || _funds is null || _banks is null || _categories is null) return;
        var vm = new EmployeeMoneyDialogViewModel(_money, _session, _funds, _banks, _categories, _period, SelectedLine);
        var dialog = new EmployeeMoneyDialog(vm) { Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) };
        if (dialog.ShowDialog() == true) { Apply(await _service.GetAsync(Year(), Month)); Message = vm.SavedMessage; }
    });
    private Task CancelPaymentAsync() => RunAsync(async () =>
    {
        if (_period is null || SelectedLine?.PaymentVoucherId is not long voucherId || _money is null) return;
        if (MessageBox.Show($"Hủy phiếu {SelectedLine.PaymentVoucherCode} của {SelectedLine.FullName}?", "Hủy chi lương", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _money.CancelSalaryPaymentAsync(_period.Id, SelectedLine.EmployeeId, voucherId, ReopenReason, Session);
        Apply(await _service.GetAsync(Year(), Month)); ReopenReason = ""; Message = "Đã hủy chi lương và phiếu chi liên kết.";
    });
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
        SelectedLine = Rows.FirstOrDefault(l => l.EmployeeId == id) ?? Rows.FirstOrDefault(l => !l.IsPaid && l.NetSalary > 0) ?? Rows.FirstOrDefault();
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
    private void AuthorizationChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.CheckAccess()) Changed(); else _dispatcher.BeginInvoke(new Action(Changed));
    }
    private void Export(object? parameter)
    {
        ExcelExportService.Export("Xuất bảng lương", $"BangLuong_{YearText}_{Month:00}_{(IsClosed ? "DaChot" : "Nhap")}", parameter as DataGrid, Rows,
            new ExcelExportColumn<PayrollLine>[] {
                new("Code","Mã NV",x=>x.Code), new("FullName","Nhân viên",x=>x.FullName), new("MethodLabel","Cách tính",x=>x.MethodLabel),
                new("StandardUnits","Công chuẩn",x=>x.StandardUnits,ExcelCellKind.Quantity), new("ActualUnits","Công tính lương",x=>x.ActualUnits,ExcelCellKind.Quantity),
                new("Rate","Lương ca/tháng",x=>x.Rate,ExcelCellKind.Money), new("Salary","Lương tính được",x=>x.Salary,ExcelCellKind.Money),
                new("TotalBonus","Tổng thưởng",x=>x.TotalBonus,ExcelCellKind.Money),new("BonusNote","Diễn giải thưởng thêm",x=>x.BonusNote),
                new("TotalDeduction","Tổng khấu trừ",x=>x.TotalDeduction,ExcelCellKind.Money),new("DeductionNote","Diễn giải khấu trừ thêm",x=>x.DeductionNote),
                new("RecordedNotes","Khoản đã ghi nhận",x=>x.RecordedNotes),new("PaymentStatus","Thanh toán",x=>x.PaymentStatus),new("PaymentVoucherCode","Phiếu chi",x=>x.PaymentVoucherCode),
                new("NetSalary","Thực nhận",x=>x.NetSalary,ExcelCellKind.Money),new("Warning","Cảnh báo",x=>x.Warning)
            });
    }
}
