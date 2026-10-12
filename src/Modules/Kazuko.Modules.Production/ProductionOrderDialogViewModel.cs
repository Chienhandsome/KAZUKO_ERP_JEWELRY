using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Core.Finance;
using Kazuko.Core.Inventory;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Production.Dialogs;
using Microsoft.Win32;

namespace Kazuko.Modules.Production;

public sealed class ProductionOrderDialogViewModel : ObservableObject
{
    private readonly long? _orderId;
    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly IProductionAttachmentStore _attachments;
    private readonly IProductionPrintDataService _printData;
    private readonly ProductionPrintService _printer = new();
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IExternalWorkerEditorService _externalWorkerEditor;
    private readonly long _branchId;

    private long? _id;
    private string _code = string.Empty;
    private string _orderSource = ProductionOrderSources.Stock;
    private long? _salesOrderId;
    private string _executionType = ProductionExecutionTypes.InHouse;
    private long? _externalWorkerId;
    private DateTime _orderDate = DateTime.Today;
    private DateTime? _dueDate;
    private DateTime? _issueDate;
    private long _sourceLocationId;
    private long _finishedLocationId;
    private string? _notes;
    private long _rowVersion;
    private string _statusCode = ProductionOrderStatuses.Draft;
    private int _selectedTabIndex;
    private string _errorMessage = string.Empty;
    private bool _isBusy;
    private bool _isLoaded;
    private readonly List<ProductionAttachmentEditorViewModel> _removedAttachments = [];

    private ProductionLineEditorViewModel? _selectedLine;
    private ProductionGalleryItemViewModel? _selectedGalleryImage;
    private ProductionMaterialStockOption? _selectedStockMaterial;
    private decimal _issueQuantity = 1m;
    private bool _hasFinishedReceipt;
    private string _completionPreview = string.Empty;

    private readonly IGoldTypeCatalogService _goldTypes;
    private readonly ICraftOrderApplicationService? _craftOrders;
    private readonly ICashFundRepository? _cashFunds;
    private readonly IBankAccountRepository? _bankAccounts;
    private readonly ICustomerEditorService? _customerEditor;

    private string _customerSearch = string.Empty;
    private CraftCustomerOption? _selectedCustomer;
    private bool _isCustomerSuggestionsOpen;
    private bool _suppressCustomerSearch;
    private CancellationTokenSource? _customerSearchCts;
    private string _craftOrderCode = string.Empty;
    private string _craftOrderStatusCode = string.Empty;
    private decimal _depositPaidAmount;
    private decimal _depositAmount;
    private string _depositPaymentMethod = PaymentMethods.Cash;
    private CashFundOption? _selectedDepositCashFund;
    private BankAccountOption? _selectedDepositBankAccount;

    public ProductionOrderDialogViewModel(
        long? orderId,
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        IProductionAttachmentStore attachments,
        IProductionPrintDataService printData,
        ICurrentSessionAccessor sessionAccessor,
        IExternalWorkerEditorService externalWorkerEditor,
        IGoldTypeCatalogService goldTypes,
        long branchId,
        ICraftOrderApplicationService? craftOrders = null,
        ICashFundRepository? cashFunds = null,
        IBankAccountRepository? bankAccounts = null,
        ICustomerEditorService? customerEditor = null,
        string? preferredOrderSource = null)
    {
        _orderId = orderId;
        _queries = queries;
        _application = application;
        _attachments = attachments;
        _printData = printData;
        _sessionAccessor = sessionAccessor;
        _externalWorkerEditor = externalWorkerEditor;
        _goldTypes = goldTypes;
        _branchId = branchId;
        _craftOrders = craftOrders;
        _cashFunds = cashFunds;
        _bankAccounts = bankAccounts;
        _customerEditor = customerEditor;

        if (orderId is null
            && !string.IsNullOrWhiteSpace(preferredOrderSource)
            && (preferredOrderSource == ProductionOrderSources.Stock
                || preferredOrderSource == ProductionOrderSources.SalesOrder))
        {
            _orderSource = preferredOrderSource;
        }

        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => IsDraft && !IsBusy);
        CancelDraftCommand = new AsyncRelayCommand(CancelDraftAsync, () => CanCancel && !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        AddLineCommand = new AsyncRelayCommand(AddLineAsync, () => IsDraft && !IsBusy);
        EditLineCommand = new ParameterRelayCommand(
            p => _ = EditLineAsync(p as ProductionLineEditorViewModel ?? SelectedLine),
            p => (p as ProductionLineEditorViewModel ?? SelectedLine) is not null && !IsBusy);
        RemoveLineCommand = new ParameterRelayCommand(
            p => RemoveLine(p as ProductionLineEditorViewModel ?? SelectedLine),
            p => IsDraft && (p as ProductionLineEditorViewModel ?? SelectedLine) is not null && Lines.Count > 1);
        SelectCustomerCommand = new ParameterRelayCommand(
            p => SelectCustomer(p as CraftCustomerOption),
            p => IsDraft && IsSalesOrderSource && p is CraftCustomerOption && !IsBusy);
        CreateCustomerCommand = new RelayCommand(
            CreateCustomer,
            () => IsDraft && IsSalesOrderSource && _customerEditor is not null && !IsBusy);
        PostDepositCommand = new AsyncRelayCommand(
            PostDepositAsync,
            () => CanEditDeposit && !IsBusy);
        RefreshEmployeesCommand = new AsyncRelayCommand(LoadEmployeesAsync);
        AddEmployeeCommand = new RelayCommand(AddSelectedEmployee, () => IsDraft && SelectedEmployeeToAdd is not null && !IsBusy);
        RemoveEmployeeCommand = new RelayCommand<ProductionEmployeeOption>(RemoveEmployee, _ => IsDraft && !IsBusy);
        RefreshWorkersCommand = new AsyncRelayCommand(LoadExternalWorkersAsync);
        CreateExternalWorkerCommand = new RelayCommand(CreateExternalWorker, () => IsDraft && !IsBusy);
        RefreshStockMaterialsCommand = new AsyncRelayCommand(LoadStockMaterialsAsync, () => CanIssueMaterials && !IsBusy);
        AddIssueLineCommand = new RelayCommand(AddIssueLine, () => CanIssueMaterials && !IsBusy);
        AddMinimumMaterialsToIssueCommand = new RelayCommand(
            AddMinimumMaterialsToIssue,
            () => CanIssueMaterials && HasMinimumMaterials && !IsBusy);
        RemoveIssueLineCommand = new ParameterRelayCommand(
            p =>
            {
                if (p is ProductionIssueLineEditorViewModel row)
                    IssueLines.Remove(row);
            },
            p => CanIssueMaterials && p is ProductionIssueLineEditorViewModel);
        PostMaterialIssueCommand = new AsyncRelayCommand(PostMaterialIssueAsync, () => CanIssueMaterials && !IsBusy);
        CompleteOrderCommand = new AsyncRelayCommand(CompleteOrderAsync, () => CanComplete && !IsBusy);
        PrintMaterialIssueCommand = new AsyncRelayCommand(() => PrintAsync(1), () => _id is > 0 && !IsBusy);
        PrintFinishedCommand = new AsyncRelayCommand(() => PrintAsync(4), () => _hasFinishedReceipt && !IsBusy);
        PrintSettlementCommand = new AsyncRelayCommand(() => PrintAsync(5), () => _id is > 0 && !IsBusy);
        SelectGalleryImageCommand = new ParameterRelayCommand(
            p =>
            {
                if (p is ProductionGalleryItemViewModel item)
                    SelectedGalleryImage = item;
            },
            p => p is ProductionGalleryItemViewModel);
        OpenGalleryImageCommand = new ParameterRelayCommand(
            p => OpenGalleryImage(p as ProductionGalleryItemViewModel ?? SelectedGalleryImage),
            p => (p as ProductionGalleryItemViewModel ?? SelectedGalleryImage) is not null);
        GoToCurrentWorkflowStepCommand = new RelayCommand(GoToCurrentWorkflowStep);
        SelectWorkflowStepCommand = new ParameterRelayCommand(
            p =>
            {
                if (p is ProductionWorkflowStepViewModel step && step.CanNavigate)
                    SelectedTabIndex = step.TabIndex;
            },
            p => p is ProductionWorkflowStepViewModel { CanNavigate: true });
        RefreshWorkflowSteps();

        _ = InitializeAsync();
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<ProductionLocationItemViewModel> Locations { get; } = [];
    public ObservableCollection<ProductionSalesOrderItemViewModel> SalesOrders { get; } = [];
    public ObservableCollection<ProductionEmployeeOption> Employees { get; } = [];
    public ObservableCollection<ProductionEmployeeOption> SelectedEmployees { get; } = [];

    private ProductionEmployeeOption? _selectedEmployeeToAdd;
    public ProductionEmployeeOption? SelectedEmployeeToAdd
    {
        get => _selectedEmployeeToAdd;
        set
        {
            if (SetProperty(ref _selectedEmployeeToAdd, value))
                ((RelayCommand)AddEmployeeCommand).NotifyCanExecuteChanged();
        }
    }
    public ObservableCollection<ProductionExternalWorkerItemViewModel> ExternalWorkers { get; } = [];
    public ObservableCollection<ProductionLineEditorViewModel> Lines { get; } = [];
    public ObservableCollection<CraftCustomerOption> CustomerSuggestions { get; } = [];
    public ObservableCollection<CashFundOption> CashFunds { get; } = [];
    public ObservableCollection<BankAccountOption> BankAccounts { get; } = [];

    public IReadOnlyList<CraftPaymentMethodOption> DepositPaymentMethodOptions { get; } =
    [
        new(PaymentMethods.Cash, "Tiền mặt"),
        new(PaymentMethods.BankTransfer, "Chuyển khoản"),
        new(PaymentMethods.Card, "Thẻ")
    ];
    public ObservableCollection<ProductionGalleryItemViewModel> GalleryImages { get; } = [];
    public ObservableCollection<ProductionMaterialStockOption> StockMaterials { get; } = [];
    public ObservableCollection<MinimumMaterialHintViewModel> MinimumMaterials { get; } = [];
    public ObservableCollection<ProductionIssueLineEditorViewModel> IssueLines { get; } = [];
    public ObservableCollection<GoldTypeOption> GoldTypeOptions { get; } = [];

    public long? CurrentProductionOrderId => _id;
    public string WindowTitle => _id is > 0 ? $"Lệnh sản xuất {Code}" : "Tạo lệnh sản xuất";

    public string Code
    {
        get => _code;
        private set => SetProperty(ref _code, value);
    }

    public string StatusDisplay => ProductionOrderStatuses.ToDisplay(_statusCode);

    public bool IsDraft => string.Equals(_statusCode, ProductionOrderStatuses.Draft, StringComparison.OrdinalIgnoreCase);
    public bool IsInProgress => string.Equals(_statusCode, ProductionOrderStatuses.InProgress, StringComparison.OrdinalIgnoreCase);
    public bool IsCompleted => string.Equals(_statusCode, ProductionOrderStatuses.Completed, StringComparison.OrdinalIgnoreCase);
    private bool _hasMaterialIssue;

    public bool HasMaterialIssue => _hasMaterialIssue;
    public bool CanIssueMaterials => IsDraft && _id is > 0 && !_hasMaterialIssue;
    public bool CanComplete => IsInProgress && HasMaterialIssue;
    public bool CanCancel => (IsDraft || IsInProgress) && _id is > 0;
    public bool CanUseExecutionTabs => _id is > 0;
    public bool ShowIssueComposer => CanIssueMaterials;
    public bool ShowIssueHistory => HasMaterialIssue;
    public bool ShowCompleteReady => CanComplete || IsCompleted;

    public ObservableCollection<ProductionWorkflowStepViewModel> WorkflowSteps { get; } = [];

    public string WorkflowHint
    {
        get
        {
            if (IsCompleted)
                return _hasFinishedReceipt
                    ? "Lệnh đã hoàn tất và nhập kho. Có thể in phiếu nhập thành phẩm ở phần Hoàn thành lệnh."
                    : "Lệnh đã hoàn tất. Chọn lệnh tại Kho → Nhập thành phẩm để ghi tồn kho.";
            if (_id is not > 0)
                return "Bước 1: điền thông tin, thêm mẫu, rồi Lưu bản nháp.";
            if (IsDraft && Lines.Count == 0)
                return "Bước 1: thêm ít nhất một mẫu sản phẩm, rồi Lưu bản nháp.";
            if (CanIssueMaterials)
                return "Bước 2: chọn NVL còn tồn → Thêm NVL → Xác nhận giao vàng (một lần).";
            if (CanComplete)
                return "Bước 3: kiểm tra tổng kết rồi Hoàn tất lệnh. Thành phẩm sẽ được nhập kho ở bước riêng.";
            return "Theo dõi tiến trình lệnh trên thanh bước phía trên.";
        }
    }

    public string IssueTabEmptyMessage =>
        _id is not > 0
            ? "Cần Lưu bản nháp trước khi giao nguyên liệu."
            : "Đã ghi sổ giao NVL. Dữ liệu bên dưới chỉ đọc.";

    public string CompleteTabBlockedMessage
    {
        get
        {
            if (IsCompleted)
                return string.Empty;
            if (!HasMaterialIssue)
                return "Chưa giao NVL — chưa thể hoàn tất.";
            return string.Empty;
        }
    }


    public IReadOnlyList<ProductionNamedOption> OrderSourceOptions { get; } =
    [
        new(ProductionOrderSources.Stock, "Chế tác nhập kho"),
        new(ProductionOrderSources.SalesOrder, "Chế tác theo đơn")
    ];

    public IReadOnlyList<ProductionNamedOption> ExecutionTypeOptions { get; } =
    [
        new(ProductionExecutionTypes.InHouse, "Nội bộ"),
        new(ProductionExecutionTypes.Outsourced, "Xưởng chế tác")
    ];

    public string OrderSource
    {
        get => _orderSource;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? ProductionOrderSources.Stock
                : value.Trim().ToUpperInvariant();
            if (SetProperty(ref _orderSource, normalized))
            {
                if (!IsSalesOrderSource)
                {
                    SalesOrderId = null;
                    SelectedCustomer = null;
                    _craftOrderCode = string.Empty;
                    _craftOrderStatusCode = string.Empty;
                    _depositPaidAmount = 0;
                }
                OnPropertyChanged(nameof(IsSalesOrderSource));
                OnPropertyChanged(nameof(SelectedOrderSourceOption));
                OnPropertyChanged(nameof(SelectedCustomerHint));
                OnPropertyChanged(nameof(CraftOrderStatusText));
                OnPropertyChanged(nameof(CanEditDeposit));
                NotifyQuoteChanged();
                NotifyCommands();
            }
        }
    }

