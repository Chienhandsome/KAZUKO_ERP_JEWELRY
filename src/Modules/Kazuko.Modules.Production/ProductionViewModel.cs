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

    private string _searchText = string.Empty;
    private string _selectedStatusFilter = "Tất cả trạng thái";
    private string _selectedSourceFilter = "Tất cả nguồn";
    private string _selectedExecutionFilter = "Tất cả hình thức";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private bool _includeDrafts = true;
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

        SourceFilterOptions.Add("Tất cả nguồn");
        SourceFilterOptions.Add("Chế tác nhập kho");
        SourceFilterOptions.Add("Chế tác theo đơn");

        ExecutionFilterOptions.Add("Tất cả hình thức");
        ExecutionFilterOptions.Add("Nội bộ");
        ExecutionFilterOptions.Add("Thợ ngoài");

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync);
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
    public ObservableCollection<string> SourceFilterOptions { get; } = [];
    public ObservableCollection<string> ExecutionFilterOptions { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                _ = LoadAsync();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
                _ = LoadAsync();
        }
    }

    public string SelectedSourceFilter
    {
        get => _selectedSourceFilter;
        set
        {
            if (SetProperty(ref _selectedSourceFilter, value))
                _ = LoadAsync();
        }
    }

    public string SelectedExecutionFilter
    {
        get => _selectedExecutionFilter;
        set
        {
            if (SetProperty(ref _selectedExecutionFilter, value))
                _ = LoadAsync();
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
                _ = LoadAsync();
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
                _ = LoadAsync();
        }
    }

    public bool IncludeDrafts
    {
        get => _includeDrafts;
        set
        {
            if (SetProperty(ref _includeDrafts, value))
                _ = LoadAsync();
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
            }
        }
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
    public ICommand CreateOrderCommand { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand EditOrderCommand { get; }
    public ICommand DeleteOrderCommand { get; }
    public ICommand CompleteOrderCommand { get; }

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

            var summary = await _queries.GetSummaryAsync(branchId, FromDate, ToDate, token);
            InProgressCount = summary.InProgressCount;
            CompletedCount = summary.CompletedCount;
            CancelledCount = summary.CancelledCount;

            var filter = BuildFilter();
            var items = await _queries.SearchOrdersAsync(branchId, filter, token);

            Orders.Clear();
            foreach (var item in items)
                Orders.Add(new ProductionOrderRowViewModel(item));

            // B5: footer chỉ hiện tổng toàn bộ danh sách đã lọc (không phân trang).
            StatusMessage = $"Tổng số phiếu: {Orders.Count}.";
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
        string? source = SelectedSourceFilter switch
        {
            "Chế tác nhập kho" => ProductionOrderSources.Stock,
            "Chế tác theo đơn" => ProductionOrderSources.SalesOrder,
            _ => null
        };
        string? execution = SelectedExecutionFilter switch
        {
            "Nội bộ" => ProductionExecutionTypes.InHouse,
            "Thợ ngoài" => ProductionExecutionTypes.Outsourced,
            _ => null
        };
        return new ProductionOrderListFilter(
            SearchText,
            status,
            source,
            execution,
            FromDate,
            ToDate,
            IncludeDrafts);
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

public sealed class ProductionOrderRowViewModel
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    public ProductionOrderRowViewModel(ProductionOrderListItem item)
    {
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
