using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales;

public sealed class SalesViewModel : ObservableObject
{
    private readonly ISalesQueryRepository _queries;
    private readonly ISalesCheckoutApplicationService _checkout;
    private readonly ISalesAuthorizationService _authorization;
    private readonly ICashFundRepository _cashFunds;
    private readonly IBankAccountRepository _bankAccounts;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly ISalesPaymentDialogService _paymentDialog;
    private readonly ISalesReceiptPresenter _receiptPresenter;

    private int _mainTabIndex;
    private bool _isLoading;
    private bool _isPosting;
    private string _errorMessage = string.Empty;
    private string _successMessage = string.Empty;
    private string? _notes;
    private Guid _clientRequestId = Guid.NewGuid();
    private bool _preserveRequestIdOnEdit;
    private SalesPermissionSnapshot _permissions = new(false, false, false, false, false, false);
    private long _salesFloorLocationId;
    private SalesCheckoutResult? _lastResult;
    private IReadOnlyList<CashFundOption> _funds = [];
    private IReadOnlyList<BankAccountOption> _banks = [];

    public SalesViewModel(
        ISalesQueryRepository queries,
        ISalesCheckoutApplicationService checkout,
        ISalesAuthorizationService authorization,
        ICashFundRepository cashFunds,
        IBankAccountRepository bankAccounts,
        ICurrentSessionAccessor sessionAccessor,
        ISalesPaymentDialogService paymentDialog,
        ISalesReceiptPresenter receiptPresenter)
    {
        _queries = queries;
        _checkout = checkout;
        _authorization = authorization;
        _cashFunds = cashFunds;
        _bankAccounts = bankAccounts;
        _sessionAccessor = sessionAccessor;
        _paymentDialog = paymentDialog;
        _receiptPresenter = receiptPresenter;

        BuySell = new BuySellWorkspaceViewModel(OnWorkspaceChanged);
        BuyOldGold = new BuyOldGoldWorkspaceViewModel(OnWorkspaceChanged);
        BuySell.FindProductAsync = code => FindProductAsync(code);

        NewCommand = new AsyncRelayCommand(NewAsync, () => !IsPosting);
        PreviousCommand = new AsyncRelayCommand(() => NavigateHistoryAsync(next: false), () => !IsPosting && _permissions.CanReadHistory);
        NextCommand = new AsyncRelayCommand(() => NavigateHistoryAsync(next: true), () => !IsPosting && _permissions.CanReadHistory);
        PayCommand = new AsyncRelayCommand(PayAsync, () => !IsPosting && !IsLoading && _permissions.CanPost);
        LookupCustomerCommand = new AsyncRelayCommand(ReloadCustomersAsync, () => !IsPosting);

        _ = InitializeAsync();
    }

    public BuySellWorkspaceViewModel BuySell { get; }
    public BuyOldGoldWorkspaceViewModel BuyOldGold { get; }

    public int MainTabIndex
    {
        get => _mainTabIndex;
        set
        {
            if (SetProperty(ref _mainTabIndex, value))
                OnWorkspaceChanged();
        }
    }