    public ProductionNamedOption? SelectedOrderSourceOption
    {
        get => OrderSourceOptions.FirstOrDefault(x =>
            string.Equals(x.Code, OrderSource, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
                OrderSource = value.Code;
        }
    }

    public ProductionNamedOption? SelectedExecutionTypeOption
    {
        get => ExecutionTypeOptions.FirstOrDefault(x =>
            string.Equals(x.Code, ExecutionType, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
                ExecutionType = value.Code;
        }
    }

    public bool IsSalesOrderSource =>
        string.Equals(OrderSource, ProductionOrderSources.SalesOrder, StringComparison.OrdinalIgnoreCase);

    public long? SalesOrderId
    {
        get => _salesOrderId;
        set
        {
            if (SetProperty(ref _salesOrderId, value))
            {
                OnPropertyChanged(nameof(SelectedSalesOrder));
                OnPropertyChanged(nameof(SelectedSalesOrderCustomer));
                OnPropertyChanged(nameof(CanEditDeposit));
                OnPropertyChanged(nameof(CraftOrderStatusText));
                NotifyCommands();
            }
        }
    }

    public ObservableCollection<ProductionSalesOrderOption> SalesOrderOptions { get; } = [];

    public ProductionSalesOrderOption? SelectedSalesOrder
    {
        get => SalesOrderOptions.FirstOrDefault(x => x.Id == SalesOrderId);
        set
        {
            SalesOrderId = value?.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedSalesOrderCustomer));
        }
    }

    public string SelectedSalesOrderCustomer => SelectedSalesOrder?.CustomerName ?? string.Empty;

    public string CustomerSearch
    {
        get => _customerSearch;
        set
        {
            if (!SetProperty(ref _customerSearch, value))
                return;
            if (_suppressCustomerSearch)
                return;

            if (SelectedCustomer is not null
                && !string.Equals(value, SelectedCustomer.DisplayText, StringComparison.Ordinal))
            {
                _selectedCustomer = null;
                OnPropertyChanged(nameof(SelectedCustomer));
                OnPropertyChanged(nameof(SelectedCustomerHint));
            }

            _ = DebouncedSearchCustomersAsync(value);
        }
    }

    public CraftCustomerOption? SelectedCustomer
    {
        get => _selectedCustomer;
        set
        {
            if (!SetProperty(ref _selectedCustomer, value))
                return;
            OnPropertyChanged(nameof(SelectedCustomerHint));
            OnPropertyChanged(nameof(CanEditDeposit));
            NotifyCommands();
            if (value is null)
                return;
            _suppressCustomerSearch = true;
            CustomerSearch = value.DisplayText;
            _suppressCustomerSearch = false;
            IsCustomerSuggestionsOpen = false;
        }
    }

    public bool IsCustomerSuggestionsOpen
    {
        get => _isCustomerSuggestionsOpen;
        set => SetProperty(ref _isCustomerSuggestionsOpen, value);
    }

    public string SelectedCustomerHint =>
        !IsSalesOrderSource
            ? string.Empty
            : SelectedCustomer is null
                ? "Chọn khách đặt hàng (không dùng khách lẻ)."
                : $"Khách: {SelectedCustomer.DisplayText}";

    public decimal QuoteLaborTotal => Lines.Sum(x => x.LaborLineTotal);
    public decimal QuoteTotal => Lines.Sum(x => x.QuoteLineTotal);

    public int QuoteQuantityTotal => Lines.Sum(x => Math.Max(0, x.PlannedQuantity));

    /// <summary>Tổng trọng lượng vàng giao dự kiến (chỉ) — dùng cho dải tổng ở footer dialog.</summary>
    public decimal QuoteGoldWeightTotal => Lines.Sum(x => x.GoldWeightTotalChi);

    public string QuoteSummaryText =>
        Lines.Count == 0
            ? "Chưa có mẫu. Thêm mẫu để tính báo giá (vàng + công + đá)."
            : $"{Lines.Count} mẫu · tổng SL {QuoteQuantityTotal} · báo giá {QuoteTotal:N0} đ";

