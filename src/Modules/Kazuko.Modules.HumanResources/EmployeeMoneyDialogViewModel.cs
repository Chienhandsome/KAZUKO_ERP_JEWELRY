using System.Collections.ObjectModel;
using System.Globalization;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.HumanResources;

public sealed record HrMoneyChoice(string Code, string Name) { public override string ToString() => Name; }
public sealed class EmployeeMoneyDialogViewModel : ObservableObject
{
    private readonly IEmployeeMoneyService _service;
    private readonly ICurrentSessionAccessor _session;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly IVoucherCategoryService _categories;
    private readonly PayrollPeriod? _period;
    private readonly PayrollLine? _line;
    private readonly Guid _requestId = Guid.NewGuid();
    private bool _busy;
    private string _error = "", _description = "", _amount = "", _year = DateTime.Today.Year.ToString(CultureInfo.InvariantCulture), _reference = "";
    private int _month = DateTime.Today.Month;
    private DateTime? _date = DateTime.Today;
    private HrMoneyChoice _kind = new("REWARD", "Khen thưởng"), _handling = new("PAYROLL", "Cộng vào lương"), _method = new("CASH", "Tiền mặt");
    private EmployeeMoneyOption? _employee;
    private CashFundOption? _fund;
    private BankAccountOption? _bank;
    private VoucherCategory? _category;
    public EmployeeMoneyDialogViewModel(IEmployeeMoneyService service, ICurrentSessionAccessor session, ICashFundRepository funds,
        IBankAccountRepository banks, IVoucherCategoryService categories, PayrollPeriod? period = null, PayrollLine? line = null)
    {
        _service = service; _session = session; _funds = funds; _banks = banks; _categories = categories; _period = period; _line = line;
        if (line is not null) _amount = line.NetSalary.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(false), () => !IsBusy);
        ReloadPaymentCommand = new AsyncRelayCommand(LoadPaymentAsync, () => !IsBusy && ShowsPayment);
    }
    public bool IsSalary => _period is not null;
    public bool IsAward => !IsSalary;
    public string Title => IsSalary ? $"Chi lương {_period!.Month:00}/{_period.Year} · {_line!.FullName}" : "Thêm khen thưởng / kỷ luật";
    public string EmployeeName => _line?.FullName ?? "";
    public string Information => IsSalary ? "Chi đủ thực nhận một lần. Phiếu chi loại Chi lương được liên kết với bảng lương đã chốt." : "Khoản cộng/trừ lương không tạo phiếu thu/chi riêng. Chỉ ghi nhận không ảnh hưởng tiền.";
    public bool ShowsAmount => IsSalary || SelectedHandling.Code != "NONE";
    public bool ShowsPeriod => !IsSalary && SelectedHandling.Code == "PAYROLL";
    public bool ShowsPayment => IsSalary || SelectedHandling.Code == "CASH";
    public bool ShowsCategory => !IsSalary && ShowsPayment;
    public bool UsesFund => ShowsPayment && SelectedMethod.Code == "CASH";
    public bool UsesBank => ShowsPayment && SelectedMethod.Code != "CASH";
    public bool CanEdit => !IsBusy;
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(CanEdit)); SaveCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); ReloadPaymentCommand.NotifyCanExecuteChanged(); } } }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Description { get => _description; set => SetProperty(ref _description, value); }
    public string AmountText { get => _amount; set => SetProperty(ref _amount, value); }
    public string YearText { get => _year; set => SetProperty(ref _year, value); }
    public int Month { get => _month; set => SetProperty(ref _month, value); }
    public DateTime? EventDate { get => _date; set => SetProperty(ref _date, value); }
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }
    public ObservableCollection<EmployeeMoneyOption> Employees { get; } = [];
    public ObservableCollection<CashFundOption> Funds { get; } = [];
    public ObservableCollection<BankAccountOption> Banks { get; } = [];
    public ObservableCollection<VoucherCategory> Categories { get; } = [];
    public IReadOnlyList<int> Months { get; } = Enumerable.Range(1, 12).ToArray();
    public IReadOnlyList<HrMoneyChoice> Kinds { get; } = [new("REWARD", "Khen thưởng"), new("DISCIPLINE", "Kỷ luật")];
    public IReadOnlyList<HrMoneyChoice> Methods { get; } = [new("CASH", "Tiền mặt"), new("BANK_TRANSFER", "Chuyển khoản"), new("CARD", "Thẻ")];
    public IReadOnlyList<HrMoneyChoice> Handlings => [new("PAYROLL", SelectedKind.Code == "REWARD" ? "Cộng vào lương" : "Trừ vào lương"), new("CASH", SelectedKind.Code == "REWARD" ? "Chi riêng" : "Thu riêng"), new("NONE", "Chỉ ghi nhận")];
    public HrMoneyChoice SelectedKind
    {
        get => _kind;
        set { if (value is null || !SetProperty(ref _kind, value)) return; OnPropertyChanged(nameof(Handlings)); SelectedHandling = Handlings.First(x => x.Code == _handling.Code); }
    }
    public HrMoneyChoice SelectedHandling
    {
        get => _handling;
        set { if (value is null || !SetProperty(ref _handling, value)) return; Changed(); if (ShowsPayment) _ = LoadPaymentAsync(); }
    }
    public HrMoneyChoice SelectedMethod { get => _method; set { if (value is not null && SetProperty(ref _method, value)) Changed(); } }
    public EmployeeMoneyOption? SelectedEmployee { get => _employee; set => SetProperty(ref _employee, value); }
    public CashFundOption? SelectedFund { get => _fund; set => SetProperty(ref _fund, value); }
    public BankAccountOption? SelectedBank { get => _bank; set => SetProperty(ref _bank, value); }
    public VoucherCategory? SelectedCategory { get => _category; set => SetProperty(ref _category, value); }
    public AsyncRelayCommand SaveCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand ReloadPaymentCommand { get; }
    public event Action<bool>? CloseRequested;
    public string SavedMessage { get; private set; } = "";
    public long? SavedAwardId { get; private set; }
    private AuthenticatedSession Session => _session.Current ?? throw new InvalidOperationException("Vui lòng đăng nhập lại.");
    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            if (!IsSalary) { var rows = await _service.GetEmployeesAsync(Session); Employees.Clear(); foreach (var row in rows) Employees.Add(row); }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
        if (ShowsPayment) await LoadPaymentAsync();
    }
    private async Task LoadPaymentAsync()
    {
        if (!ShowsPayment || IsBusy) return;
        IsBusy = true; Error = "";
        try
        {
            var funds = await _funds.GetActiveFundsAsync(Session); var banks = await _banks.GetActiveAccountsAsync(Session);
            var fundId = SelectedFund?.Id; var bankId = SelectedBank?.Id;
            Funds.Clear(); foreach (var f in funds) Funds.Add(f); Banks.Clear(); foreach (var b in banks) Banks.Add(b);
            SelectedFund = Funds.FirstOrDefault(x => x.Id == fundId) ?? Funds.FirstOrDefault(x => x.IsDefault) ?? Funds.FirstOrDefault();
            SelectedBank = Banks.FirstOrDefault(x => x.Id == bankId) ?? Banks.FirstOrDefault();
            if (!IsSalary)
            {
                var rows = await _categories.ListAsync(SelectedKind.Code == "REWARD" ? "OUT" : "IN", false, Session);
                Categories.Clear(); foreach (var row in rows) Categories.Add(row);
                SelectedCategory = Categories.FirstOrDefault(x => x.Code == (SelectedKind.Code == "REWARD" ? "BONUS" : "OTHER")) ?? Categories.FirstOrDefault();
            }
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    private void Changed()
    {
        foreach (var name in new[] { nameof(ShowsAmount), nameof(ShowsPeriod), nameof(ShowsPayment), nameof(ShowsCategory), nameof(UsesFund), nameof(UsesBank) }) OnPropertyChanged(name);
        ReloadPaymentCommand.NotifyCanExecuteChanged();
    }
    private async Task SaveAsync()
    {
        IsBusy = true; Error = "";
        try
        {
            var eventDate = EventDate ?? throw new InvalidOperationException("Chọn ngày phát sinh / thanh toán.");
            var amount = ShowsAmount && decimal.TryParse(AmountText, NumberStyles.Number, CultureInfo.GetCultureInfo("vi-VN"), out var parsed) ? parsed : 0;
            var payment = ShowsPayment ? new PaymentAllocationDraft(SelectedMethod.Code, amount, UsesFund ? SelectedFund?.Id : null, UsesBank ? SelectedBank?.Id : null, string.IsNullOrWhiteSpace(Reference) ? null : Reference.Trim()) : null;
            if (IsSalary)
            {
                var result = await _service.PaySalaryAsync(new(_period!.Id, _period.Revision, _line!.EmployeeId, eventDate, payment!), Session);
                SavedMessage = $"{(result.AlreadyPosted ? "Đã có" : "Đã tạo")} phiếu chi {result.VoucherCode}.";
            }
            else
            {
                if (SelectedEmployee is null) throw new InvalidOperationException("Chọn nhân viên.");
                if (ShowsPeriod && !int.TryParse(YearText, out _)) throw new InvalidOperationException("Năm kỳ lương không hợp lệ.");
                var request = new EmployeeAwardRequest(_requestId, SelectedEmployee.Id, eventDate, SelectedKind.Code, SelectedHandling.Code,
                    amount, Description, ShowsPeriod ? int.Parse(YearText, CultureInfo.InvariantCulture) : null, ShowsPeriod ? Month : null,
                    payment, ShowsCategory ? SelectedCategory?.Id : null);
                var result = await _service.CreateAsync(EmployeeMoneyRules.Validate(request), Session);
                SavedAwardId = result.Id;
                SavedMessage = result.VoucherCode is null ? "Đã ghi nhận khoản phát sinh." : $"Đã ghi nhận và tạo phiếu {result.VoucherCode}.";
            }
            CloseRequested?.Invoke(true);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
}
