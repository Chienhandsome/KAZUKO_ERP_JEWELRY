using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Production;

public sealed class ProductionViewModel : ObservableObject
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly IProductionAttachmentStore _attachments;
    private readonly IProductionPrintDataService _printData;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private CancellationTokenSource? _loadCts;

    private string _searchText = string.Empty;
    private string _selectedStatusFilter = "Tất cả";
    private string _selectedSourceFilter = "Tất cả";
    private string _selectedExecutionFilter = "Tất cả";
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private bool _includeDrafts = true;
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private ProductionOrderRowViewModel? _selectedOrder;

    private int _inProgressCount;
    private int _completedPeriodCount;
    private int _outsourcedCount;
    private int _inHouseCount;

    public ProductionViewModel(
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        IProductionAttachmentStore attachments,
        IProductionPrintDataService printData,
        ICurrentSessionAccessor sessionAccessor)
    {
        _queries = queries;
        _application = application;
        _attachments = attachments;
        _printData = printData;
        _sessionAccessor = sessionAccessor;

        var today = DateTime.Today;
        _fromDate = new DateTime(today.Year, today.Month, 1);
        _toDate = today;

        StatusFilterOptions.Add("Tất cả");
        StatusFilterOptions.Add(ProductionOrderStatuses.Draft);
        StatusFilterOptions.Add(ProductionOrderStatuses.InProgress);
        StatusFilterOptions.Add(ProductionOrderStatuses.Completed);
        StatusFilterOptions.Add(ProductionOrderStatuses.Cancelled);

        SourceFilterOptions.Add("Tất cả");
        SourceFilterOptions.Add(ProductionOrderSources.Stock);
        SourceFilterOptions.Add(ProductionOrderSources.SalesOrder);

        ExecutionFilterOptions.Add("Tất cả");
        ExecutionFilterOptions.Add(ProductionExecutionTypes.InHouse);
        ExecutionFilterOptions.Add(ProductionExecutionTypes.Outsourced);

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        CreateOrderCommand = new AsyncRelayCommand(CreateOrderAsync);
        OpenDetailCommand = new ParameterRelayCommand(
            p => _ = OpenDetailAsync(p as ProductionOrderRowViewModel),
            p => p is ProductionOrderRowViewModel);

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
        set => SetProperty(ref _selectedOrder, value);
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

    public int CompletedPeriodCount
    {
        get => _completedPeriodCount;
        private set => SetProperty(ref _completedPeriodCount, value);
    }

    public int OutsourcedCount
    {
        get => _outsourcedCount;
        private set => SetProperty(ref _outsourcedCount, value);
    }

    public int InHouseCount
    {
        get => _inHouseCount;
        private set => SetProperty(ref _inHouseCount, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand CreateOrderCommand { get; }
    public ICommand OpenDetailCommand { get; }

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
            CompletedPeriodCount = summary.CompletedInPeriodCount;
            OutsourcedCount = summary.OutsourcedCount;
            InHouseCount = summary.InHouseCount;

            var filter = BuildFilter();
            var items = await _queries.SearchOrdersAsync(branchId, filter, token);

            Orders.Clear();
            foreach (var item in items)
                Orders.Add(new ProductionOrderRowViewModel(item));

            StatusMessage = $"Đã tải {Orders.Count} lệnh sản xuất.";
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
        string? status = SelectedStatusFilter is "Tất cả" ? null : SelectedStatusFilter;
        string? source = SelectedSourceFilter is "Tất cả" ? null : SelectedSourceFilter;
        string? execution = SelectedExecutionFilter is "Tất cả" ? null : SelectedExecutionFilter;
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
                DataContext = new ProductionOrderDialogViewModel(
                    null,
                    _queries,
                    _application,
                    _attachments,
                    _printData,
                    _sessionAccessor,
                    branchId)
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
                    branchId)
            };
            dialog.ShowDialog();
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
        StatusDisplay = ProductionOrderStatuses.ToListDisplay(item.StatusCode);
        SourceDisplay = item.OrderSource switch
        {
            ProductionOrderSources.Stock => "Nhập kho",
            ProductionOrderSources.SalesOrder => "Theo đơn bán",
            _ => item.OrderSource
        };
        ExecutionDisplay = item.ExecutionType switch
        {
            ProductionExecutionTypes.InHouse => "Nội bộ",
            ProductionExecutionTypes.Outsourced => "Thợ ngoài",
            _ => item.ExecutionType
        };
        SalesOrderCode = item.SalesOrderCode ?? "—";
        WorkerDisplayName = item.WorkerDisplayName ?? "—";
        ModelSummary = string.IsNullOrWhiteSpace(item.ModelSummary) ? "—" : item.ModelSummary;
        PlannedQuantityTotal = item.PlannedQuantityTotal;
        RowVersion = item.RowVersion;
        StatusCode = item.StatusCode;
    }

    public long Id { get; }
    public string Code { get; }
    public string OrderDateDisplay { get; }
    public string StatusDisplay { get; }
    public string SourceDisplay { get; }
    public string ExecutionDisplay { get; }
    public string SalesOrderCode { get; }
    public string WorkerDisplayName { get; }
    public string ModelSummary { get; }
    public int PlannedQuantityTotal { get; }
    public long RowVersion { get; }
    public string StatusCode { get; }
}
