using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Production;

public sealed class ProductionViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly IProductionAttachmentStore _attachments;
    private readonly IProductionPrintDataService _printData;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IExternalWorkerEditorService _externalWorkerEditor;
    private readonly IGoldTypeCatalogService _goldTypes;
    private readonly ICraftOrderApplicationService _craftOrders;
    private readonly ICashFundRepository _cashFunds;
    private readonly IBankAccountRepository _bankAccounts;
    private readonly ICustomerEditorService _customerEditor;
    private CancellationTokenSource? _loadCts;

    private string _selectedStatusFilter = "Tất cả trạng thái";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    // Redesign 14/10: filter theo target UI
    private string _orderCodeFilterText = string.Empty;
    private string _customerFilterText = string.Empty;
    private string _salesOrderFilterText = string.Empty;
    private string _workerFilterText = string.Empty;
    private string _modelFilterText = string.Empty;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private ProductionOrderRowViewModel? _selectedOrder;

    // M1 redesign (A1, B2): 3 thẻ KPI đếm trên toàn bộ — bỏ "Chờ đối trừ",
    // "Hoàn thành kỳ", "Thợ ngoài (đang làm)", "Nội bộ (đang làm)".
    private int _inProgressCount;
    private int _completedCount;
    private int _cancelledCount;

    public ProductionViewModel(
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        IProductionAttachmentStore attachments,
        IProductionPrintDataService printData,
        ICurrentSessionAccessor sessionAccessor,
        IExternalWorkerEditorService externalWorkerEditor,
        IGoldTypeCatalogService goldTypes,
        ICraftOrderApplicationService craftOrders,
        ICashFundRepository cashFunds,
        IBankAccountRepository bankAccounts,
        ICustomerEditorService customerEditor)
    {
        _queries = queries;
        _application = application;
        _attachments = attachments;
        _printData = printData;
        _sessionAccessor = sessionAccessor;
        _externalWorkerEditor = externalWorkerEditor;
        _goldTypes = goldTypes;
        _craftOrders = craftOrders;
        _cashFunds = cashFunds;
        _bankAccounts = bankAccounts;
        _customerEditor = customerEditor;

        var today = DateTime.Today;
        _fromDate = new DateTime(today.Year, today.Month, 1);
        _toDate = today;

        StatusFilterOptions.Add("Tất cả trạng thái");
        StatusFilterOptions.Add("Bản nháp");
        StatusFilterOptions.Add("Đang làm");
        StatusFilterOptions.Add("Hoàn thành");
        StatusFilterOptions.Add("Đã hủy");

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        ResetFiltersCommand = new AsyncRelayCommand(ResetFiltersAsync);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync);
        PrintOrderCommand = new AsyncRelayCommand(
            () => PrintOrderAsync(SelectedOrder),
            () => SelectedOrder is not null);
        ExportExcelCommand = new RelayCommand(ExportExcel);
        OpenDetailCommand = new ParameterRelayCommand(
            p => _ = OpenDetailAsync(p as ProductionOrderRowViewModel ?? SelectedOrder),
            p => (p as ProductionOrderRowViewModel ?? SelectedOrder) is not null);
        // C5: nút Sửa chỉ bật khi phiếu còn là bản nháp (chưa khóa sau xác nhận giao vàng).
        EditOrderCommand = new ParameterRelayCommand(
            p => _ = OpenDetailAsync(p as ProductionOrderRowViewModel ?? SelectedOrder),
            p => (p as ProductionOrderRowViewModel ?? SelectedOrder) is { IsDraft: true });
        DeleteOrderCommand = new ParameterRelayCommand(
            p => _ = DeleteOrderAsync(p as ProductionOrderRowViewModel ?? SelectedOrder),
            p => (p as ProductionOrderRowViewModel ?? SelectedOrder) is { CanDelete: true });
        CompleteOrderCommand = new ParameterRelayCommand(
            p => _ = CompleteOrderAsync(p as ProductionOrderRowViewModel ?? SelectedOrder),
            p => (p as ProductionOrderRowViewModel ?? SelectedOrder) is { IsInProgress: true });

        _ = LoadAsync();
    }

    public ObservableCollection<ProductionOrderRowViewModel> Orders { get; } = [];
    public ObservableCollection<string> StatusFilterOptions { get; } = [];

    private bool _suppressAutoSearch;

    private void TriggerFilterSearch()
    {
        if (!_suppressAutoSearch)
            _ = LoadAsync();
    }

    public string OrderCodeFilterText
    {
        get => _orderCodeFilterText;
        set
        {
            if (SetProperty(ref _orderCodeFilterText, value))
                TriggerFilterSearch();
        }
    }

    public string CustomerFilterText
    {
        get => _customerFilterText;
        set
        {
            if (SetProperty(ref _customerFilterText, value))
                TriggerFilterSearch();
        }
    }

    public string SalesOrderFilterText
    {
        get => _salesOrderFilterText;
        set
        {
            if (SetProperty(ref _salesOrderFilterText, value))
                TriggerFilterSearch();
        }
    }

    public string WorkerFilterText
    {
        get => _workerFilterText;
        set
        {
            if (SetProperty(ref _workerFilterText, value))
                TriggerFilterSearch();
        }
    }

    public string ModelFilterText
    {
        get => _modelFilterText;
        set
        {
            if (SetProperty(ref _modelFilterText, value))
                TriggerFilterSearch();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
                TriggerFilterSearch();
        }
    }

    public ProductionOrderRowViewModel? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
            {
                (OpenDetailCommand as ParameterRelayCommand)?.NotifyCanExecuteChanged();
                (EditOrderCommand as ParameterRelayCommand)?.NotifyCanExecuteChanged();
                (DeleteOrderCommand as ParameterRelayCommand)?.NotifyCanExecuteChanged();
                (CompleteOrderCommand as ParameterRelayCommand)?.NotifyCanExecuteChanged();
                (PrintOrderCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                if (value is not null)
                    PopulateFiltersFromOrder(value);
            }
        }
    }

    /// <summary>
    /// Điền thông tin phiếu đang chọn lên các field filter phía trên.
    /// </summary>
    private void PopulateFiltersFromOrder(ProductionOrderRowViewModel order)
    {
        _suppressAutoSearch = true;
        try
        {
            OrderCodeFilterText = order.Code ?? string.Empty;
            CustomerFilterText = order.CustomerName ?? string.Empty;
            SalesOrderFilterText = order.SalesOrderCode ?? string.Empty;
            WorkerFilterText = order.WorkerDisplayName ?? string.Empty;
            ModelFilterText = order.ModelSummary ?? string.Empty;
        }
        finally
        {
            _suppressAutoSearch = false;
        }
        // Không tự đổi Từ/Đến ngày và Trạng thái để tránh làm hẹp kết quả lọc bất ngờ
        // Không tự kích hoạt tìm kiếm — người dùng bấm "Tìm kiếm" khi cần
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    public int InProgressCount
    {
        get => _inProgressCount;
        private set => SetProperty(ref _inProgressCount, value);
    }

    public int CompletedCount
    {
        get => _completedCount;
        private set => SetProperty(ref _completedCount, value);
    }

    public int CancelledCount
    {
        get => _cancelledCount;
        private set => SetProperty(ref _cancelledCount, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand CreateOrderCommand { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand EditOrderCommand { get; }
    public ICommand DeleteOrderCommand { get; }
    public ICommand CompleteOrderCommand { get; }
    public ICommand PrintOrderCommand { get; }
    public ICommand ExportExcelCommand { get; }

    private long RequireBranchId()
    {
        var branchId = _sessionAccessor.Current?.BranchId ?? 0;
        if (branchId <= 0)
            throw new InvalidOperationException("Phiên làm việc thiếu chi nhánh.");
        return branchId;
    }

    private async Task LoadAsync()
    {
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        try
        {
            IsLoading = true;
            ErrorMessage = string.Empty;
            var branchId = RequireBranchId();

            var summary = await _queries.GetSummaryAsync(branchId, _fromDate, _toDate, token);
            InProgressCount = summary.InProgressCount;
            CompletedCount = summary.CompletedCount;
            CancelledCount = summary.CancelledCount;

            var filter = BuildFilter();
            var items = await _queries.SearchOrdersAsync(branchId, filter, token);

            Orders.Clear();
            var index = 1;
            foreach (var item in items)
                Orders.Add(new ProductionOrderRowViewModel(item, index++));

            StatusMessage = string.Empty;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = string.Empty;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private ProductionOrderListFilter BuildFilter()
    {
        string? status = SelectedStatusFilter switch
        {
            "Bản nháp" => ProductionOrderStatuses.Draft,
            "Đang làm" => ProductionOrderStatuses.InProgress,
            "Hoàn thành" => ProductionOrderStatuses.Completed,
            "Đã hủy" => ProductionOrderStatuses.Cancelled,
            _ => null
        };
        return new ProductionOrderListFilter(
            SearchText: null,
            StatusCode: status,
            OrderSource: null,
            ExecutionType: null,
            FromDate: _fromDate,
            ToDate: _toDate,
            IncludeDrafts: true,
            OrderCode: string.IsNullOrWhiteSpace(OrderCodeFilterText) ? null : OrderCodeFilterText.Trim(),
            CustomerName: string.IsNullOrWhiteSpace(CustomerFilterText) ? null : CustomerFilterText.Trim(),
            SalesOrderCode: string.IsNullOrWhiteSpace(SalesOrderFilterText) ? null : SalesOrderFilterText.Trim(),
            WorkerName: string.IsNullOrWhiteSpace(WorkerFilterText) ? null : WorkerFilterText.Trim(),
            ModelName: string.IsNullOrWhiteSpace(ModelFilterText) ? null : ModelFilterText.Trim());
    }

    private async Task ResetFiltersAsync()
    {
        var today = DateTime.Today;
        _fromDate = new DateTime(today.Year, today.Month, 1);
        _toDate = today;
        SelectedStatusFilter = "Tất cả trạng thái";
        OrderCodeFilterText = string.Empty;
        CustomerFilterText = string.Empty;
        SalesOrderFilterText = string.Empty;
        WorkerFilterText = string.Empty;
        ModelFilterText = string.Empty;
        await LoadAsync();
    }

    private async Task PrintOrderAsync(ProductionOrderRowViewModel? row)
    {
        if (row is null)
            return;

        try
        {
            ErrorMessage = string.Empty;
            var dto = await _printData.GetOrderPrintAsync(row.Id);
            if (dto is null)
            {
                ErrorMessage = $"Không lấy được dữ liệu in cho lệnh {row.Code}.";
                return;
            }
            new ProductionPrintService().PrintOrder(dto);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void ExportExcel()
    {
        MessageBox.Show(
            "Chức năng xuất Excel chưa được hỗ trợ trong phiên bản này.",
            "Thông báo",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async Task CreateOrderAsync()
    {
        try
        {
            var branchId = RequireBranchId();
            var dialog = new ProductionOrderDialog
            {
                Owner = Application.Current.MainWindow,
                WindowState = WindowState.Maximized,
                DataContext = new ProductionOrderDialogViewModel(
                    null,
                    _queries,
                    _application,
                    _attachments,
                    _printData,
                    _sessionAccessor,
                    _externalWorkerEditor,
                    _goldTypes,
                    branchId,
                    _craftOrders,
                    _cashFunds,
                    _bankAccounts,
                    _customerEditor)
            };
            dialog.ShowDialog();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task OpenDetailAsync(ProductionOrderRowViewModel? row)
    {
        if (row is null)
            return;

        try
        {
            var branchId = RequireBranchId();
            var dialog = new ProductionOrderDialog
            {
                Owner = Application.Current.MainWindow,
                DataContext = new ProductionOrderDialogViewModel(
                    row.Id,
                    _queries,
                    _application,
                    _attachments,
                    _printData,
                    _sessionAccessor,
                    _externalWorkerEditor,
                    _goldTypes,
                    branchId,
                    _craftOrders,
                    _cashFunds,
                    _bankAccounts,
                    _customerEditor)
            };
            dialog.ShowDialog();
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task DeleteOrderAsync(ProductionOrderRowViewModel? row)
    {
        if (row is null || !row.CanDelete)
            return;

        var session = _sessionAccessor.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        var prompt = row.IsDraft
            ? $"Xóa/hủy lệnh bản nháp {row.Code}? Thao tác không hoàn tác."
            : $"Hủy lệnh đang làm {row.Code}? Chỉ hủy được khi chưa nhập thành phẩm/công nợ và NVL đã đối soát về 0.";

        if (MessageBox.Show(
                prompt,
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            ErrorMessage = string.Empty;
            await _application.CancelAsync(row.Id, row.RowVersion, null, session);
            StatusMessage = $"Đã hủy lệnh {row.Code}.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task CompleteOrderAsync(ProductionOrderRowViewModel? row)
    {
        if (row is null || !row.IsInProgress)
            return;

        var session = _sessionAccessor.Current
            ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

        if (MessageBox.Show(
                $"Hoàn tất lệnh {row.Code}? Thành phẩm sẽ được nhập kho ở bước riêng. Công nợ tiền công thợ ngoài sẽ được ghi nhận.",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            ErrorMessage = string.Empty;
            var result = await _application.CompleteAsync(row.Id, row.RowVersion, session);
            StatusMessage = $"Đã hoàn tất {result.Code} ({result.FinishedProductCount} món). Chọn lệnh này tại Kho → Nhập thành phẩm để ghi tồn kho.";
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

public sealed class ProductionOrderRowViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");
    private bool _isSelected;

    public ProductionOrderRowViewModel(ProductionOrderListItem item, int index)
    {
        Index = index;
        Id = item.Id;
        Code = item.Code;
        OrderDateDisplay = item.OrderDate.ToString("dd/MM/yyyy", ViCulture);
        // A3: "Ngày giao" = ngày giao NVL/vàng cho thợ (từ chứng từ xuất kho NVL);
        // trống nếu phiếu chưa giao.
        MaterialIssueDateDisplay = item.MaterialIssueDate?.ToString("dd/MM/yyyy", ViCulture) ?? "—";
        DueDateDisplay = item.DueDate?.ToString("dd/MM/yyyy", ViCulture) ?? "—";
        StatusDisplay = ProductionOrderStatuses.ToListDisplay(item.StatusCode);
        SourceDisplay = item.OrderSource switch
        {
            ProductionOrderSources.Stock => "Chế tác nhập kho",
            ProductionOrderSources.SalesOrder => "Chế tác theo đơn",
            _ => item.OrderSource
        };
        ExecutionDisplay = item.ExecutionType switch
        {
            ProductionExecutionTypes.InHouse => "Nội bộ",
            ProductionExecutionTypes.Outsourced => "Thợ ngoài",
            _ => item.ExecutionType
        };
        SalesOrderCode = item.SalesOrderCode ?? "—";
        CustomerName = item.CustomerName ?? "—";
        WorkerDisplayName = item.WorkerDisplayName ?? "—";
        ModelSummary = string.IsNullOrWhiteSpace(item.ModelSummary) ? "—" : item.ModelSummary;
        PlannedQuantityTotal = item.PlannedQuantityTotal;
        RowVersion = item.RowVersion;
        StatusCode = item.StatusCode;
        IsDraft = string.Equals(item.StatusCode, ProductionOrderStatuses.Draft, StringComparison.OrdinalIgnoreCase);
        IsInProgress = string.Equals(item.StatusCode, ProductionOrderStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
        CanDelete = IsDraft || IsInProgress;
    }

    public long Id { get; }
    public int Index { get; }
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    public string Code { get; }
    public string OrderDateDisplay { get; }
    public string MaterialIssueDateDisplay { get; }
    public string DueDateDisplay { get; }
    public string StatusDisplay { get; }
    public string SourceDisplay { get; }
    public string ExecutionDisplay { get; }
    public string SalesOrderCode { get; }
    public string CustomerName { get; }
    public string WorkerDisplayName { get; }
    public string ModelSummary { get; }
    public int PlannedQuantityTotal { get; }
    public long RowVersion { get; }
    public string StatusCode { get; }
    public bool IsDraft { get; }
    public bool IsInProgress { get; }
    public bool CanDelete { get; }
}