    public string QuoteTotalDisplay => $"Tổng báo giá (vàng + công + đá): {QuoteTotal:N0} đ";

    public string CraftOrderStatusText
    {
        get
        {
            if (!IsSalesOrderSource)
                return string.Empty;
            if (SalesOrderId is > 0 && !string.IsNullOrWhiteSpace(_craftOrderCode))
                return $"Đơn đặt {_craftOrderCode} · {_craftOrderStatusCode} · đã cọc {_depositPaidAmount:N0} đ. Có thể lưu lệnh.";
            return "Chưa đặt cọc — bấm Đặt cọc sau khi chọn khách và thêm mẫu, rồi mới lưu lệnh.";
        }
    }

    public bool CanEditDeposit =>
        IsDraft
        && IsSalesOrderSource
        && SelectedCustomer is not null
        && Lines.Count > 0
        && QuoteTotal > 0
        && SalesOrderId is null or <= 0
        && _craftOrders is not null
        && _cashFunds is not null
        && _bankAccounts is not null;

    public decimal DepositAmount
    {
        get => _depositAmount;
        set
        {
            if (SetProperty(ref _depositAmount, value))
                NotifyCommands();
        }
    }

    public string DepositPaymentMethod
    {
        get => _depositPaymentMethod;
        set
        {
            if (!SetProperty(ref _depositPaymentMethod, value))
                return;
            OnPropertyChanged(nameof(DepositRequiresCashFund));
            OnPropertyChanged(nameof(DepositRequiresBankAccount));
            OnPropertyChanged(nameof(SelectedDepositPaymentMethod));
        }
    }

    public CraftPaymentMethodOption? SelectedDepositPaymentMethod
    {
        get => DepositPaymentMethodOptions.FirstOrDefault(x =>
            string.Equals(x.Code, DepositPaymentMethod, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is null)
                return;
            DepositPaymentMethod = value.Code;
        }
    }

    public bool DepositRequiresCashFund =>
        string.Equals(DepositPaymentMethod, PaymentMethods.Cash, StringComparison.OrdinalIgnoreCase);

    public bool DepositRequiresBankAccount =>
        string.Equals(DepositPaymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase)
        || string.Equals(DepositPaymentMethod, PaymentMethods.Card, StringComparison.OrdinalIgnoreCase);

    public CashFundOption? SelectedDepositCashFund
    {
        get => _selectedDepositCashFund;
        set => SetProperty(ref _selectedDepositCashFund, value);
    }

    public BankAccountOption? SelectedDepositBankAccount
    {
        get => _selectedDepositBankAccount;
        set => SetProperty(ref _selectedDepositBankAccount, value);
    }