    public bool IsBuySellTab => MainTabIndex == 0;
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                ((AsyncRelayCommand)PayCommand).NotifyCanExecuteChanged();
        }
    }

    public bool IsPosting
    {
        get => _isPosting;
        private set
        {
            if (SetProperty(ref _isPosting, value))
            {
                ((AsyncRelayCommand)PayCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)NewCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public string SuccessMessage
    {
        get => _successMessage;
        private set => SetProperty(ref _successMessage, value);
    }

    public string? Notes
    {
        get => _notes;
        set
        {
            if (SetProperty(ref _notes, value))
                InvalidateRequestIdIfNeeded();
        }
    }

    public SalesPermissionSnapshot Permissions => _permissions;
    public bool CanChangeSalesperson => _permissions.CanChangeSalesperson;
    public SalesCheckoutResult? LastResult
    {
        get => _lastResult;
        private set => SetProperty(ref _lastResult, value);
    }

    public Guid ClientRequestId => _clientRequestId;

    public ICommand NewCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PayCommand { get; }
    public ICommand LookupCustomerCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            var session = RequireSession();
            _permissions = await _authorization.GetPermissionsAsync(session);
            OnPropertyChanged(nameof(Permissions));
            OnPropertyChanged(nameof(CanChangeSalesperson));

            var customers = await _queries.SearchCustomersAsync(string.Empty, session);
            var staff = await _queries.GetSalespeopleAsync(session);
            var materials = await _queries.GetOldGoldMaterialsAsync(session);
            var locations = await _queries.GetOldGoldLocationsAsync(session);
            _funds = await _cashFunds.GetActiveFundsAsync(session);
            _banks = await _bankAccounts.GetActiveAccountsAsync(session);

            Replace(BuySell.CustomerOptions, customers);
            Replace(BuySell.StaffOptions, staff);
            Replace(BuySell.OldGoldTypeOptions, materials);
            Replace(BuySell.OldGoldLocationOptions, locations);
            Replace(BuyOldGold.CustomerOptions, customers);
            Replace(BuyOldGold.StaffOptions, staff);
            Replace(BuyOldGold.OldGoldTypeOptions, materials);
            Replace(BuyOldGold.OldGoldLocationOptions, locations);

            var self = staff.FirstOrDefault(x => x.Id == session.UserId) ?? staff.FirstOrDefault();
            var walkIn = customers.FirstOrDefault(x => x.IsWalkIn);
            var named = customers.FirstOrDefault(x => !x.IsWalkIn);
            BuySell.SelectedCustomer = walkIn ?? customers.FirstOrDefault();
            BuySell.SelectedStaff = self;
            BuyOldGold.SelectedCustomer = named ?? customers.FirstOrDefault();
            BuyOldGold.SelectedStaff = self;
            BuySell.SelectedOldGoldLocation = locations.FirstOrDefault(x => x.IsDefault) ?? locations.FirstOrDefault();
            BuyOldGold.SelectedOldGoldLocation = BuySell.SelectedOldGoldLocation;
            BuySell.SelectedOldGoldMaterial = materials.FirstOrDefault();
            BuyOldGold.SelectedOldGoldMaterial = materials.FirstOrDefault();
            BuySell.CanChangeSalesperson = _permissions.CanChangeSalesperson;
            BuyOldGold.CanChangeSalesperson = _permissions.CanChangeSalesperson;

            // Infer sales-floor from first product lookup seed is unavailable; leave 0 until first product scan.
            _salesFloorLocationId = 0;
            BuySell.SellLines.Clear();
            BuySell.BuyLines.Clear();
            BuyOldGold.BuyLines.Clear();
            BuySell.RaiseTotalsChanged();
            BuyOldGold.RaiseTotalsChanged();
            RenewRequestId();
        }
        catch (Exception ex)
        {
            ErrorMessage = FriendlyError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task NewAsync()
    {
        var session = RequireSession();
        var self = BuySell.StaffOptions.FirstOrDefault(x => x.Id == session.UserId)
            ?? BuySell.StaffOptions.FirstOrDefault();
        BuySell.ResetForNew(BuySell.CustomerOptions.FirstOrDefault(x => x.IsWalkIn) ?? BuySell.SelectedCustomer, self);
        BuyOldGold.ResetForNew(BuyOldGold.CustomerOptions.FirstOrDefault(x => !x.IsWalkIn) ?? BuyOldGold.SelectedCustomer, self);
        LastResult = null;
        SuccessMessage = string.Empty;
        ErrorMessage = string.Empty;
        Notes = null;
        RenewRequestId();
        await Task.CompletedTask;
    }

    private async Task ReloadCustomersAsync()
    {
        var session = RequireSession();
        var customers = await _queries.SearchCustomersAsync(string.Empty, session);
        var selectedBuySell = BuySell.SelectedCustomer?.Id;
        var selectedOld = BuyOldGold.SelectedCustomer?.Id;
        Replace(BuySell.CustomerOptions, customers);
        Replace(BuyOldGold.CustomerOptions, customers);
        BuySell.SelectedCustomer = customers.FirstOrDefault(x => x.Id == selectedBuySell) ?? customers.FirstOrDefault();
        BuyOldGold.SelectedCustomer = customers.FirstOrDefault(x => x.Id == selectedOld) ?? customers.FirstOrDefault();
    }

    private async Task<SalesProductOption?> FindProductAsync(string code)
    {
        var session = RequireSession();
        var product = await _queries.FindAvailableProductAsync(code, session);
        if (product is not null && _salesFloorLocationId <= 0)
            _salesFloorLocationId = product.SourceLocationId;
        return product;
    }

    private async Task PayAsync()
    {
        ErrorMessage = string.Empty;
        SuccessMessage = string.Empty;
        try
        {
            var session = RequireSession();
            if (!_permissions.CanPost)
                throw new InvalidOperationException("Không có quyền ghi sổ bán hàng.");

            var totals = IsBuySellTab ? BuySell.ComputeTotals() : BuyOldGold.ComputeTotals();
            if (IsBuySellTab && BuySell.SellLines.Count == 0 && BuySell.BuyLines.Count == 0)
                throw new InvalidOperationException("Phải có ít nhất một dòng bán hoặc vàng cũ.");
            if (!IsBuySellTab && BuyOldGold.BuyLines.Count == 0)
                throw new InvalidOperationException("Phải có ít nhất một dòng vàng cũ.");
            if (IsBuySellTab && BuySell.SellLines.Count > 0 && _salesFloorLocationId <= 0)
                throw new InvalidOperationException("Chưa xác định được kho bán hàng từ thành phẩm.");

            SalesPaymentDialogResult? payment = null;
            if (totals.NetAmountVnd != 0)
            {
                var isPayout = totals.NetAmountVnd < 0;
                var customer = IsBuySellTab ? BuySell.SelectedCustomer : BuyOldGold.SelectedCustomer;
                payment = _paymentDialog.Show(new SalesPaymentDialogRequest(
                    totals.NetAmountVnd,
                    isPayout,
                    _permissions.CanApproveDebt,
                    AllowReceivable: customer is { IsWalkIn: false },
                    _funds,
                    _banks,
                    IsBuySellTab ? BuySell.CheckoutTypeLabel : BuyOldGold.CheckoutTypeLabel));
                if (payment is null)
                    return;
            }
            else
            {
                payment = new SalesPaymentDialogResult([], 0);
            }

            var draft = IsBuySellTab
                ? BuySell.BuildDraft(_clientRequestId, payment.Allocations, payment.ReceivableAmountVnd,
                    _salesFloorLocationId, Notes)
                : BuyOldGold.BuildDraft(_clientRequestId, payment.Allocations, Notes);

            IsPosting = true;
            _preserveRequestIdOnEdit = true;
            var result = await _checkout.PostAsync(draft, session);
            LastResult = result;
            SuccessMessage = result.AlreadyPosted
                ? $"Đã ghi sổ trước đó: {result.DocumentCode}"
                : $"Ghi sổ thành công: {result.DocumentCode}";
            if (IsBuySellTab)
                BuySell.DocumentCode = result.DocumentCode;
            else
                BuyOldGold.DocumentCode = result.DocumentCode;

            await _receiptPresenter.ShowAsync(result.CheckoutId);
            RenewRequestId();
            _preserveRequestIdOnEdit = false;
            if (IsBuySellTab)
                BuySell.ResetForNew(BuySell.SelectedCustomer, BuySell.SelectedStaff);
            else
                BuyOldGold.ResetForNew(BuyOldGold.SelectedCustomer, BuyOldGold.SelectedStaff);
        }
        catch (Exception ex)
        {
            ErrorMessage = FriendlyError(ex);
            _preserveRequestIdOnEdit = true;
        }
        finally
        {
            IsPosting = false;
        }
    }

    private async Task NavigateHistoryAsync(bool next)
    {
        try
        {
            var session = RequireSession();
            var currentId = LastResult?.CheckoutId;
            if (currentId is null or <= 0)
            {
                ErrorMessage = "Chưa có chứng từ để điều hướng.";
                return;
            }

            var adjacent = await _queries.GetAdjacentCheckoutIdAsync(currentId.Value, next, session);
            if (adjacent is null)
            {
                ErrorMessage = next ? "Không còn chứng từ sau." : "Không còn chứng từ trước.";
                return;
            }

            var detail = await _queries.GetCheckoutAsync(adjacent.Value, session);
            if (detail is null)
            {
                ErrorMessage = "Không tải được chứng từ.";
                return;
            }

            LastResult = new SalesCheckoutResult(
                detail.CheckoutId, detail.DocumentCode, detail.CheckoutType, detail.Totals,
                null, null, AlreadyPosted: true);
            SuccessMessage = $"Đang xem {detail.DocumentCode} (chỉ đọc).";
            if (detail.CheckoutType == SalesCheckoutTypes.OldGoldPurchase)
            {
                MainTabIndex = 1;
                BuyOldGold.DocumentCode = detail.DocumentCode;
                BuyOldGold.IsReadOnly = true;
            }
            else
            {
                MainTabIndex = 0;
                BuySell.DocumentCode = detail.DocumentCode;
                BuySell.IsReadOnly = true;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = FriendlyError(ex);
        }
    }

    private void OnWorkspaceChanged()
    {
        OnPropertyChanged(nameof(IsBuySellTab));
        InvalidateRequestIdIfNeeded();
    }

    private void InvalidateRequestIdIfNeeded()
    {
        if (_preserveRequestIdOnEdit)
        {
            RenewRequestId();
            _preserveRequestIdOnEdit = false;
        }
    }

    private void RenewRequestId() => _clientRequestId = Guid.NewGuid();

    private AuthenticatedSession RequireSession() =>
        _sessionAccessor.Current
        ?? throw new InvalidOperationException("Chưa đăng nhập.");

    private static void Replace<T>(System.Collections.ObjectModel.ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
            target.Add(item);
    }

    private static string FriendlyError(Exception ex)
    {
        var message = ex.Message;
        if (message.Contains("SQL", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Firebird", StringComparison.OrdinalIgnoreCase))
            return "Không thể hoàn tất giao dịch. Vui lòng kiểm tra dữ liệu và thử lại.";
        return message;
    }
}
