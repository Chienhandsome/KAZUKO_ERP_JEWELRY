using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales;

public sealed class CraftOrderWorkspaceViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ICraftOrderApplicationService _craftOrders;
    private readonly IWarehouseRepository _warehouse;
    private readonly Func<AuthenticatedSession> _requireSession;
    private readonly Func<SalesPaymentDialogRequest, SalesPaymentDialogResult?> _showPayment;
    private readonly Func<string?, ProductionOrderEditResult?> _createProductionOrder;
    private readonly Action _onChanged;

    private DateTime _fromDate = DateTime.Today.AddDays(-90);
    private DateTime _toDate = DateTime.Today;
    private string _searchText = string.Empty;
    private string _statusFilter = string.Empty;
    private CraftOrderListItem? _selectedItem;
    private CraftOrderHeader? _selectedHeader;
    private bool _isLoading;
    private bool _canCollect;
    private bool _canCreateOrder;
    private string _statusMessage = string.Empty;
    private int _searchVersion;
    private int _detailVersion;
    private bool _hasStartedLoading;
    private long _resultOrderId;

    public CraftOrderWorkspaceViewModel(
        ICraftOrderApplicationService craftOrders,
        IWarehouseRepository warehouse,
        Func<AuthenticatedSession> requireSession,
        Func<SalesPaymentDialogRequest, SalesPaymentDialogResult?> showPayment,
        Func<string?, ProductionOrderEditResult?> createProductionOrder,
        Action? onChanged = null)
    {
        _craftOrders = craftOrders ?? throw new ArgumentNullException(nameof(craftOrders));
        _warehouse = warehouse ?? throw new ArgumentNullException(nameof(warehouse));
        _requireSession = requireSession ?? throw new ArgumentNullException(nameof(requireSession));
        _showPayment = showPayment ?? throw new ArgumentNullException(nameof(showPayment));
        _createProductionOrder = createProductionOrder
            ?? throw new ArgumentNullException(nameof(createProductionOrder));
        _onChanged = onChanged ?? (() => { });

        SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
        CreateOrderCommand = new RelayCommand(CreateOrder, () => !IsLoading && CanCreateOrder);
        DeliverCommand = new AsyncRelayCommand(DeliverAsync, () => !IsLoading && CanDeliverSelected);
        CollectCommand = new AsyncRelayCommand(CollectAsync, () => !IsLoading && CanCollectSelected);
        CheckResultCommand=new AsyncRelayCommand(CheckDeliveryResultAsync,()=>!IsLoading&&(SelectedHeader is not null||_resultOrderId>0));
    }

    public ObservableCollection<CraftOrderListItem> Items { get; } = [];
    public ICommand CheckResultCommand{get;}

    private async Task CheckDeliveryResultAsync()
    {
        try{await OpenOrderAsync(SelectedHeader?.Id??_resultOrderId);}
        catch(Exception ex){StatusMessage="Chưa kiểm tra được kết quả giao hàng. Không giao lại khi chưa xác minh. "+ex.Message;}
    }

    public async Task OpenOrderAsync(long orderId)
    {
        if(orderId<=0)return;
        _resultOrderId=orderId;
        IsLoading=true;
        try
        {
            await LoadDetailAsync(orderId);
            if(SelectedHeader is null)throw new InvalidOperationException("Không tìm thấy đơn đặt hoặc chưa có quyền xem.");
            StatusMessage=SelectedHeader.HasFinishedGoodsIssue?"Đã xuất thành phẩm. Kiểm tra trạng thái đơn và số tiền còn lại; không giao lại.":"Chưa có phiếu xuất thành phẩm. Có thể giao sau khi kiểm tra đủ món và thanh toán.";
        }
        finally{IsLoading=false;((AsyncRelayCommand)CheckResultCommand).NotifyCanExecuteChanged();}
    }
    public ObservableCollection<CraftOrderLineItem> Lines { get; } = [];

    public IReadOnlyList<CraftOrderStatusFilterOption> StatusFilterOptions { get; } =
    [
        new(string.Empty, "Tất cả trạng thái"),
        new(CraftOrderStatuses.Deposited, "Đã cọc"),
        new(CraftOrderStatuses.InProduction, "Đang SX"),
        new(CraftOrderStatuses.Ready, "Chờ giao / tất toán"),
        new(CraftOrderStatuses.Completed, "Hoàn tất"),
        new(CraftOrderStatuses.Cancelled, "Đã hủy")
    ];

    public DateTime FromDate
    {
        get => _fromDate;
        set { if (SetProperty(ref _fromDate, value.Date)) ScheduleFilterRefresh(); }
    }

    public DateTime ToDate
    {
        get => _toDate;
        set { if (SetProperty(ref _toDate, value.Date)) ScheduleFilterRefresh(); }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ScheduleFilterRefresh(); }
    }

    public string StatusFilter
    {
        get => _statusFilter;
        set
        {
            if (!SetProperty(ref _statusFilter, value))
                return;
            OnPropertyChanged(nameof(SelectedStatusFilter));
            ScheduleFilterRefresh();
        }
    }

    public CraftOrderStatusFilterOption? SelectedStatusFilter
    {
        get => StatusFilterOptions.FirstOrDefault(x =>
            string.Equals(x.Code, StatusFilter, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
                return;
            StatusFilter = value.Code;
        }
    }

    public CraftOrderListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetProperty(ref _selectedItem, value))
                return;
            _ = LoadDetailAsync(value?.Id);
            _onChanged();
        }
    }

    public CraftOrderHeader? SelectedHeader
    {
        get => _selectedHeader;
        private set
        {
            if (!SetProperty(ref _selectedHeader, value))
                return;
            OnPropertyChanged(nameof(HasDetail));
            OnPropertyChanged(nameof(DetailTitle));
            OnPropertyChanged(nameof(DetailCustomerText));
            OnPropertyChanged(nameof(DetailStatusText));
            OnPropertyChanged(nameof(GrandTotalText));
            OnPropertyChanged(nameof(PaidAmountText));
            OnPropertyChanged(nameof(RemainingDueText));
            OnPropertyChanged(nameof(IssueStatusText));
            OnPropertyChanged(nameof(SettlementHintText));
            OnPropertyChanged(nameof(HasLines));
            OnPropertyChanged(nameof(CanDeliverSelected));
            OnPropertyChanged(nameof(CanCollectSelected));
            ((AsyncRelayCommand)DeliverCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CollectCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CheckResultCommand).NotifyCanExecuteChanged();
            _onChanged();
        }
    }

    public bool HasDetail => SelectedHeader is not null;
    public string DetailTitle => SelectedHeader is null ? "Chi tiết đơn đặt" : $"Đơn {SelectedHeader.Code}";
    public string DetailCustomerText => SelectedHeader is null
        ? string.Empty
        : string.IsNullOrWhiteSpace(SelectedHeader.CustomerPhone)
            ? $"{SelectedHeader.CustomerCode} — {SelectedHeader.CustomerName}"
            : $"{SelectedHeader.CustomerCode} — {SelectedHeader.CustomerName} ({SelectedHeader.CustomerPhone})";
    public string DetailStatusText => SelectedHeader?.StatusCode ?? string.Empty;
    public string GrandTotalText => FormatVnd(SelectedHeader?.GrandTotalVnd ?? 0);
    public string PaidAmountText => FormatVnd(SelectedHeader?.DepositPaidVnd ?? 0);
    public string RemainingDueText => FormatVnd(SelectedHeader?.RemainingDueVnd ?? 0);
    public string IssueStatusText => SelectedHeader is null
        ? string.Empty
        : SelectedHeader.HasFinishedGoodsIssue
            ? "Đã xuất kho / giao"
            : "Chưa xuất kho";
    public string SettlementHintText
    {
        get
        {
            if (SelectedHeader is null)
                return string.Empty;
            var paid = SelectedHeader.DepositPaidVnd;
            var total = SelectedHeader.GrandTotalVnd;
            if (total <= 0)
                return string.Empty;
            var pct = total > 0 ? Math.Round(paid * 100m / total, 0, MidpointRounding.AwayFromZero) : 0m;
            return $"Đã thu {FormatVnd(paid)} / tổng báo giá {FormatVnd(total)} (~{pct:N0}%).";
        }
    }
    public bool HasLines => Lines.Count > 0;

    public bool CanCreateOrder
    {
        get => _canCreateOrder;
        set
        {
            if (SetProperty(ref _canCreateOrder, value))
                ((RelayCommand)CreateOrderCommand).NotifyCanExecuteChanged();
        }
    }

    public bool CanCollect
    {
        get => _canCollect;
        set
        {
            if (SetProperty(ref _canCollect, value))
            {
                OnPropertyChanged(nameof(CanDeliverSelected));
                OnPropertyChanged(nameof(CanCollectSelected));
                ((AsyncRelayCommand)DeliverCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)CollectCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanDeliverSelected =>
        CanCollect && SelectedHeader?.CanDeliver == true;

    public bool CanCollectSelected =>
        CanCollect && SelectedHeader?.CanCollectRemaining == true;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value))
                return;
            ((AsyncRelayCommand)CheckResultCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)SearchCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CreateOrderCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)DeliverCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)CollectCommand).NotifyCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public ICommand SearchCommand { get; }
    public ICommand CreateOrderCommand { get; }
    public ICommand DeliverCommand { get; }
    public ICommand CollectCommand { get; }

    public Func<IReadOnlyList<Core.Finance.CashFundOption>> GetCashFunds { get; set; } = () => [];
    public Func<IReadOnlyList<Core.Finance.BankAccountOption>> GetBankAccounts { get; set; } = () => [];
    public Func<bool> GetCanApproveDebt { get; set; } = () => false;
    public Action<string>? ReportSuccess { get; set; }
    public Action<string>? ReportError { get; set; }

    public async Task EnsureLoadedAsync()
    {
        if (Items.Count == 0 && !IsLoading)
            await SearchAsync();
    }

    private void CreateOrder()
    {
        try
        {
            ErrorClear();
            var result = _createProductionOrder(ProductionOrderSources.SalesOrder);
            if (result is null)
                return;
            ReportSuccess?.Invoke(
                result.DialogConfirmed
                    ? $"Đã tạo đơn đặt {result.Code}. Tạo lệnh tại Sản xuất và chọn đơn này."
                    : "Đã hủy tạo đơn đặt.");
            _ = SearchAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ReportError?.Invoke(ex.Message);
        }
    }

    private void ScheduleFilterRefresh()
    {
        // Do not query an unopened tab (including users without craft-order access).
        if (!_hasStartedLoading)
            return;
        // Invalidate in-flight results immediately, before the debounce expires.
        var version = ++_searchVersion;
        _ = RefreshFiltersAfterDelayAsync(version);
    }

    private async Task RefreshFiltersAfterDelayAsync(int version)
    {
        await Task.Delay(300);
        if (version == _searchVersion)
            await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _hasStartedLoading = true;
        var version = ++_searchVersion;
        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;
            var session = _requireSession();
            var rows = await _craftOrders.SearchOrdersAsync(
                new CraftOrderSearchFilter(FromDate, ToDate, SearchText, StatusFilter),
                session);
            if (version != _searchVersion)
                return;

            Items.Clear();
            foreach (var row in rows)
                Items.Add(row);

            SelectedItem = Items.FirstOrDefault(x => x.Id == SelectedItem?.Id) ?? Items.FirstOrDefault();
            StatusMessage = Items.Count == 0
                ? "Không có đơn đặt phù hợp."
                : $"Tìm thấy {Items.Count} đơn đặt.";
        }
        catch (Exception ex)
        {
            if (version != _searchVersion)
                return;
            StatusMessage = ex.Message;
            ReportError?.Invoke(ex.Message);
        }
        finally
        {
            if (version == _searchVersion)
                IsLoading = false;
        }
    }

    private async Task LoadDetailAsync(long? salesOrderId)
    {
        var version = ++_detailVersion;
        if (salesOrderId is null or <= 0)
        {
            SelectedHeader = null;
            Lines.Clear();
            OnPropertyChanged(nameof(HasLines));
            return;
        }

        try
        {
            var session = _requireSession();
            var header = await _craftOrders.GetHeaderAsync(salesOrderId.Value, session);
            var lines = await _craftOrders.GetLinesAsync(salesOrderId.Value, session);
            if (version != _detailVersion)
                return;
            SelectedHeader = header;
            Lines.Clear();
            foreach (var line in lines)
                Lines.Add(line);
            OnPropertyChanged(nameof(HasLines));
        }
        catch (Exception ex)
        {
            if (version != _detailVersion)
                return;
            SelectedHeader = null;
            Lines.Clear();
            OnPropertyChanged(nameof(HasLines));
            StatusMessage = ex.Message;
            ReportError?.Invoke(ex.Message);
        }
    }

    private async Task DeliverAsync()
    {
        if (SelectedHeader is null || !CanDeliverSelected)
            return;
        var header = SelectedHeader;

        try
        {
            IsLoading = true;
            ErrorClear();
            var session = _requireSession();
            var candidates = await _warehouse.SearchIssueCandidatesAsync(header.Id, session);
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    "Đơn chưa có thành phẩm trong kho để giao. Kiểm tra lệnh SX đã hoàn tất chưa.");

            var locations = await _warehouse.GetLocationsAsync(session);
            var candidateLocations = candidates.Select(x => x.LocationId).Distinct().ToArray();
            if (candidateLocations.Length != 1 || candidateLocations[0] is null or <= 0)
                throw new InvalidOperationException(
                    "Thành phẩm của đơn đang ở nhiều kho hoặc chưa có vị trí. Vui lòng tập trung đủ món về Kho thành phẩm trước khi giao.");
            var locationId = candidateLocations[0]!.Value;
            var location = locations.FirstOrDefault(x => x.Id == locationId
                && x.InventoryScope is InventoryScopes.FinishedGoods or InventoryScopes.SalesFloor);
            if (location is null)
                throw new InvalidOperationException("Chỉ giao từ Kho thành phẩm hoặc Kho bán hàng của chi nhánh hiện tại.");

            var lines = candidates.ToList();
            var locationName = location.DisplayName;
            var confirm = MessageBox.Show(
                $"Giao {lines.Count} món tại {locationName}?\n"
                + string.Join("\n", lines.Take(8).Select(x => $"• {x.Code} — {x.ProductName}"))
                + (lines.Count > 8 ? $"\n… và {lines.Count - 8} món khác" : string.Empty)
                + (header.RemainingDueVnd > 0
                    ? $"\n\nCòn lại {FormatVnd(header.RemainingDueVnd)} — sẽ thu / ghi nợ sau khi xác nhận."
                    : "\n\nĐơn đã thu đủ cọc — chỉ xuất kho và hoàn tất."),
                "Giao hàng đơn đặt",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes)
                return;

            IReadOnlyList<Core.Finance.PaymentAllocationDraft> collections =
                Array.Empty<Core.Finance.PaymentAllocationDraft>();
            var receivable = 0m;
            if (header.RemainingDueVnd > 0)
            {
                var payment = _showPayment(new SalesPaymentDialogRequest(
                    header.RemainingDueVnd,
                    IsPayout: false,
                    CanApproveDebt: GetCanApproveDebt(),
                    AllowReceivable: true,
                    GetCashFunds(),
                    GetBankAccounts(),
                    $"Giao hàng / thu phần còn {header.Code}"));
                if (payment is null)
                    return;
                collections = payment.Allocations;
                receivable = payment.ReceivableAmountVnd;
            }

            var result = await _craftOrders.DeliverAsync(
                new CraftOrderDeliverDraft(
                    header.Id,
                    DateTime.Now,
                    locationId,
                    session.DisplayName,
                    header.CustomerName,
                    null,
                    lines.Select(x => x.Id).ToList(),
                    collections,
                    receivable),
                session);

            ReportSuccess?.Invoke(
                $"Đã giao {result.OrderCode}"
                + (result.CollectedAmountVnd > 0 ? $", thu {FormatVnd(result.CollectedAmountVnd)}" : string.Empty)
                + (result.ReceivableAmountVnd > 0 ? $", công nợ {FormatVnd(result.ReceivableAmountVnd)}" : string.Empty)
                + ".");
            await SearchAsync();
        }
        catch (Exception ex)
        {
            _resultOrderId=header.Id;
            StatusMessage = "Chưa xác minh được kết quả giao hàng. Bấm Kiểm tra kết quả trước khi giao lại. "+ex.Message;
            ReportError?.Invoke(StatusMessage);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CollectAsync()
    {
        if (SelectedHeader is null || !CanCollectSelected)
            return;

        try
        {
            IsLoading = true;
            ErrorClear();
            var remaining = SelectedHeader.RemainingDueVnd;
            var payment = _showPayment(new SalesPaymentDialogRequest(
                remaining,
                IsPayout: false,
                CanApproveDebt: GetCanApproveDebt(),
                AllowReceivable: true,
                GetCashFunds(),
                GetBankAccounts(),
                $"Thu phần còn đơn {SelectedHeader.Code}"));
            if (payment is null)
                return;

            var session = _requireSession();
            var result = await _craftOrders.CollectRemainingAsync(
                new CraftOrderCollectDraft(
                    SelectedHeader.Id,
                    DateTime.Today,
                    payment.Allocations,
                    payment.ReceivableAmountVnd,
                    null),
                session);

            ReportSuccess?.Invoke(
                $"Đã tất toán {result.OrderCode}: thu {FormatVnd(result.CollectedAmountVnd)}"
                + (result.ReceivableAmountVnd > 0
                    ? $", công nợ {FormatVnd(result.ReceivableAmountVnd)}"
                    : string.Empty)
                + ".");
            await SearchAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
            ReportError?.Invoke(ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ErrorClear()
    {
        StatusMessage = string.Empty;
    }

    private static string FormatVnd(decimal amount) =>
        amount.ToString("N0", Vi) + " đ";
}

public sealed record CraftOrderStatusFilterOption(string Code, string DisplayName);