    public string ExecutionType
    {
        get => _executionType;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value)
                ? ProductionExecutionTypes.InHouse
                : value.Trim().ToUpperInvariant();
            if (SetProperty(ref _executionType, normalized))
            {
                if (IsInHouse)
                    ExternalWorkerId = null;
                OnPropertyChanged(nameof(IsInHouse));
                OnPropertyChanged(nameof(IsOutsourced));
                OnPropertyChanged(nameof(SelectedExecutionTypeOption));
                NotifyCommands();
            }
        }
    }

    public bool IsInHouse =>
        string.Equals(ExecutionType, ProductionExecutionTypes.InHouse, StringComparison.OrdinalIgnoreCase);

    public bool IsOutsourced =>
        string.Equals(ExecutionType, ProductionExecutionTypes.Outsourced, StringComparison.OrdinalIgnoreCase);

    public long? ExternalWorkerId
    {
        get => _externalWorkerId;
        set => SetProperty(ref _externalWorkerId, value);
    }

    public DateTime OrderDate
    {
        get => _orderDate;
        set => SetProperty(ref _orderDate, value);
    }

    /// <summary>Hạn hoàn thành — nhập được khi còn bản nháp (PRODUCTION_ORDERS.DUE_DATE, migration 62).</summary>
    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    /// <summary>
    /// Ngày giao — derived từ ngày ghi sổ giao NVL (STOCK_DOCUMENTS.DOCUMENT_DATE qua
    /// MATERIAL_ISSUE_DOCUMENT_ID). Trống khi còn bản nháp / chưa giao.
    /// </summary>
    public DateTime? IssueDate
    {
        get => _issueDate;
        private set
        {
            if (SetProperty(ref _issueDate, value))
                OnPropertyChanged(nameof(IssueDateDisplay));
        }
    }

    public string IssueDateDisplay => _issueDate?.ToString("dd/MM/yyyy") ?? "—";

    public long SourceLocationId
    {
        get => _sourceLocationId;
        set
        {
            if (SetProperty(ref _sourceLocationId, value) && CanIssueMaterials)
                _ = LoadStockMaterialsAsync();
        }
    }

    public long FinishedLocationId
    {
        get => _finishedLocationId;
        set => SetProperty(ref _finishedLocationId, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value))
                return;

            // Tab "Giao NVL" = index 2 — tải tồn khi người dùng mở tab.
            if (value == 2 && CanIssueMaterials)
                _ = LoadStockMaterialsAsync();
        }
    }

    public string StockMaterialsHint
    {
        get
        {
            if (HasMaterialIssue)
                return "Đã ghi sổ giao NVL — danh sách bên dưới là lịch sử chỉ đọc.";
            if (!CanIssueMaterials)
                return "Chỉ giao NVL khi lệnh còn bản nháp và chưa ghi sổ giao. Hãy Lưu bản nháp trước.";
            if (SourceLocationId <= 0)
                return "Chọn kho xuất NVL ở phần Thông tin lệnh trước.";
            if (StockMaterials.Count == 0)
                return "Không có tồn NVL (> 0) tại kho xuất đã chọn. Bấm Tải tồn hoặc đổi kho.";
            return $"Có {StockMaterials.Count} NVL còn tồn — chọn rồi bấm Thêm dòng.";
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
                NotifyCommands();
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    public ProductionMaterialStockOption? SelectedStockMaterial
    {
        get => _selectedStockMaterial;
        set
        {
            if (SetProperty(ref _selectedStockMaterial, value))
                OnPropertyChanged(nameof(IssueQuantityHint));
        }
    }

    /// <summary>Gợi ý đơn vị cho ô nhập số lượng — đá quý tính theo viên (số nguyên).</summary>
    public string IssueQuantityHint =>
        _selectedStockMaterial is not null
        && string.Equals(_selectedStockMaterial.MaterialType, ProductionComponentKinds.Stone, StringComparison.OrdinalIgnoreCase)
            ? "Số lượng (viên — nhập số nguyên)"
            : "Số lượng";

    public decimal IssueQuantity
    {
        get => _issueQuantity;
        set => SetProperty(ref _issueQuantity, value);
    }

    public ProductionLineEditorViewModel? SelectedLine
    {
        get => _selectedLine;
        set => SetProperty(ref _selectedLine, value);
    }

    public ProductionGalleryItemViewModel? SelectedGalleryImage
    {
        get => _selectedGalleryImage;
        set
        {
            if (SetProperty(ref _selectedGalleryImage, value))
            {
                foreach (var item in GalleryImages)
                    item.IsSelected = ReferenceEquals(item, value);
                OnPropertyChanged(nameof(HasGalleryImages));
            }
        }
    }

    public bool HasGalleryImages => GalleryImages.Count > 0;

    public string CompletionPreview
    {
        get => _completionPreview;
        private set => SetProperty(ref _completionPreview, value);
    }

    public ICommand SaveDraftCommand { get; }
    public ICommand CancelDraftCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand AddLineCommand { get; }
    public ICommand EditLineCommand { get; }
    public ICommand RemoveLineCommand { get; }
    public ICommand SelectGalleryImageCommand { get; }
    public ICommand OpenGalleryImageCommand { get; }
    public ICommand SelectCustomerCommand { get; }
    public ICommand CreateCustomerCommand { get; }
    public ICommand PostDepositCommand { get; }
    public ICommand RefreshEmployeesCommand { get; }
    public ICommand AddEmployeeCommand { get; }
    public ICommand RemoveEmployeeCommand { get; }
    public ICommand RefreshWorkersCommand { get; }
    public ICommand CreateExternalWorkerCommand { get; }
    public ICommand RefreshStockMaterialsCommand { get; }
    public ICommand AddIssueLineCommand { get; }
    public ICommand AddMinimumMaterialsToIssueCommand { get; }
    public ICommand RemoveIssueLineCommand { get; }
    public ICommand PostMaterialIssueCommand { get; }
    public ICommand CompleteOrderCommand { get; }
    public ICommand PrintMaterialIssueCommand { get; }
    public ICommand PrintFinishedCommand { get; }
    public ICommand PrintSettlementCommand { get; }
    public ICommand GoToCurrentWorkflowStepCommand { get; }
    public ICommand SelectWorkflowStepCommand { get; }

    private AuthenticatedSession RequireSession() =>
        _sessionAccessor.Current ?? throw new InvalidOperationException("Phiên đăng nhập đã hết hạn.");

    private static string InferComponentKind(string materialType) =>
        materialType.Trim().ToUpperInvariant() switch
        {
            "GOLD" => ProductionComponentKinds.Gold,
            "STONE" => ProductionComponentKinds.Stone,
            "ACCESSORY" => ProductionComponentKinds.Accessory,
            _ => ProductionComponentKinds.Other
        };

    private async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            await LoadGoldTypesAsync();
            await LoadLocationsAsync();
            await LoadEligibleSalesOrdersAsync();
            await LoadEmployeesAsync();
            await LoadExternalWorkersAsync();

            if (_orderId is > 0)
                await LoadDetailAsync(_orderId.Value);
            else
                ApplyDefaultLocations();

            NotifyQuoteChanged();
            IsLoaded = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadGoldTypesAsync()
    {
        GoldTypeOptions.Clear();
        try
        {
            foreach (var type in await _goldTypes.ListAsync(activeOnly: true))
                GoldTypeOptions.Add(type);
        }
        catch
        {
            // Catalog chưa sẵn sàng.
        }
    }

    private void ApplyDefaultLocations()
    {
        var raw = Locations.FirstOrDefault(x =>
            string.Equals(x.InventoryScope, InventoryScopes.RawMaterial, StringComparison.OrdinalIgnoreCase));
        var finished = Locations.FirstOrDefault(x =>
            string.Equals(x.InventoryScope, InventoryScopes.FinishedGoods, StringComparison.OrdinalIgnoreCase));
        if (raw is not null)
            SourceLocationId = raw.Id;
        if (finished is not null)
            FinishedLocationId = finished.Id;
    }

    private async Task LoadLocationsAsync()
    {
        var items = await _queries.GetLocationsAsync(_branchId);
        Locations.Clear();
        foreach (var item in items)
            Locations.Add(new ProductionLocationItemViewModel(item));
    }

    private async Task LoadEligibleSalesOrdersAsync()
    {
        var items = await _queries.SearchEligibleSalesOrdersAsync(_branchId, null);
        SalesOrderOptions.Clear();
        foreach (var item in items)
            SalesOrderOptions.Add(item);
        OnPropertyChanged(nameof(SelectedSalesOrder));
    }

    private async Task LoadPaymentAccountsAsync()
    {
        CashFunds.Clear();
        BankAccounts.Clear();
        if (_cashFunds is null || _bankAccounts is null)
            return;

        var session = RequireSession();
        foreach (var fund in await _cashFunds.GetActiveFundsAsync(session))
            CashFunds.Add(fund);
        foreach (var bank in await _bankAccounts.GetActiveAccountsAsync(session))
            BankAccounts.Add(bank);

        SelectedDepositCashFund = CashFunds.FirstOrDefault(x => x.IsDefault) ?? CashFunds.FirstOrDefault();
        SelectedDepositBankAccount = BankAccounts.FirstOrDefault();
    }

    private async Task DebouncedSearchCustomersAsync(string searchText)
    {
        if (_craftOrders is null)
            return;

        _customerSearchCts?.Cancel();
        _customerSearchCts?.Dispose();
        var cts = new CancellationTokenSource();
        _customerSearchCts = cts;

        try
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                ApplyCustomerSuggestions(Array.Empty<CraftCustomerOption>());
                return;
            }

            // Đã chọn khách và ô đang hiện đúng DisplayText → không mở lại gợi ý.
            if (SelectedCustomer is not null
                && string.Equals(searchText, SelectedCustomer.DisplayText, StringComparison.Ordinal))
            {
                ApplyCustomerSuggestions(Array.Empty<CraftCustomerOption>());
                return;
            }

            await Task.Delay(250, cts.Token);
            var session = RequireSession();
            var items = await _craftOrders.SearchCustomersAsync(searchText, session, cts.Token);
            cts.Token.ThrowIfCancellationRequested();
            ApplyCustomerSuggestions(items);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ErrorMessage = ex.Message;
                ApplyCustomerSuggestions(Array.Empty<CraftCustomerOption>());
            });
        }
    }

    private void ApplyCustomerSuggestions(IReadOnlyList<CraftCustomerOption> items)
    {
        void Apply()
        {
            CustomerSuggestions.Clear();
            foreach (var item in items)
                CustomerSuggestions.Add(item);
            IsCustomerSuggestionsOpen = CustomerSuggestions.Count > 0;
        }

        if (Application.Current?.Dispatcher?.CheckAccess() == true)
            Apply();
        else if (Application.Current?.Dispatcher is not null)
            Application.Current.Dispatcher.Invoke(Apply);
        else
            Apply();
    }

    private void SelectCustomer(CraftCustomerOption? customer)
    {
        if (customer is null)
            return;
        if (customer.IsWalkIn)
        {
            ErrorMessage = "Đơn đặt không dùng khách lẻ.";
            return;
        }

        ErrorMessage = string.Empty;
        SelectedCustomer = customer;
    }

    private void CreateCustomer()
    {
        if (_customerEditor is null)
            return;

        try
        {
            ErrorMessage = string.Empty;
            _ = RequireSession();
            var result = _customerEditor.ShowCreate();
            if (result is null)
                return;

            var code = result.Code.Trim();
            var isWalkIn = string.Equals(code, "KH-VL", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "WALK_IN", StringComparison.OrdinalIgnoreCase)
                || string.Equals(code, "KHACH_LE", StringComparison.OrdinalIgnoreCase);
            if (isWalkIn)
            {
                ErrorMessage = "Đơn đặt không dùng khách lẻ.";
                return;
            }

            SelectedCustomer = new CraftCustomerOption(
                result.Id,
                code,
                result.Name,
                string.IsNullOrWhiteSpace(result.Phone) ? null : result.Phone.Trim(),
                IsWalkIn: false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task PostDepositAsync()
    {
        if (_craftOrders is null)
        {
            ErrorMessage = "Chưa cấu hình dịch vụ đơn đặt.";
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            if (SelectedCustomer is null)
                throw new InvalidOperationException("Chọn khách hàng trên tab Thông tin lệnh.");
            if (Lines.Count == 0 || QuoteTotal <= 0)
                throw new InvalidOperationException("Thêm mẫu và nhập báo giá (TL vàng / giá / công / đá) trước khi đặt cọc.");
            if (Lines.Any(x => x.EstimatedWeightChi <= 0))
                throw new InvalidOperationException("Mỗi mẫu cần trọng lượng vàng ước tính > 0 để báo giá đủ.");
            if (DepositAmount <= 0)
                throw new InvalidOperationException("Số tiền cọc phải > 0.");
            if (DepositAmount > QuoteTotal)
                throw new InvalidOperationException("Tiền cọc không được vượt tổng báo giá.");

            var allocation = new PaymentAllocationDraft(
                DepositPaymentMethod,
                DepositAmount,
                DepositRequiresCashFund ? SelectedDepositCashFund?.Id : null,
                DepositRequiresBankAccount ? SelectedDepositBankAccount?.Id : null,
                null);

            var lineDrafts = Lines.Select(line => line.ToCraftLineDraft()).ToList();

            var created = await _craftOrders.CreateWithDepositAsync(
                new CraftOrderDraft(
                    SelectedCustomer.Id,
                    OrderDate,
                    RequireSession().UserId > 0 ? RequireSession().UserId : null,
                    lineDrafts,
                    [allocation],
                    Notes),
                RequireSession());

            SalesOrderId = created.SalesOrderId;
            _craftOrderCode = created.Code;
            _craftOrderStatusCode = created.StatusCode;
            _depositPaidAmount = created.DepositAmountVnd;
            OnPropertyChanged(nameof(CraftOrderStatusText));
            OnPropertyChanged(nameof(CanEditDeposit));
            NotifyCommands();

            MessageBox.Show(
                $"Đã đặt cọc {created.DepositAmountVnd:N0} đ cho đơn {created.Code}. Có thể lưu bản nháp lệnh.",
                "Thành công",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyQuoteChanged()
    {
        OnPropertyChanged(nameof(QuoteLaborTotal));
        OnPropertyChanged(nameof(QuoteTotal));
        OnPropertyChanged(nameof(QuoteQuantityTotal));
        OnPropertyChanged(nameof(QuoteGoldWeightTotal));
        OnPropertyChanged(nameof(QuoteSummaryText));
        OnPropertyChanged(nameof(QuoteTotalDisplay));
        OnPropertyChanged(nameof(CanEditDeposit));
        OnPropertyChanged(nameof(CraftOrderStatusText));
        RefreshMinimumMaterials();
        if (DepositAmount <= 0 && QuoteTotal > 0)
            DepositAmount = Math.Round(QuoteTotal * 0.3m, 0, MidpointRounding.AwayFromZero);
        ((AsyncRelayCommand)PostDepositCommand).NotifyCanExecuteChanged();
    }

    private async Task LoadCraftOrderContextAsync(long? salesOrderId)
    {
        _craftOrderCode = string.Empty;
        _craftOrderStatusCode = string.Empty;
        _depositPaidAmount = 0;

        if (salesOrderId is not > 0 || _craftOrders is null)
        {
            OnPropertyChanged(nameof(CraftOrderStatusText));
            OnPropertyChanged(nameof(CanEditDeposit));
            return;
        }

        var header = await _craftOrders.GetHeaderAsync(salesOrderId.Value, RequireSession());
        if (header is null)
        {
            OnPropertyChanged(nameof(CraftOrderStatusText));
            return;
        }

        _craftOrderCode = header.Code;
        _craftOrderStatusCode = header.StatusCode;
        _depositPaidAmount = header.DepositPaidVnd;
        SelectedCustomer = new CraftCustomerOption(
            header.CustomerId,
            header.CustomerCode,
            header.CustomerName,
            header.CustomerPhone,
            IsWalkIn: false);
        OnPropertyChanged(nameof(CraftOrderStatusText));
        OnPropertyChanged(nameof(CanEditDeposit));
    }

    private async Task LoadEmployeesAsync()
    {
        var items = await _queries.SearchWorkingEmployeesAsync(null);
        Employees.Clear();
        foreach (var item in items)
            Employees.Add(item);
        // Giữ lại các NV đã chọn nếu vẫn còn trong danh sách
        var kept = SelectedEmployees.Where(x => Employees.Any(e => e.Id == x.Id)).ToList();
        SelectedEmployees.Clear();
        foreach (var item in kept)
            SelectedEmployees.Add(item);
    }

    private void AddSelectedEmployee()
    {
        var option = SelectedEmployeeToAdd;
        if (option is null)
            return;
        if (SelectedEmployees.Any(x => x.Id == option.Id))
        {
            ErrorMessage = $"Nhân viên {option.FullName} đã có trong danh sách.";
            return;
        }
        ErrorMessage = string.Empty;
        SelectedEmployees.Add(option);
        SelectedEmployeeToAdd = null;
    }

    private void RemoveEmployee(ProductionEmployeeOption? option)
    {
        if (option is not null)
            SelectedEmployees.Remove(option);
    }

    private async Task LoadExternalWorkersAsync()
    {
        var items = await _queries.SearchActiveExternalWorkersAsync(null);
        ExternalWorkers.Clear();
        foreach (var item in items)
            ExternalWorkers.Add(new ProductionExternalWorkerItemViewModel(item));
    }

    private void CreateExternalWorker()
    {
        try
        {
            var created = _externalWorkerEditor.Show();
            _ = ReloadExternalWorkersAsync(created?.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task ReloadExternalWorkersAsync(long? selectId)
    {
        var previousId = selectId ?? ExternalWorkerId;
        await LoadExternalWorkersAsync();
        if (previousId is > 0 && ExternalWorkers.Any(x => x.Id == previousId))
            ExternalWorkerId = previousId;
    }

    private async Task LoadDetailAsync(long orderId)
    {
        var detail = await _queries.GetOrderDetailAsync(orderId, _branchId)
            ?? throw new InvalidOperationException("Không tìm thấy lệnh sản xuất.");

        _id = detail.Id;
        Code = detail.Code;
        _statusCode = detail.StatusCode;
        OnPropertyChanged(nameof(StatusDisplay));
        OnPropertyChanged(nameof(IsDraft));
        OrderSource = detail.OrderSource;
        SalesOrderId = detail.SalesOrderId;
        if (detail.SalesOrderId is > 0 && SalesOrderOptions.All(x => x.Id != detail.SalesOrderId.Value))
            SalesOrderOptions.Add(new ProductionSalesOrderOption(detail.SalesOrderId.Value,
                detail.SalesOrderCode ?? string.Empty, string.Empty, string.Empty, detail.OrderDate));
        OnPropertyChanged(nameof(SelectedSalesOrder));
        OnPropertyChanged(nameof(SelectedSalesOrderCustomer));
        ExecutionType = detail.ExecutionType;
        ExternalWorkerId = detail.ExternalWorkerId;
        OrderDate = detail.OrderDate;
        DueDate = detail.DueDate;
        IssueDate = detail.MaterialIssueDocumentId is > 0 ? detail.MaterialIssueDocumentDate : null;
        SourceLocationId = detail.SourceLocationId;
        FinishedLocationId = detail.FinishedLocationId;
        Notes = detail.Notes;
        _rowVersion = detail.RowVersion;
        _hasFinishedReceipt = detail.FinishedReceiptDocumentId is > 0;
        OnPropertyChanged(nameof(WindowTitle));

        Lines.Clear();
        foreach (var line in detail.Lines)
            Lines.Add(new ProductionLineEditorViewModel(line));

        await LoadCraftOrderContextAsync(detail.SalesOrderId);

        SelectedEmployees.Clear();
        var workerIds = detail.Workers.Select(w => w.EmployeeId).ToHashSet();
        foreach (var employee in Employees.Where(e => workerIds.Contains(e.Id)))
            SelectedEmployees.Add(employee);

        _removedAttachments.Clear();
        foreach (var line in Lines)
            line.Attachments.Clear();

        if (Lines.Count > 0)
        {
            foreach (var attachment in detail.Attachments)
            {
                var vm = new ProductionAttachmentEditorViewModel(attachment);
                var target = Lines.FirstOrDefault(l => l.LineNumber == attachment.LineNumber)
                    ?? Lines[0];
                vm.LineNumber = target.LineNumber;
                target.Attachments.Add(vm);
            }

            foreach (var line in Lines)
                line.NotifyAttachmentSummaryChanged();
        }

        SelectedLine = Lines.FirstOrDefault();
        RefreshGalleryImages();
        NotifyQuoteChanged();

        _hasMaterialIssue = detail.MaterialIssueDocumentId is > 0;
        IssueLines.Clear();
        foreach (var balance in detail.MaterialBalances)
        {
            if (_hasMaterialIssue)
            {
                IssueLines.Add(ProductionIssueLineEditorViewModel.FromPostedBalance(balance));
            }
        }

        RefreshCompletionPreview();
        if (CanIssueMaterials)
            await LoadStockMaterialsAsync();

        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(HasMaterialIssue));
        OnPropertyChanged(nameof(CanIssueMaterials));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanUseExecutionTabs));
        RefreshWorkflowSteps();
        NotifyCommands();

    }

    private async Task LoadStockMaterialsAsync()
    {
        try
        {
            if (SourceLocationId <= 0)
            {
                StockMaterials.Clear();
                OnPropertyChanged(nameof(StockMaterialsHint));
                RefreshMinimumMaterials();
                return;
            }

            var items = await _queries.SearchMaterialStockAsync(_branchId, SourceLocationId, null);
            StockMaterials.Clear();
            foreach (var item in items)
                StockMaterials.Add(item);

            if (SelectedStockMaterial is not null
                && StockMaterials.All(x => x.MaterialId != SelectedStockMaterial.MaterialId))
            {
                SelectedStockMaterial = null;
            }

            OnPropertyChanged(nameof(StockMaterialsHint));
            RefreshMinimumMaterials();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            OnPropertyChanged(nameof(StockMaterialsHint));
        }
    }

    private void AddIssueLine()
    {
        if (SelectedStockMaterial is null || IssueQuantity <= 0)
        {
            ErrorMessage = "Chọn NVL và nhập số lượng > 0.";
            return;
        }

        // Đá quý tính theo viên — số lượng phải là số nguyên.
        if (string.Equals(SelectedStockMaterial.MaterialType, ProductionComponentKinds.Stone, StringComparison.OrdinalIgnoreCase)
            && IssueQuantity != Math.Truncate(IssueQuantity))
        {
            ErrorMessage = "Đá quý tính theo viên — số lượng phải là số nguyên.";
            return;
        }

        if (IssueLines.Any(x => x.MaterialId == SelectedStockMaterial.MaterialId))
        {
            ErrorMessage = "NVL đã có trong phiếu giao.";
            return;
        }

        IssueLines.Add(new ProductionIssueLineEditorViewModel(SelectedStockMaterial, IssueQuantity));
        ErrorMessage = string.Empty;
        RefreshWorkflowSteps();
    }

    public bool HasMinimumMaterials => MinimumMaterials.Count > 0;

    public string MinimumMaterialsHint =>
        HasMinimumMaterials
            ? "Gợi ý tối thiểu theo TL vàng ước tính × SL mẫu — không bắt buộc khớp khi ghi sổ."
            : "Chưa có TL vàng trên sản phẩm. Sửa sản phẩm để hiện NVL cần cấp tối thiểu.";

    private void RefreshMinimumMaterials()
    {
        MinimumMaterials.Clear();

        var groups = Lines
            .Where(x => x.EstimatedWeightChi > 0 && x.PlannedQuantity > 0
                        && (!string.IsNullOrWhiteSpace(x.GoldTypeCode) || x.GoldMaterialId is > 0))
            .GroupBy(x => x.GoldMaterialId is > 0
                ? $"id:{x.GoldMaterialId.Value}"
                : $"code:{(x.GoldTypeCode ?? string.Empty).Trim().ToUpperInvariant()}")
            .Select(g =>
            {
                var first = g.First();
                var qty = g.Sum(x => x.GoldWeightTotalChi);
                var stock = first.GoldMaterialId is > 0
                    ? StockMaterials.FirstOrDefault(s => s.MaterialId == first.GoldMaterialId.Value)
                    : StockMaterials.FirstOrDefault(s =>
                        string.Equals(s.Code, first.GoldTypeCode, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.PurityCode, first.GoldTypeCode, StringComparison.OrdinalIgnoreCase));

                return new MinimumMaterialHintViewModel(
                    first.GoldMaterialId ?? stock?.MaterialId,
                    first.GoldTypeCode,
                    string.IsNullOrWhiteSpace(first.GoldTypeName) ? first.GoldTypeCode : first.GoldTypeName,
                    qty,
                    stock?.UnitCode ?? "CHI",
                    stock?.QuantityOnHand,
                    stock);
            })
            .Where(x => x.MinimumQuantityChi > 0)
            .OrderBy(x => x.MaterialCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in groups)
            MinimumMaterials.Add(item);

        OnPropertyChanged(nameof(HasMinimumMaterials));
        OnPropertyChanged(nameof(MinimumMaterialsHint));
        ((RelayCommand)AddMinimumMaterialsToIssueCommand).NotifyCanExecuteChanged();
    }

    private void AddMinimumMaterialsToIssue()
    {
        if (!CanIssueMaterials)
            return;

        var added = 0;
        foreach (var hint in MinimumMaterials)
        {
            var stock = hint.MatchedStock
                ?? (hint.MaterialId is > 0
                    ? StockMaterials.FirstOrDefault(s => s.MaterialId == hint.MaterialId.Value)
                    : StockMaterials.FirstOrDefault(s =>
                        string.Equals(s.Code, hint.MaterialCode, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(s.PurityCode, hint.MaterialCode, StringComparison.OrdinalIgnoreCase)));

            if (stock is null)
                continue;

            var existing = IssueLines.FirstOrDefault(x => x.MaterialId == stock.MaterialId);
            if (existing is not null)
            {
                var qty = Math.Max(existing.Quantity, hint.MinimumQuantityChi);
                var notes = existing.Notes;
                IssueLines.Remove(existing);
                var row = new ProductionIssueLineEditorViewModel(stock, qty) { Notes = notes };
                IssueLines.Add(row);
                added++;
                continue;
            }

            IssueLines.Add(new ProductionIssueLineEditorViewModel(stock, hint.MinimumQuantityChi));
            added++;
        }

        ErrorMessage = added == 0
            ? "Không map được NVL kho cho gợi ý tối thiểu. Chọn NVL thủ công hoặc bấm Tải tồn."
            : string.Empty;
        RefreshWorkflowSteps();
    }

    private async Task PostMaterialIssueAsync()
    {
        if (_id is not > 0 || IssueLines.Count == 0)
        {
            ErrorMessage = "Cần lưu lệnh và khai ít nhất một dòng NVL.";
            return;
        }

        if (MessageBox.Show(
                "Ghi sổ giao nguyên liệu? Thao tác không thể sửa trực tiếp sau đó.",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var draft = new ProductionMaterialIssueDraft(
                _id.Value,
                _rowVersion,
                DateTime.Now,
                null,
                null,
                Notes,
                IssueLines.Select(x => new ProductionMaterialIssueLineDraft(x.MaterialId, x.Quantity, x.Notes)).ToList());
            var result = await _application.PostMaterialIssueAsync(draft, RequireSession());
            _rowVersion = result.RowVersion;
            await LoadDetailAsync(_id.Value);
            MessageBox.Show($"Đã ghi sổ giao NVL {result.DocumentCode}.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CompleteOrderAsync()
    {
        if (_id is not > 0)
            return;

        if (MessageBox.Show(
                "Hoàn tất lệnh? Thành phẩm sẽ được nhập kho ở bước riêng. Công nợ tiền công thợ ngoài sẽ được ghi nhận.",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var result = await _application.CompleteAsync(_id.Value, _rowVersion, RequireSession());
            _rowVersion = result.RowVersion;
            await LoadDetailAsync(_id.Value);
            MessageBox.Show(
                $"Đã hoàn tất {result.Code} ({result.FinishedProductCount} món). Chọn lệnh này tại Kho → Nhập thành phẩm để ghi tồn kho.",
                "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PrintAsync(int template)
    {
        if (_id is not > 0)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            switch (template)
            {
                case 1:
                    var issue = await _printData.GetMaterialIssuePrintAsync(_id.Value);
                    if (issue is null) { ErrorMessage = "Chưa có phiếu giao NVL để in."; break; }
                    _printer.PrintMaterialIssue(issue);
                    break;
                case 4:
                    var fin = await _printData.GetFinishedReceiptPrintAsync(_id.Value);
                    if (fin is null) { ErrorMessage = "Chưa có phiếu nhập thành phẩm để in."; break; }
                    _printer.PrintFinishedReceipt(fin);
                    break;
                case 5:
                    var set = await _printData.GetGoldSettlementPrintAsync(_id.Value);
                    if (set is null) { ErrorMessage = "Không có dữ liệu quyết toán để in."; break; }
                    _printer.PrintGoldSettlement(set);
                    break;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshCompletionPreview()
    {
        var labor = Lines.Sum(x => x.LaborLineTotal);
        var serialPreview = _hasFinishedReceipt
            ? "Đã nhập kho thành phẩm."
            : CanComplete || IsCompleted
                ? "Khi nhập kho sẽ tạo serial/thành phẩm theo số lượng mẫu."
                : "Hoàn tất lệnh để chuyển sang nhập kho thành phẩm.";
        CompletionPreview =
            $"{serialPreview} Tổng tiền công dự kiến: {labor:N0} VND. " +
            (IsOutsourced && labor > 0
                ? "Hoàn tất sẽ sinh một công nợ tiền công thợ ngoài."
                : "Lệnh nội bộ không sinh công nợ tiền công.");
    }

    private void RefreshWorkflowSteps()
    {
        var current = ResolveWorkflowStepIndex();
        var definitions = new (string Title, int TabIndex)[]
        {
            ("1. Nháp", 0),
            ("2. Giao NVL", 2),
            ("3. Hoàn tất", 3)
        };

        WorkflowSteps.Clear();
        for (var i = 0; i < definitions.Length; i++)
        {
            var (title, tabIndex) = definitions[i];
            var isDone = i < current || (i == 2 && IsCompleted);
            var isCurrent = IsCompleted ? i == 2 : i == current;
            var canNavigate = tabIndex <= 1 || (_id is > 0 && CanUseExecutionTabs);

            WorkflowSteps.Add(new ProductionWorkflowStepViewModel(
                i, title, tabIndex, isCurrent, isDone, canNavigate));
        }

        OnPropertyChanged(nameof(WorkflowHint));
        OnPropertyChanged(nameof(ShowIssueComposer));
        OnPropertyChanged(nameof(ShowIssueHistory));

        OnPropertyChanged(nameof(ShowCompleteReady));
        OnPropertyChanged(nameof(IssueTabEmptyMessage));

        OnPropertyChanged(nameof(CompleteTabBlockedMessage));
    }

    private int ResolveWorkflowStepIndex()
    {
        if (IsCompleted)
            return 2;
        if (_id is not > 0 || (IsDraft && Lines.Count == 0))
            return 0;
        if (CanIssueMaterials || (IsDraft && !_hasMaterialIssue))
            return 1;
        if (CanComplete)
            return 2;
        return 0;
    }

    private void GoToCurrentWorkflowStep()
    {
        var current = ResolveWorkflowStepIndex();
        SelectedTabIndex = current switch
        {
            0 => Lines.Count == 0 ? 1 : 0,
            1 => 2,
            _ => 3
        };
    }

    private async Task AddLineAsync()
    {
        var next = Lines.Count == 0 ? 1 : Lines.Max(x => x.LineNumber) + 1;
        var draft = new ProductionLineEditorViewModel(next);
        if (!await ShowLineDialogAsync(draft, isNew: true))
            return;

        Lines.Add(draft);
        SelectedLine = draft;
        RefreshGalleryImages();
        NotifyQuoteChanged();
        RefreshWorkflowSteps();
    }

    private async Task EditLineAsync(ProductionLineEditorViewModel? line)
    {
        if (line is null)
            return;

        if (await ShowLineDialogAsync(line, isNew: false))
        {
            RefreshGalleryImages();
            NotifyQuoteChanged();
        }
    }

    private async Task<bool> ShowLineDialogAsync(ProductionLineEditorViewModel line, bool isNew)
    {
        if (SourceLocationId <= 0)
        {
            ErrorMessage = "Vui lòng chọn kho xuất trước khi thêm/sửa mẫu.";
            return false;
        }

        IReadOnlyList<ProductionMaterialStockOption> goldStock;
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            goldStock = await LoadGoldStockOptionsAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            IsBusy = false;
        }

        IReadOnlyDictionary<long, decimal> sellPrices;
        try
        {
            sellPrices = await LoadGoldSellPricesByMaterialIdAsync();
        }
        catch
        {
            sellPrices = new Dictionary<long, decimal>();
        }

        var dialogVm = new ProductionLineEditDialogViewModel(
            _attachments,
            line.LineNumber,
            IsDraft,
            goldStock,
            isNew ? null : line,
            sellPrices);

        var dialog = new ProductionLineEditDialog
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current.MainWindow,
            DataContext = dialogVm
        };

        var accepted = dialog.ShowDialog() == true;
        if (!accepted)
            return false;

        var previousAttachments = line.Attachments.ToList();
        dialogVm.ApplyTo(line);

        foreach (var removed in dialogVm.RemovedAttachments)
        {
            if (removed.Id is > 0 && _removedAttachments.All(x => x.Id != removed.Id))
                _removedAttachments.Add(removed);
        }

        foreach (var old in previousAttachments)
        {
            if (old.Id is > 0)
                continue;
            if (string.IsNullOrWhiteSpace(old.StagingPath))
                continue;
            if (line.Attachments.Any(a => a.StagingPath == old.StagingPath))
                continue;
            _attachments.TryDeleteStagingFile(old.StagingPath);
        }

        return true;
    }

    private async Task<IReadOnlyList<ProductionMaterialStockOption>> LoadGoldStockOptionsAsync()
    {
        if (SourceLocationId <= 0)
            return Array.Empty<ProductionMaterialStockOption>();

        var items = await _queries.SearchMaterialStockAsync(_branchId, SourceLocationId, null);
        return items
            .Where(x => string.Equals(x.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private async Task<IReadOnlyDictionary<long, decimal>> LoadGoldSellPricesByMaterialIdAsync()
    {
        var map = new Dictionary<long, decimal>();
        foreach (var option in await _goldTypes.ListAsync(activeOnly: true))
        {
            var detail = await _goldTypes.GetByIdAsync(option.Id);
            if (detail is null || detail.MaterialId <= 0 || detail.SellPricePerChi <= 0)
                continue;
            map[detail.MaterialId] = detail.SellPricePerChi;
        }

        return map;
    }

    private void RemoveLine(ProductionLineEditorViewModel? line)
    {
        if (line is null || Lines.Count <= 1)
            return;

        foreach (var attachment in line.Attachments.ToList())
        {
            attachment.MarkDeleted = true;
            if (attachment.Id is > 0)
                _removedAttachments.Add(attachment);
            else
                _attachments.TryDeleteStagingFile(attachment.StagingPath);
        }

        Lines.Remove(line);
        var no = 1;
        foreach (var item in Lines.OrderBy(x => x.LineNumber))
        {
            item.LineNumber = no;
            foreach (var attachment in item.Attachments)
                attachment.LineNumber = no;
            no++;
        }

        SelectedLine = Lines.FirstOrDefault();
        RefreshGalleryImages();
        NotifyQuoteChanged();
        RefreshWorkflowSteps();
    }

    private void RefreshGalleryImages()
    {
        var previousPath = SelectedGalleryImage?.LocalPath;
        GalleryImages.Clear();

        foreach (var line in Lines.OrderBy(x => x.LineNumber))
        {
            foreach (var attachment in line.Attachments.Where(a => a.IsPreviewableImage))
            {
                var label = string.IsNullOrWhiteSpace(line.ModelName)
                    ? $"Mẫu #{line.LineNumber}"
                    : $"{line.ModelName} · {attachment.KindDisplayName}";
                GalleryImages.Add(new ProductionGalleryItemViewModel(
                    attachment.LocalPath!,
                    attachment.OriginalFileName,
                    label));
            }
        }

        SelectedGalleryImage = GalleryImages.FirstOrDefault(x =>
                string.Equals(x.LocalPath, previousPath, StringComparison.OrdinalIgnoreCase))
            ?? GalleryImages.FirstOrDefault();
        OnPropertyChanged(nameof(HasGalleryImages));
    }

    private void OpenGalleryImage(ProductionGalleryItemViewModel? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.LocalPath) || !File.Exists(item.LocalPath))
            return;

        SelectedGalleryImage = item;
        var dialog = new ProductionImageViewerDialog
        {
            Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                ?? Application.Current.MainWindow,
            DataContext = new ProductionImageViewerViewModel(item.LocalPath, item.Title)
        };
        dialog.ShowDialog();
    }

    private async Task SaveDraftAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var employeeIds = SelectedEmployees.Select(x => x.Id).ToList();
            var lineDrafts = Lines.Select(x => x.ToDraft()).ToList();
            var attachmentDrafts = Lines
                .SelectMany(line => line.Attachments.Select((att, index) =>
                {
                    att.LineNumber = line.LineNumber;
                    return att.ToDraft(index);
                }))
                .Concat(_removedAttachments.Select(x => x.ToDraft(0)))
                .ToList();

            var draft = new ProductionOrderDraft(
                _id,
                string.IsNullOrWhiteSpace(Code) ? null : Code,
                OrderSource,
                IsSalesOrderSource ? SalesOrderId : null,
                ExecutionType,
                IsOutsourced ? ExternalWorkerId : null,
                OrderDate,
                DueDate,
                SourceLocationId,
                FinishedLocationId,
                Notes,
                _id is > 0 ? _rowVersion : null,
                employeeIds,
                lineDrafts,
                attachmentDrafts);

            var result = await _application.SaveDraftAsync(draft, RequireSession());
            _id = result.Id;
            Code = result.Code;
            _rowVersion = result.RowVersion;
            _statusCode = ProductionOrderStatuses.Draft;
            _removedAttachments.Clear();
            OnPropertyChanged(nameof(WindowTitle));
            await LoadDetailAsync(result.Id);
            ErrorMessage = string.Empty;
            MessageBox.Show($"Đã lưu bản nháp {result.Code}.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CancelDraftAsync()
    {
        if (_id is not > 0)
            return;

        var cancelPrompt = IsDraft
            ? "Hủy lệnh bản nháp này? Thao tác không hoàn tác."
            : "Hủy lệnh đang thực hiện? Chỉ hủy được khi chưa hoàn tất và đủ điều kiện nghiệp vụ. Thao tác không hoàn tác.";
        if (MessageBox.Show(
                cancelPrompt,
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await _application.CancelAsync(_id.Value, _rowVersion, null, RequireSession());
            RequestClose?.Invoke(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(IsDraft));
        OnPropertyChanged(nameof(CanIssueMaterials));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(StockMaterialsHint));
        RefreshWorkflowSteps();
        ((AsyncRelayCommand)SaveDraftCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CancelDraftCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddLineCommand).NotifyCanExecuteChanged();
        ((ParameterRelayCommand)EditLineCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CreateCustomerCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PostDepositCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RefreshStockMaterialsCommand).NotifyCanExecuteChanged();
        ((RelayCommand)AddMinimumMaterialsToIssueCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PostMaterialIssueCommand).NotifyCanExecuteChanged();
        RefreshMinimumMaterials();
        ((AsyncRelayCommand)CompleteOrderCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintMaterialIssueCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintFinishedCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintSettlementCommand).NotifyCanExecuteChanged();
    }
}

public sealed class ProductionWorkflowStepViewModel(
    int index,
    string title,
    int tabIndex,
    bool isCurrent,
    bool isDone,
    bool canNavigate)
{
    public int Index { get; } = index;
    public string Title { get; } = title;
    public int TabIndex { get; } = tabIndex;
    public bool IsCurrent { get; } = isCurrent;
    public bool IsDone { get; } = isDone;
    public bool CanNavigate { get; } = canNavigate;
    public bool IsPending => !IsCurrent && !IsDone;
}

public sealed class ProductionLocationItemViewModel
{
    public ProductionLocationItemViewModel(ProductionLocationOption option)
    {
        Id = option.Id;
        DisplayName = $"{option.Name} ({option.Code})";
        InventoryScope = option.InventoryScope;
    }

    public long Id { get; }
    public string DisplayName { get; }
    public string InventoryScope { get; }
}

public sealed record ProductionNamedOption(string Code, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed class ProductionGalleryItemViewModel : ObservableObject
{
    private bool _isSelected;

    public ProductionGalleryItemViewModel(string localPath, string fileName, string caption)
    {
        LocalPath = localPath;
        FileName = fileName;
        Caption = caption;
        Title = string.IsNullOrWhiteSpace(caption) ? fileName : $"{caption} — {fileName}";
    }

    public string LocalPath { get; }
    public string FileName { get; }
    public string Caption { get; }
    public string Title { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public sealed class ProductionSalesOrderItemViewModel
{
    public ProductionSalesOrderItemViewModel(ProductionSalesOrderOption option)
        : this(option.Id, option.Code, option.CustomerName, option.StatusCode, option.OrderDate)
    {
    }

    public ProductionSalesOrderItemViewModel(long id, string code, string customerName, string statusCode, DateTime orderDate)
    {
        Id = id;
        DisplayName = $"{code} — {customerName} ({orderDate:dd/MM/yyyy})";
        Code = code;
        StatusCode = statusCode;
    }

    public long Id { get; }
    public string DisplayName { get; }
    public string Code { get; }
    public string StatusCode { get; }
}

public sealed class ProductionExternalWorkerItemViewModel
{
    public ProductionExternalWorkerItemViewModel(ProductionExternalWorkerOption option)
    {
        Id = option.Id;
        DisplayName = $"{option.Name} ({option.Code})";
    }

    public long Id { get; }
    public string DisplayName { get; }
}

public sealed class ProductionLineEditorViewModel : ObservableObject
{
    private int _lineNumber;
    private string? _modelCode;
    private string _modelName = string.Empty;
    private string _goldTypeCode = string.Empty;
    private string _goldTypeName = string.Empty;
    private long? _goldMaterialId;
    private int _plannedQuantity = 1;
    private decimal _laborCostPerUnit;
    private decimal _estimatedWeightChi;
    private decimal _goldUnitPricePerChi;
    private decimal _stoneAmountPerUnitVnd;
    private string? _description;
    private string? _notes;

    public ProductionLineEditorViewModel(int lineNumber)
    {
        _lineNumber = lineNumber;
    }

    public ProductionLineEditorViewModel(ProductionOrderLineDto line)
    {
        Id = line.Id;
        _lineNumber = line.LineNumber;
        _modelCode = line.ModelCode;
        _modelName = line.ModelName;
        _goldTypeCode = line.GoldTypeCode;
        _goldTypeName = line.GoldTypeName;
        _plannedQuantity = line.PlannedQuantity;
        _laborCostPerUnit = line.LaborCostPerUnit;
        _estimatedWeightChi = line.EstimatedWeightChi;
        _goldUnitPricePerChi = line.GoldUnitPricePerChi;
        _stoneAmountPerUnitVnd = line.StoneAmountPerUnitVnd;
        _description = line.Description;
        _notes = line.Notes;
    }

    public long? Id { get; }
    public ObservableCollection<ProductionAttachmentEditorViewModel> Attachments { get; } = [];

    public int LineNumber
    {
        get => _lineNumber;
        set => SetProperty(ref _lineNumber, value);
    }

    public string? ModelCode
    {
        get => _modelCode;
        set => SetProperty(ref _modelCode, value);
    }

    public string ModelName
    {
        get => _modelName;
        set => SetProperty(ref _modelName, value ?? string.Empty);
    }

    public string GoldTypeCode
    {
        get => _goldTypeCode;
        set => SetProperty(ref _goldTypeCode, value ?? string.Empty);
    }

    public string GoldTypeName
    {
        get => _goldTypeName;
        set => SetProperty(ref _goldTypeName, value ?? string.Empty);
    }

    public long? GoldMaterialId
    {
        get => _goldMaterialId;
        set => SetProperty(ref _goldMaterialId, value);
    }

    public int PlannedQuantity
    {
        get => _plannedQuantity;
        set
        {
            if (SetProperty(ref _plannedQuantity, value))
                NotifyQuoteTotals();
        }
    }

    public decimal LaborCostPerUnit
    {
        get => _laborCostPerUnit;
        set
        {
            if (SetProperty(ref _laborCostPerUnit, value))
                NotifyQuoteTotals();
        }
    }

    public decimal EstimatedWeightChi
    {
        get => _estimatedWeightChi;
        set
        {
            if (SetProperty(ref _estimatedWeightChi, value))
                NotifyQuoteTotals();
        }
    }

    public decimal GoldUnitPricePerChi
    {
        get => _goldUnitPricePerChi;
        set
        {
            if (SetProperty(ref _goldUnitPricePerChi, value))
                NotifyQuoteTotals();
        }
    }

    public decimal StoneAmountPerUnitVnd
    {
        get => _stoneAmountPerUnitVnd;
        set
        {
            if (SetProperty(ref _stoneAmountPerUnitVnd, value))
                NotifyQuoteTotals();
        }
    }

    public decimal LaborLineTotal => LaborCostPerUnit * Math.Max(0, PlannedQuantity);

    public decimal QuoteLineTotal => CraftOrderValidator.LineTotal(ToCraftLineDraft());

    public decimal GoldWeightTotalChi => EstimatedWeightChi * Math.Max(0, PlannedQuantity);

    public string? Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
    }

    public string? Notes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    public string AttachmentSummary
    {
        get
        {
            var images = Attachments.Count(x => x.IsSampleImage);
            var designs = Attachments.Count(x => !x.IsSampleImage);
            if (images == 0 && designs == 0)
                return "—";
            return $"{images} ảnh, {designs} thiết kế";
        }
    }

    public void NotifyAttachmentSummaryChanged() => OnPropertyChanged(nameof(AttachmentSummary));

    private void NotifyQuoteTotals()
    {
        OnPropertyChanged(nameof(LaborLineTotal));
        OnPropertyChanged(nameof(QuoteLineTotal));
        OnPropertyChanged(nameof(GoldWeightTotalChi));
    }

    public CraftOrderLineDraft ToCraftLineDraft() =>
        new(
            ModelName,
            string.IsNullOrWhiteSpace(GoldTypeCode) ? GoldTypeName : GoldTypeCode,
            PlannedQuantity,
            EstimatedWeightChi,
            GoldUnitPricePerChi,
            LaborCostPerUnit,
            StoneAmountPerUnitVnd,
            Notes);

    public ProductionOrderLineDraft ToDraft() =>
        new(Id, LineNumber, ModelCode, ModelName, GoldTypeCode, GoldTypeName,
            PlannedQuantity, LaborCostPerUnit,
            EstimatedWeightChi, GoldUnitPricePerChi, StoneAmountPerUnitVnd,
            Description, Notes);
}

public sealed class ProductionAttachmentEditorViewModel
{
    public ProductionAttachmentEditorViewModel(string stagingPath, string originalFileName, string attachmentKind)
    {
        StagingPath = stagingPath;
        OriginalFileName = originalFileName;
        AttachmentKind = attachmentKind;
    }

    public ProductionAttachmentEditorViewModel(ProductionAttachmentDto dto)
    {
        Id = dto.Id;
        AttachmentKind = dto.AttachmentKind;
        StoredPath = dto.StoredPath;
        OriginalFileName = dto.OriginalFileName;
        MimeType = dto.MimeType;
        Caption = dto.Caption;
        DisplayOrder = dto.DisplayOrder;
        LineNumber = dto.LineNumber;
    }

    private ProductionAttachmentEditorViewModel(
        long? id,
        string attachmentKind,
        string? stagingPath,
        string? storedPath,
        string originalFileName,
        string? mimeType,
        string? caption,
        int displayOrder,
        int? lineNumber,
        bool markDeleted)
    {
        Id = id;
        AttachmentKind = attachmentKind;
        StagingPath = stagingPath;
        StoredPath = storedPath;
        OriginalFileName = originalFileName;
        MimeType = mimeType;
        Caption = caption;
        DisplayOrder = displayOrder;
        LineNumber = lineNumber;
        MarkDeleted = markDeleted;
    }

    public long? Id { get; }
    public string AttachmentKind { get; set; }
    public string? StagingPath { get; }
    public string? StoredPath { get; }
    public string OriginalFileName { get; }
    public string? MimeType { get; set; }
    public string? Caption { get; set; }
    public int DisplayOrder { get; set; }
    public int? LineNumber { get; set; }
    public bool MarkDeleted { get; set; }

    public bool IsSampleImage =>
        string.Equals(AttachmentKind, ProductionAttachmentKinds.CustomerRequirement, StringComparison.OrdinalIgnoreCase);

    public string KindDisplayName => IsSampleImage ? "Ảnh mẫu" : "File thiết kế";

    public string? LocalPath =>
        !string.IsNullOrWhiteSpace(StagingPath) ? StagingPath : StoredPath;

    public bool IsPreviewableImage
    {
        get
        {
            var path = LocalPath;
            if (string.IsNullOrWhiteSpace(path))
                return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif";
        }
    }

    public ProductionAttachmentEditorViewModel CloneForEdit() =>
        new(Id, AttachmentKind, StagingPath, StoredPath, OriginalFileName, MimeType, Caption, DisplayOrder, LineNumber, MarkDeleted);

    public ProductionAttachmentDraft ToDraft(int displayOrder) =>
        new(Id, AttachmentKind, StagingPath, StoredPath, OriginalFileName, MimeType, Caption, displayOrder, LineNumber, MarkDeleted);
}

public sealed class MinimumMaterialHintViewModel
{
    public MinimumMaterialHintViewModel(
        long? materialId,
        string materialCode,
        string materialName,
        decimal minimumQuantityChi,
        string unitCode,
        decimal? quantityOnHand,
        ProductionMaterialStockOption? matchedStock)
    {
        MaterialId = materialId;
        MaterialCode = materialCode;
        MaterialName = materialName;
        MinimumQuantityChi = minimumQuantityChi;
        UnitCode = unitCode;
        QuantityOnHand = quantityOnHand;
        MatchedStock = matchedStock;
    }

    public long? MaterialId { get; }
    public string MaterialCode { get; }
    public string MaterialName { get; }
    public decimal MinimumQuantityChi { get; }
    public string UnitCode { get; }
    public decimal? QuantityOnHand { get; }
    public ProductionMaterialStockOption? MatchedStock { get; }

    public string OnHandDisplay =>
        QuantityOnHand is null
            ? "Chưa map tồn"
            : $"{QuantityOnHand.Value:0.###} {UnitCode}";

    public string MinimumDisplay => $"{MinimumQuantityChi:0.###} {UnitCode}";

    public string StockStatusHint =>
        QuantityOnHand is null
            ? "Cần chọn NVL thủ công"
            : QuantityOnHand.Value + 0.0001m < MinimumQuantityChi
                ? "Tồn thấp hơn tối thiểu"
                : "Đủ tồn gợi ý";
}

public sealed class ProductionIssueLineEditorViewModel
{
    public ProductionIssueLineEditorViewModel(ProductionMaterialStockOption stock, decimal quantity)
    {
        MaterialId = stock.MaterialId;
        MaterialCode = stock.Code;
        MaterialName = stock.Name;
        MaterialType = stock.MaterialType;
        PurityDisplay = string.IsNullOrWhiteSpace(stock.PurityCode) ? "--" : stock.PurityCode;
        UnitCode = stock.UnitCode;
        SnapshotCostHint = stock.AverageCost;
        Quantity = quantity;
        IsPostedHistory = false;
    }

    private ProductionIssueLineEditorViewModel(ProductionMaterialBalanceDto balance)
    {
        MaterialId = balance.MaterialId;
        MaterialCode = balance.MaterialCode;
        MaterialName = balance.MaterialName;
        MaterialType = balance.MaterialType;
        // DTO lịch sử không có PurityCode — hiển thị "--".
        PurityDisplay = "--";
        UnitCode = balance.UnitCode;
        SnapshotCostHint = balance.UnitCostSnapshot;
        Quantity = balance.QuantityIssued;
        IsPostedHistory = true;
    }

    public static ProductionIssueLineEditorViewModel FromPostedBalance(ProductionMaterialBalanceDto balance) =>
        new(balance);

    public long MaterialId { get; }
    public string MaterialCode { get; }
    public string MaterialName { get; }
    public string MaterialType { get; }
    public string PurityDisplay { get; }
    public string UnitCode { get; }
    public decimal SnapshotCostHint { get; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public bool IsPostedHistory { get; }
    public string DisplayCostHint => IsPostedHistory
        ? $"{SnapshotCostHint:N0} / {UnitCode} (snapshot khi giao)"
        : $"{SnapshotCostHint:N0} / {UnitCode} (ước tính tồn)";
}

