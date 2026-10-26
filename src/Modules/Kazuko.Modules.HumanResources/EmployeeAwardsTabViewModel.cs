using System.Collections.ObjectModel;
using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.HumanResources;

public sealed class EmployeeAwardsTabViewModel : ObservableObject
{
    private readonly IEmployeeMoneyService _service;
    private readonly ICurrentSessionAccessor _session;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly IVoucherCategoryService _categories;
    private readonly IAuthorizationService? _permissions;
    private IReadOnlyList<EmployeeAward> _all = [];
    private bool _busy, _loaded;
    private DateTime _from = new(DateTime.Today.Year, DateTime.Today.Month, 1), _to = DateTime.Today;
    private string _search = "", _reason = "", _error = "", _message = "";
    private EmployeeAward? _selected;
    public EmployeeAwardsTabViewModel(IEmployeeMoneyService service, ICurrentSessionAccessor session, ICashFundRepository funds,
        IBankAccountRepository banks, IVoucherCategoryService categories, IAuthorizationService? permissions)
    {
        _service = service; _session = session; _funds = funds; _banks = banks; _categories = categories; _permissions = permissions;
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy && Allows(PermissionAction.Add));
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => !IsBusy && Selected is { Status: "ACTIVE" } && !string.IsNullOrWhiteSpace(CancelReason) && Allows(PermissionAction.Delete));
    }
    public ObservableCollection<EmployeeAward> Rows { get; } = [];
    public DateTime From { get => _from; set => SetProperty(ref _from, value); }
    public DateTime To { get => _to; set => SetProperty(ref _to, value); }
    public string Search { get => _search; set { if (SetProperty(ref _search, value)) Filter(); } }
    public string CancelReason { get => _reason; set { if (SetProperty(ref _reason, value)) CancelCommand.NotifyCanExecuteChanged(); } }
    public EmployeeAward? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) { CancelCommand.NotifyCanExecuteChanged(); OnPropertyChanged(nameof(SelectedDescription)); } } }
    public string SelectedDescription => Selected is null ? "Chọn một dòng để xem nội dung." : $"{Selected.EmployeeName} · {Selected.KindText} · {Selected.HandlingText}\n{Selected.Description}\nKỳ: {Selected.PeriodText} · {Selected.StatusText} · Phiếu: {Selected.VoucherCode ?? "—"}";
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(CanEdit)); LoadCommand.NotifyCanExecuteChanged(); AddCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged(); } } }
    public bool CanEdit => !IsBusy;
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncRelayCommand LoadCommand { get; }
    public AsyncRelayCommand AddCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    private bool Allows(PermissionAction action) => _permissions?.Current.Allows("CAT_EMPLOYEES", action) ?? true;
    private AuthenticatedSession Session => _session.Current ?? throw new InvalidOperationException("Vui lòng đăng nhập lại.");
    public Task EnsureLoadedAsync() { if (_loaded) return Task.CompletedTask; _loaded = true; return LoadAsync(); }
    private async Task LoadAsync()
    {
        IsBusy = true; Error = "";
        try { _all = await _service.ListAsync(From, To, Session); Filter(); }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
    private void Filter()
    {
        var id = Selected?.Id; Rows.Clear();
        foreach (var row in _all)
            if (string.IsNullOrWhiteSpace(Search) || (row.EmployeeCode + " " + row.EmployeeName + " " + row.Description).Contains(Search.Trim(), StringComparison.CurrentCultureIgnoreCase)) Rows.Add(row);
        Selected = Rows.FirstOrDefault(x => x.Id == id) ?? Rows.FirstOrDefault();
    }
    private async Task AddAsync()
    {
        try
        {
            var vm = new EmployeeMoneyDialogViewModel(_service, _session, _funds, _banks, _categories);
            var dialog = new EmployeeMoneyDialog(vm) { Owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(x => x.IsActive) };
            if (dialog.ShowDialog() == true)
            {
                if (vm.EventDate is DateTime date && (date < From || date > To)) { From = new(date.Year, date.Month, 1); To = From.AddMonths(1).AddDays(-1); }
                Search = "";
                await LoadAsync(); Selected = Rows.FirstOrDefault(x => x.Id == vm.SavedAwardId); Message = vm.SavedMessage;
            }
        }
        catch (Exception ex) { Error = ex.Message; }
    }
    private async Task CancelAsync()
    {
        if (Selected is not { } selected) return;
        if (MessageBox.Show($"Hủy khoản của {selected.EmployeeName}?" + (selected.VoucherId is not null ? " Phiếu thu/chi liên kết cũng được hủy." : ""), "Hủy khoản phát sinh", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        IsBusy = true; Error = "";
        try { await _service.CancelAsync(selected.Id, CancelReason, Session); await LoadAsync(); CancelReason = ""; Message = "Đã hủy khoản phát sinh. Tính lại kỳ lương nếu khoản này đã vào bản nháp."; }
        catch (Exception ex) { Error = ex.Message; }
        finally { IsBusy = false; }
    }
}
