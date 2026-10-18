using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Finance;

/// <summary>
/// ViewModel dùng chung cho tab Thu và tab Chi — phân biệt bằng VoucherType (IN/OUT).
/// </summary>
public sealed class VoucherListViewModel : ObservableObject
{
    private readonly Kazuko.Core.UserManagement.IAuthorizationService? _permissions;
    public string AddPermissionRule => (_voucherType == FinancialVoucherTypes.In ? "FIN_CASH_IN" : "FIN_CASH_OUT") + ":Add";
    private readonly IFinanceDashboardRepository _dashboard;
    private readonly IFinanceVoucherService _voucherService;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly string _voucherType;

    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _keyword = string.Empty;
    private DateTime? _fromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime? _toDate = DateTime.Today;
    private RecentVoucherRow? _selectedVoucher;
    private CashVoucherDetail? _selectedDetail;
    private bool _detailCancellable;

    public VoucherListViewModel(
        IFinanceDashboardRepository dashboard,
        IFinanceVoucherService voucherService,
        ICurrentSessionAccessor sessionAccessor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        string voucherType,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null)
    {
        _dashboard = dashboard;
        _voucherService = voucherService;
        _sessionAccessor = sessionAccessor;
        _funds = funds;
        _banks = banks;
        _voucherType = voucherType;
        _permissions = authorization;

        var isIncome = voucherType == FinancialVoucherTypes.In;
        Title = isIncome ? "Phiếu thu" : "Phiếu chi";
        AddButtonText = isIncome ? "Thêm phiếu thu" : "Thêm phiếu chi";

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        RefreshCommand = new AsyncRelayCommand(SearchAsync, () => !IsBusy);
        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsBusy);
        CancelCommand = new AsyncRelayCommand(CancelAsync, () => !IsBusy && DetailCancellable);

        _ = SearchAsync();
    }

    public string Title { get; }
    public string AddButtonText { get; }

    public ObservableCollection<RecentVoucherRow> Vouchers { get; } = [];

    public ICommand SearchCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand CancelCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string Keyword
    {
        get => _keyword;
        set => SetProperty(ref _keyword, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public RecentVoucherRow? SelectedVoucher
    {
        get => _selectedVoucher;
        set
        {
            if (SetProperty(ref _selectedVoucher, value))
            {
                _ = LoadDetailAsync();
            }
        }
    }

    public CashVoucherDetail? SelectedDetail
    {
        get => _selectedDetail;
        private set => SetProperty(ref _selectedDetail, value);
    }

    public bool DetailCancellable
    {
        get => _detailCancellable;
        private set
        {
            if (SetProperty(ref _detailCancellable, value))
            {
                (CancelCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    private async Task SearchAsync()
    {
        if (_permissions is not null && !_permissions.Current.Allows(_voucherType == FinancialVoucherTypes.In ? "FIN_CASH_IN" : "FIN_CASH_OUT")) return;
        ErrorMessage = string.Empty;
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập không hợp lệ.";
            return;
        }

        IsBusy = true;
        try
        {
            var items = await _dashboard.SearchVouchersAsync(
                _voucherType,
                FromDate,
                ToDate,
                string.IsNullOrWhiteSpace(Keyword) ? null : Keyword,
                session);

            Vouchers.Clear();
            var index = 1;
            foreach (var item in items)
            {
                var isIncome = item.VoucherType == FinancialVoucherTypes.In;
                Vouchers.Add(new RecentVoucherRow
                {
                    Id = item.Id,
                    Index = index++,
                    VoucherDate = item.VoucherDate,
                    Code = item.Code,
                    VoucherType = item.VoucherType,
                    Content = item.Content,
                    PartyName = item.PartyName,
                    PaymentMethod = item.PaymentMethod,
                    IncomeAmount = isIncome ? item.AmountVnd : 0,
                    ExpenseAmount = isIncome ? 0 : item.AmountVnd,
                    CreatedByName = item.CreatedByName
                });
            }

            SelectedVoucher = null;
            SelectedDetail = null;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không tải được danh sách phiếu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadDetailAsync()
    {
        SelectedDetail = null;
        DetailCancellable = false;

        var row = SelectedVoucher;
        if (row is null)
        {
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập không hợp lệ.";
            return;
        }

        try
        {
            var detail = await _dashboard.GetVoucherDetailAsync(row.Id, session);
            SelectedDetail = detail;
            DetailCancellable = detail is not null
                && detail.StatusCode == FinancialVoucherStatuses.Posted;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không tải được chi tiết phiếu: {ex.Message}";
        }
    }

    private async Task AddAsync()
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
        var saved = CashVoucherDialog.Show(owner, _voucherService, _sessionAccessor, _funds, _banks, _voucherType);
        if (saved is not null)
        {
            await SearchAsync();
        }
    }

    private async Task CancelAsync()
    {
        var detail = SelectedDetail;
        if (detail is null || !DetailCancellable)
        {
            return;
        }

        var confirm = MessageBox.Show(
            $"Hủy phiếu {detail.Code}?\nPhiếu đã hủy không thể khôi phục.",
            "Xác nhận hủy phiếu",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập không hợp lệ.";
            return;
        }

        IsBusy = true;
        try
        {
            await _voucherService.CancelVoucherAsync(
                detail.Id, "Hủy bởi người dùng từ danh sách phiếu", session);
            await SearchAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Không hủy được phiếu: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
