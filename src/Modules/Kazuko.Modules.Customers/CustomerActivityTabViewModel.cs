using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed record CustomerActivityOption(long Id, string Code, string Name, string Phone, bool IsWalkIn)
{
    public override string ToString() => DisplayText;
    public string DisplayText => $"{Code} — {Name} — {Phone}";
}

public sealed class CustomerActivityTabViewModel : ObservableObject
{
    private readonly ICustomerRepository _repository;
    private readonly ICurrentSessionAccessor _session;
    private readonly IDebtWorkbenchRepository? _debt;
    private readonly bool _debtMode;
    private CustomerActivityOption? _selected;
    private CancellationTokenSource? _loadCts, _optionsCts;
    private bool _loading;
    private string _error = "", _search = "";
    private DateTime? _from, _to;
    private CustomerDebtSnapshot _snapshot = new(0, 0, 0, 0);
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public CustomerActivityTabViewModel(ICustomerRepository repository, ICurrentSessionAccessor session,
        IDebtWorkbenchRepository? debt, bool debtMode, ICommand? collect = null)
    {
        _repository = repository; _session = session; _debt = debt; _debtMode = debtMode;
        CollectPaymentCommand = collect;
        FilterCommand = new AsyncRelayCommand(LoadAsync);
        SearchCustomersCommand = new AsyncRelayCommand(LoadCustomersAsync);
    }
    public ObservableCollection<CustomerActivityOption> CustomerOptions { get; } = new();
    public ObservableCollection<PurchaseHistoryRowViewModel> FullPurchaseHistory { get; } = new();
    public ObservableCollection<PaymentHistoryRowViewModel> PaymentHistory { get; } = new();
    public ICommand FilterCommand { get; }
    public ICommand SearchCustomersCommand { get; }
    public ICommand? CollectPaymentCommand { get; }
    public string CustomerSearchText { get => _search; set => SetProperty(ref _search, value); }
    public DateTime? FromDate { get => _from; set => SetProperty(ref _from, value); }
    public DateTime? ToDate { get => _to; set => SetProperty(ref _to, value); }
    public CustomerActivityOption? SelectedCustomer
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(SelectedCode)); OnPropertyChanged(nameof(SelectedName));
            _ = LoadAsync();
        }
    }
    public string SelectedCode => SelectedCustomer?.Code ?? "Chọn khách hàng ở thanh lọc";
    public string SelectedName => SelectedCustomer?.Name ?? "";
    public bool HasFullPurchaseHistory => FullPurchaseHistory.Count > 0;
    public string ErrorMessage { get => _error; private set => SetProperty(ref _error, value); }
    public bool IsDetailLoading { get => _loading; private set => SetProperty(ref _loading, value); }
    public string DebtIncurredText => _snapshot.TotalIncurred.ToString("N0", Vi);
    public string DebtPaidText => _snapshot.Paid.ToString("N0", Vi);
    public string DebtCurrentText => _snapshot.CurrentDebt.ToString("N0", Vi);
    public string DebtOverdueText => _snapshot.Overdue.ToString("N0", Vi);
    public bool HasCurrentDebt => _snapshot.CurrentDebt > 0;
    public bool HasOverdueDebt => _snapshot.Overdue > 0;
    public string ResultHint => SelectedCustomer is null ? "Chọn khách hàng ngay tại thanh lọc để xem dữ liệu." :
        _debtMode ? "Khoảng ngày chỉ lọc lịch sử thanh toán. Các số tổng là công nợ hiện tại của khách đã chọn." :
        "Hiển thị tối đa 100 giao dịch gần nhất trong khoảng ngày đã lọc. Để trống ngày để xem mọi thời gian.";

    public async Task LoadCustomersAsync()
    {
        _optionsCts?.Cancel(); _optionsCts?.Dispose();
        _optionsCts = new(); var token = _optionsCts.Token;
        try
        {
            var items = await _repository.SearchAsync(new(CustomerSearchText, 0, "", null, null), token);
            token.ThrowIfCancellationRequested();
            var previousId = SelectedCustomer?.Id;
            CustomerOptions.Clear();
            foreach (var item in items) CustomerOptions.Add(new(item.Id, item.Code, item.Name, item.Phone, item.IsWalkIn));
            if (previousId.HasValue) SelectedCustomer = CustomerOptions.FirstOrDefault(x => x.Id == previousId);
        }
        catch (OperationCanceledException) { }
        catch { if (!token.IsCancellationRequested) ErrorMessage = "Không thể tải bộ chọn khách hàng. Bấm Tìm khách để thử lại."; }
    }

    public async Task LoadAsync()
    {
        _loadCts?.Cancel(); _loadCts?.Dispose(); _loadCts = new();
        var token = _loadCts.Token;
        var selected = SelectedCustomer; var from = FromDate?.Date; var to = ToDate?.Date;
        FullPurchaseHistory.Clear(); PaymentHistory.Clear(); SetSnapshot(new(0, 0, 0, 0));
        ErrorMessage = ""; OnPropertyChanged(nameof(HasFullPurchaseHistory)); OnPropertyChanged(nameof(ResultHint));
        IsDetailLoading = false;
        if (from.HasValue && to.HasValue && from > to) { ErrorMessage = "Từ ngày phải nhỏ hơn hoặc bằng đến ngày."; return; }
        if (selected is null) return;
        IsDetailLoading = true;
        try
        {
            if (!_debtMode)
            {
                var rows = await _repository.GetPurchaseHistoryAsync(selected.Id, 100, token, from, to);
                token.ThrowIfCancellationRequested();
                foreach (var row in rows) FullPurchaseHistory.Add(new(row));
                OnPropertyChanged(nameof(HasFullPurchaseHistory));
            }
            else
            {
                var snapshot = await _repository.GetDebtSnapshotAsync(selected.Id, token);
                token.ThrowIfCancellationRequested();
                if (_debt is not null && _session.Current is { } session)
                {
                    var ledger = await _debt.GetCustomerLedgerAsync(selected.Id, session, token);
                    token.ThrowIfCancellationRequested();
                    foreach (var entry in ledger.Where(x => x.SourceType == DebtLedgerSourceTypes.OrderPayment &&
                        (!from.HasValue || x.EntryDate.Date >= from) && (!to.HasValue || x.EntryDate.Date <= to)))
                        PaymentHistory.Add(new(entry));
                }
                SetSnapshot(snapshot);
            }
        }
        catch (OperationCanceledException) { }
        catch { if (!token.IsCancellationRequested) ErrorMessage = "Không thể tải dữ liệu. Bấm Lọc / Làm mới để thử lại."; }
        finally { if (!token.IsCancellationRequested) IsDetailLoading = false; }
    }
    private void SetSnapshot(CustomerDebtSnapshot snapshot)
    {
        _snapshot = snapshot;
        foreach (var name in new[] { nameof(DebtIncurredText), nameof(DebtPaidText), nameof(DebtCurrentText),
            nameof(DebtOverdueText), nameof(HasCurrentDebt), nameof(HasOverdueDebt) }) OnPropertyChanged(name);
    }
}
