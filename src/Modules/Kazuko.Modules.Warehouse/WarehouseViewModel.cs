using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Suppliers;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class WarehouseViewModel : ObservableObject
{
    private readonly Kazuko.Core.UserManagement.IAuthorizationService? _permissions;
    private bool CanView(string function) => _permissions?.Current.Allows(function) ?? true;
    private const int OverviewPreviewLimit = 8;
    private const int RecentHistoryLimit = 6;

    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IWarehouseRepository _repository;
    private readonly IWarehouseGoldPriceRepository _warehouseGoldPrices;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly IMaterialReceiptService _materialReceipts;
    private readonly IFinishedProductTransferService _finishedProductTransfers;
    private readonly ISupplierRepository _suppliers;
    private readonly ISupplierEditorService _supplierEditor;
    private readonly IGoldTypeEditorService _goldTypeEditor;
    private readonly ICashFundRepository _funds;
    private readonly IBankAccountRepository _banks;
    private readonly IGoldTypeCatalogService _goldTypes;
    private IReadOnlyList<GoldTypeOption> _goldTypeCatalog = Array.Empty<GoldTypeOption>();
    private readonly IProductionQueryRepository _productionQueries;
    private readonly IProductionApplicationService _productionApplication;
    private readonly IPreferencesService _preferences;
    private CancellationTokenSource? _loadCts;
    private bool _suppressDocumentDetailLoad;
    private bool _suppressTabSideEffects;

    private string _balanceSearchText = string.Empty;
    private string _selectedMaterialType = "Tất cả";
    private WarehouseLocationOption? _selectedLocation;
    private bool _lowStockOnly;
    private MaterialBalanceRowViewModel? _selectedBalance;

    private string _overviewRawSearchText = string.Empty;
    private string _overviewRawGoldType = "Tất cả loại vàng";
    private string _overviewRawStatus = "Tất cả trạng thái";
    private string _overviewFinishedSearchText = string.Empty;
    private FinishedProductGroupOption? _overviewFinishedGroup;
    private string _overviewFinishedGoldType = "Tất cả loại vàng";
    private string _overviewFinishedStatus = "Tất cả trạng thái";
    private string _overviewSaleSearchText = string.Empty;
    private FinishedProductGroupOption? _overviewSaleGroup;
    private string _overviewSaleGoldType = "Tất cả loại vàng";
    private string _overviewSaleStatus = "Tất cả trạng thái";

    private string _finishedSearchText = string.Empty;
    private FinishedProductGroupOption? _selectedFinishedGroup;
    private string _selectedFinishedGoldType = "Tất cả loại vàng";
    private string _selectedFinishedStatus = "Tất cả trạng thái";
    private FinishedProductRowViewModel? _selectedFinishedProduct;

    private string _saleSearchText = string.Empty;
    private FinishedProductGroupOption? _selectedSaleGroup;
    private string _selectedSaleGoldType = "Tất cả loại vàng";
    private string _selectedSaleStatus = "Tất cả trạng thái";
    private SaleItemRowViewModel? _selectedSaleItem;

    private string _documentSearchText = string.Empty;
    private string _selectedDocumentType = "Tất cả";
    private string _selectedDocumentStatus = "Tất cả";
    private StockDocumentRowViewModel? _selectedDocument;

    private string _movementSearchText = string.Empty;
    private WarehouseLocationOption? _selectedMovementLocation;
    private StockMovementRowViewModel? _selectedMovement;

    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isBalancesEmpty = true;
    private bool _isFinishedEmpty = true;
    private bool _isSalesEmpty = true;
    private bool _isDocumentsEmpty = true;
    private bool _isMovementsEmpty = true;
    private bool _isRecentHistoryEmpty = true;
    private bool _hasDocumentDetail;
    private int _selectedTabIndex;

    private decimal _rawMaterialStockValue;
    private decimal _finishedProductStockValue;
    private decimal _salesGoodsStockValue;
    private int _balanceCount;
    private int _lowStockCount;
    private int _documentCount;
    private int _todayDocumentCount;

    private string _detailCode = string.Empty;
    private string _detailDate = string.Empty;
    private string _detailType = string.Empty;
    private string _detailStatus = string.Empty;
    private string _detailReference = string.Empty;
    private string _detailCounterparty = string.Empty;
    private string _detailSource = string.Empty;
    private string _detailNotes = string.Empty;
    private string _detailCreatedBy = string.Empty;
    private string _detailPostedBy = string.Empty;
    private string _detailPostedAt = string.Empty;

    private string _donutCenterText = "0";
    private string _donutCenterSubText = "Triệu VNĐ";

    public WarehouseViewModel(
        IWarehouseRepository repository,
        IWarehouseGoldPriceRepository warehouseGoldPrices,
        ICurrentSessionAccessor sessionAccessor,
        IMaterialReceiptService materialReceipts,
        IFinishedProductTransferService finishedProductTransfers,
        ISupplierRepository suppliers,
        ISupplierEditorService supplierEditor,
        IGoldTypeEditorService goldTypeEditor,
        ICashFundRepository funds,
        IBankAccountRepository banks,
        IGoldTypeCatalogService goldTypes,
        IProductionQueryRepository productionQueries,
        IProductionApplicationService productionApplication,
        IPreferencesService preferences,
        Kazuko.Core.UserManagement.IAuthorizationService? authorization = null)
    {
        _repository = repository;
        _warehouseGoldPrices = warehouseGoldPrices;
        _sessionAccessor = sessionAccessor;
        _materialReceipts = materialReceipts;
        _finishedProductTransfers = finishedProductTransfers;
        _suppliers = suppliers;
        _supplierEditor = supplierEditor;
        _goldTypeEditor = goldTypeEditor;
        _funds = funds;
        _banks = banks;
        _goldTypes = goldTypes;
        _productionQueries = productionQueries;
        _productionApplication = productionApplication;
        _preferences = preferences;
        _permissions = authorization;

        Locations.Add(new WarehouseLocationOption(0, 0, string.Empty, "Tất cả vị trí", "WAREHOUSE"));
        MovementLocations.Add(new WarehouseLocationOption(0, 0, string.Empty, "Tất cả vị trí", "WAREHOUSE"));
        SelectedLocation = Locations[0];
        SelectedMovementLocation = MovementLocations[0];

        MaterialTypeOptions.Add("Tất cả");
        MaterialTypeOptions.Add("GOLD");
        MaterialTypeOptions.Add("STONE");
        MaterialTypeOptions.Add("ACCESSORY");

        GoldTypeFilterOptions.Add("Tất cả loại vàng");
        _ = LoadGoldTypeFilterOptionsAsync();

        ProductGroupFilterOptions.Add(new FinishedProductGroupOption(0, string.Empty, "Tất cả nhóm"));
        OverviewFinishedGroup = ProductGroupFilterOptions[0];
        OverviewSaleGroup = ProductGroupFilterOptions[0];
        SelectedFinishedGroup = ProductGroupFilterOptions[0];
        SelectedSaleGroup = ProductGroupFilterOptions[0];
        _ = LoadProductGroupFilterOptionsAsync();

        StatusFilterOptions.Add("Tất cả trạng thái");
        StatusFilterOptions.Add("OK");
        StatusFilterOptions.Add("Thấp");

        FinishedStatusOptions.Add("Tất cả trạng thái");
        FinishedStatusOptions.Add("IN_STOCK");
        FinishedStatusOptions.Add("RESERVED");

        SaleStatusOptions.Add("Tất cả trạng thái");
        SaleStatusOptions.Add("DISPLAYED");

        DocumentTypeOptions.Add("Tất cả");
        DocumentTypeOptions.Add("OPENING");
        DocumentTypeOptions.Add("RECEIPT");
        DocumentTypeOptions.Add("ISSUE");
        // GX = giao NVL sản xuất (PRODUCTION_MATERIAL_ISSUE); XK = xuất kho thường (ISSUE).
        DocumentTypeOptions.Add("PRODUCTION_MATERIAL_ISSUE");
        DocumentTypeOptions.Add("PRODUCTION_FINISHED_RECEIPT");
        DocumentTypeOptions.Add("TRANSFER");
        DocumentTypeOptions.Add("OUTSOURCED_CRAFT_ISSUE");
        DocumentTypeOptions.Add("CRAFT_RETURN_RECEIPT");
        DocumentTypeOptions.Add("FINISHED_GOODS_ISSUE");
        DocumentTypeOptions.Add("STOCK_ADJUSTMENT");

        DocumentStatusOptions.Add("Tất cả");
        DocumentStatusOptions.Add("DRAFT");
        DocumentStatusOptions.Add("POSTED");
        DocumentStatusOptions.Add("CANCELLED");

        SearchBalancesCommand = new AsyncRelayCommand(SearchBalancesAsync, () => !IsLoading);
        SearchFinishedProductsCommand = new AsyncRelayCommand(SearchFinishedProductsAsync, () => !IsLoading);
        SearchSaleItemsCommand = new AsyncRelayCommand(SearchSaleItemsAsync, () => !IsLoading);
        SearchDocumentsCommand = new AsyncRelayCommand(SearchDocumentsAsync, () => !IsLoading);
        SearchMovementsCommand = new AsyncRelayCommand(SearchMovementsAsync, () => !IsLoading);
        RefreshCommand = new AsyncRelayCommand(RefreshAllAsync, () => !IsLoading);
        ClearBalanceFiltersCommand = new RelayCommand(ClearBalanceFilters, () => !IsLoading);
        ClearFinishedFiltersCommand = new RelayCommand(ClearFinishedFilters, () => !IsLoading);
        ClearSaleFiltersCommand = new RelayCommand(ClearSaleFilters, () => !IsLoading);
        ClearDocumentFiltersCommand = new RelayCommand(ClearDocumentFilters, () => !IsLoading);
        ClearMovementFiltersCommand = new RelayCommand(ClearMovementFilters, () => !IsLoading);

        NavigateToTabCommand = new RelayCommand<int>(NavigateToTab);
        OpenReceiptTabCommand = new RelayCommand(OpenMaterialReceiptDialog);
        OpenIssueTabCommand = new RelayCommand(() => OpenDocumentTab("ISSUE", WarehouseTabs.Issue));
        OpenTransferTabCommand = new RelayCommand(() => OpenDocumentTab("TRANSFER", WarehouseTabs.Transfer));
        OpenFinishedProductTransferCommand = new RelayCommand(OpenFinishedProductTransferDialog);
        OpenSalesIntakeApprovalCommand = new RelayCommand(OpenSalesIntakeApprovalDialog);
        OpenProductionFinishedReceiptCommand = new RelayCommand(OpenProductionFinishedReceiptDialog);
        OpenWarehouseGoldPricesCommand = new RelayCommand(OpenWarehouseGoldPriceDialog);
        ViewAllRawMaterialsCommand = new RelayCommand(() => NavigateToTab(WarehouseTabs.RawMaterials));
        ViewAllFinishedProductsCommand = new RelayCommand(() => NavigateToTab(WarehouseTabs.FinishedProducts));
        ViewAllSalesGoodsCommand = new RelayCommand(() => NavigateToTab(WarehouseTabs.SalesGoods));
        ViewAllRecentHistoryCommand = new RelayCommand(() => OpenDocumentTab("TRANSFER", WarehouseTabs.Transfer));
        ConfigureGoldTypeChartCommand = new AsyncRelayCommand(OpenGoldTypeChartConfigAsync);
        ApplyOverviewRawFiltersCommand = new RelayCommand(ApplyOverviewRawFilters);
        ApplyOverviewFinishedFiltersCommand = new RelayCommand(ApplyOverviewFinishedFilters);
        ApplyOverviewSaleFiltersCommand = new RelayCommand(ApplyOverviewSaleFilters);

        _ = InitializeAsync();
    }

    public ObservableCollection<MaterialBalanceRowViewModel> Balances { get; } = new();
    public ObservableCollection<MaterialBalanceRowViewModel> OverviewRawBalances { get; } = new();
    public ObservableCollection<FinishedProductRowViewModel> FinishedProducts { get; } = new();
    public ObservableCollection<FinishedProductRowViewModel> OverviewFinishedProducts { get; } = new();
    public ObservableCollection<SaleItemRowViewModel> SaleItems { get; } = new();
    public ObservableCollection<SaleItemRowViewModel> OverviewSaleItems { get; } = new();
    public ObservableCollection<StockDocumentRowViewModel> Documents { get; } = new();
    public ObservableCollection<StockDocumentLineRowViewModel> DocumentLines { get; } = new();
    public ObservableCollection<StockMovementRowViewModel> Movements { get; } = new();
    public ObservableCollection<StockMovementRowViewModel> RecentMovements { get; } = new();
    public ObservableCollection<StockDocumentRowViewModel> RecentDocuments { get; } = new();
    public ObservableCollection<ChartSliceViewModel> CompositionSlices { get; } = new();
    public ObservableCollection<ChartSliceViewModel> GoldTypeBars { get; } = new();
    public ObservableCollection<WarehouseLocationOption> Locations { get; } = new();
    public ObservableCollection<WarehouseLocationOption> MovementLocations { get; } = new();
    public ObservableCollection<string> MaterialTypeOptions { get; } = new();
    public ObservableCollection<string> GoldTypeFilterOptions { get; } = new();
    public ObservableCollection<FinishedProductGroupOption> ProductGroupFilterOptions { get; } = new();
    public ObservableCollection<string> StatusFilterOptions { get; } = new();
    public ObservableCollection<string> FinishedStatusOptions { get; } = new();
    public ObservableCollection<string> SaleStatusOptions { get; } = new();
    public ObservableCollection<string> DocumentTypeOptions { get; } = new();
    public ObservableCollection<string> DocumentStatusOptions { get; } = new();

    public string BalanceSearchText
    {
        get => _balanceSearchText;
        set => SetProperty(ref _balanceSearchText, value);
    }

    public string SelectedMaterialType
    {
        get => _selectedMaterialType;
        set => SetProperty(ref _selectedMaterialType, value);
    }

    public WarehouseLocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set => SetProperty(ref _selectedLocation, value);
    }

    public bool LowStockOnly
    {
        get => _lowStockOnly;
        set => SetProperty(ref _lowStockOnly, value);
    }

    public MaterialBalanceRowViewModel? SelectedBalance
    {
        get => _selectedBalance;
        set
        {
            if (SetProperty(ref _selectedBalance, value))
                OnPropertyChanged(nameof(HasSelectedBalance));
        }
    }

    public bool HasSelectedBalance => SelectedBalance is not null;

    public string OverviewRawSearchText
    {
        get => _overviewRawSearchText;
        set
        {
            if (SetProperty(ref _overviewRawSearchText, value))
                ApplyOverviewRawFilters();
        }
    }

    public string OverviewRawGoldType
    {
        get => _overviewRawGoldType;
        set
        {
            if (SetProperty(ref _overviewRawGoldType, value))
                ApplyOverviewRawFilters();
        }
    }

    public string OverviewRawStatus
    {
        get => _overviewRawStatus;
        set
        {
            if (SetProperty(ref _overviewRawStatus, value))
                ApplyOverviewRawFilters();
        }
    }

    public string OverviewFinishedSearchText
    {
        get => _overviewFinishedSearchText;
        set
        {
            if (SetProperty(ref _overviewFinishedSearchText, value))
                ApplyOverviewFinishedFilters();
        }
    }

    public string OverviewFinishedGoldType
    {
        get => _overviewFinishedGoldType;
        set
        {
            if (SetProperty(ref _overviewFinishedGoldType, value))
                ApplyOverviewFinishedFilters();
        }
    }

    public FinishedProductGroupOption? OverviewFinishedGroup
    {
        get => _overviewFinishedGroup;
        set
        {
            if (SetProperty(ref _overviewFinishedGroup, value))
                ApplyOverviewFinishedFilters();
        }
    }

    public string OverviewFinishedStatus
    {
        get => _overviewFinishedStatus;
        set
        {
            if (SetProperty(ref _overviewFinishedStatus, value))
                ApplyOverviewFinishedFilters();
        }
    }

    public string OverviewSaleSearchText
    {
        get => _overviewSaleSearchText;
        set
        {
            if (SetProperty(ref _overviewSaleSearchText, value))
                ApplyOverviewSaleFilters();
        }
    }

    public string OverviewSaleGoldType
    {
        get => _overviewSaleGoldType;
        set
        {
            if (SetProperty(ref _overviewSaleGoldType, value))
                ApplyOverviewSaleFilters();
        }
    }

    public FinishedProductGroupOption? OverviewSaleGroup
    {
        get => _overviewSaleGroup;
        set
        {
            if (SetProperty(ref _overviewSaleGroup, value))
                ApplyOverviewSaleFilters();
        }
    }

    public string OverviewSaleStatus
    {
        get => _overviewSaleStatus;
        set
        {
            if (SetProperty(ref _overviewSaleStatus, value))
                ApplyOverviewSaleFilters();
        }
    }

    public string FinishedSearchText
    {
        get => _finishedSearchText;
        set => SetProperty(ref _finishedSearchText, value);
    }

    public string SelectedFinishedGoldType
    {
        get => _selectedFinishedGoldType;
        set => SetProperty(ref _selectedFinishedGoldType, value);
    }

    public FinishedProductGroupOption? SelectedFinishedGroup
    {
        get => _selectedFinishedGroup;
        set => SetProperty(ref _selectedFinishedGroup, value);
    }

    public string SelectedFinishedStatus
    {
        get => _selectedFinishedStatus;
        set => SetProperty(ref _selectedFinishedStatus, value);
    }

    public FinishedProductRowViewModel? SelectedFinishedProduct
    {
        get => _selectedFinishedProduct;
        set
        {
            if (SetProperty(ref _selectedFinishedProduct, value))
                OnPropertyChanged(nameof(HasSelectedFinishedProduct));
        }
    }

    public bool HasSelectedFinishedProduct => SelectedFinishedProduct is not null;

    public string SaleSearchText
    {
        get => _saleSearchText;
        set => SetProperty(ref _saleSearchText, value);
    }

    public string SelectedSaleGoldType
    {
        get => _selectedSaleGoldType;
        set => SetProperty(ref _selectedSaleGoldType, value);
    }

    public FinishedProductGroupOption? SelectedSaleGroup
    {
        get => _selectedSaleGroup;
        set => SetProperty(ref _selectedSaleGroup, value);
    }

    public string SelectedSaleStatus
    {
        get => _selectedSaleStatus;
        set => SetProperty(ref _selectedSaleStatus, value);
    }

    public SaleItemRowViewModel? SelectedSaleItem
    {
        get => _selectedSaleItem;
        set
        {
            if (SetProperty(ref _selectedSaleItem, value))
                OnPropertyChanged(nameof(HasSelectedSaleItem));
        }
    }

    public bool HasSelectedSaleItem => SelectedSaleItem is not null;

    public string DocumentSearchText
    {
        get => _documentSearchText;
        set => SetProperty(ref _documentSearchText, value);
    }

    public string SelectedDocumentType
    {
        get => _selectedDocumentType;
        set => SetProperty(ref _selectedDocumentType, value);
    }

    public string SelectedDocumentStatus
    {
        get => _selectedDocumentStatus;
        set => SetProperty(ref _selectedDocumentStatus, value);
    }

    public StockDocumentRowViewModel? SelectedDocument
    {
        get => _selectedDocument;
        set
        {
            if (!SetProperty(ref _selectedDocument, value) || _suppressDocumentDetailLoad)
                return;
            _ = LoadDocumentDetailAsync(value);
        }
    }

    public string MovementSearchText
    {
        get => _movementSearchText;
        set => SetProperty(ref _movementSearchText, value);
    }

    public WarehouseLocationOption? SelectedMovementLocation
    {
        get => _selectedMovementLocation;
        set => SetProperty(ref _selectedMovementLocation, value);
    }

    public StockMovementRowViewModel? SelectedMovement
    {
        get => _selectedMovement;
        set => SetProperty(ref _selectedMovement, value);
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
        private set
        {
            if (SetProperty(ref _isLoading, value))
                OnPropertyChanged(nameof(IsBusyOverlayVisible));
        }
    }

    public bool IsBusyOverlayVisible => IsLoading;

    public bool IsBalancesEmpty
    {
        get => _isBalancesEmpty;
        private set => SetProperty(ref _isBalancesEmpty, value);
    }

    public bool IsFinishedEmpty
    {
        get => _isFinishedEmpty;
        private set => SetProperty(ref _isFinishedEmpty, value);
    }

    public bool IsSalesEmpty
    {
        get => _isSalesEmpty;
        private set => SetProperty(ref _isSalesEmpty, value);
    }

    public bool IsOverviewRawEmpty => OverviewRawBalances.Count == 0;
    public bool IsOverviewFinishedEmpty => OverviewFinishedProducts.Count == 0;
    public bool IsOverviewSaleEmpty => OverviewSaleItems.Count == 0;

    public bool IsDocumentsEmpty
    {
        get => _isDocumentsEmpty;
        private set => SetProperty(ref _isDocumentsEmpty, value);
    }

    public bool IsMovementsEmpty
    {
        get => _isMovementsEmpty;
        private set => SetProperty(ref _isMovementsEmpty, value);
    }

    public bool IsRecentHistoryEmpty
    {
        get => _isRecentHistoryEmpty;
        private set => SetProperty(ref _isRecentHistoryEmpty, value);
    }

    public bool HasDocumentDetail
    {
        get => _hasDocumentDetail;
        private set => SetProperty(ref _hasDocumentDetail, value);
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (!SetProperty(ref _selectedTabIndex, value) || _suppressTabSideEffects)
                return;
            OnTabChanged(value);
        }
    }

    public decimal RawMaterialStockValue
    {
        get => _rawMaterialStockValue;
        private set
        {
            if (SetProperty(ref _rawMaterialStockValue, value))
            {
                OnPropertyChanged(nameof(RawMaterialStockValueText));
                OnPropertyChanged(nameof(TotalStockValue));
                OnPropertyChanged(nameof(TotalStockValueText));
            }
        }
    }

    public decimal FinishedProductStockValue
    {
        get => _finishedProductStockValue;
        private set
        {
            if (SetProperty(ref _finishedProductStockValue, value))
            {
                OnPropertyChanged(nameof(FinishedProductStockValueText));
                OnPropertyChanged(nameof(TotalStockValue));
                OnPropertyChanged(nameof(TotalStockValueText));
            }
        }
    }

    public decimal SalesGoodsStockValue
    {
        get => _salesGoodsStockValue;
        private set
        {
            if (SetProperty(ref _salesGoodsStockValue, value))
            {
                OnPropertyChanged(nameof(SalesGoodsStockValueText));
                OnPropertyChanged(nameof(TotalStockValue));
                OnPropertyChanged(nameof(TotalStockValueText));
            }
        }
    }

    public decimal TotalStockValue => RawMaterialStockValue + FinishedProductStockValue + SalesGoodsStockValue;

    public string RawMaterialStockValueText => FormatMoney(RawMaterialStockValue);
    public string FinishedProductStockValueText => FormatMoney(FinishedProductStockValue);
    public string SalesGoodsStockValueText => FormatMoney(SalesGoodsStockValue);
    public string TotalStockValueText => FormatMoney(TotalStockValue);

    public string DonutCenterText
    {
        get => _donutCenterText;
        private set => SetProperty(ref _donutCenterText, value);
    }

    public string DonutCenterSubText
    {
        get => _donutCenterSubText;
        private set => SetProperty(ref _donutCenterSubText, value);
    }

    public int BalanceCount
    {
        get => _balanceCount;
        private set => SetProperty(ref _balanceCount, value);
    }

    public int LowStockCount
    {
        get => _lowStockCount;
        private set => SetProperty(ref _lowStockCount, value);
    }

    public decimal MaterialStockValue => RawMaterialStockValue;
    public string MaterialStockValueText => RawMaterialStockValueText;

    public int DocumentCount
    {
        get => _documentCount;
        private set => SetProperty(ref _documentCount, value);
    }

    public int TodayDocumentCount
    {
        get => _todayDocumentCount;
        private set => SetProperty(ref _todayDocumentCount, value);
    }

    public string DetailCode
    {
        get => _detailCode;
        private set => SetProperty(ref _detailCode, value);
    }

    public string DetailDate
    {
        get => _detailDate;
        private set => SetProperty(ref _detailDate, value);
    }

    public string DetailType
    {
        get => _detailType;
        private set => SetProperty(ref _detailType, value);
    }

    public string DetailStatus
    {
        get => _detailStatus;
        private set => SetProperty(ref _detailStatus, value);
    }

    public string DetailReference
    {
        get => _detailReference;
        private set => SetProperty(ref _detailReference, value);
    }

    public string DetailCounterparty
    {
        get => _detailCounterparty;
        private set => SetProperty(ref _detailCounterparty, value);
    }

    public string DetailSource
    {
        get => _detailSource;
        private set => SetProperty(ref _detailSource, value);
    }

    public string DetailNotes
    {
        get => _detailNotes;
        private set => SetProperty(ref _detailNotes, value);
    }

    public string DetailCreatedBy
    {
        get => _detailCreatedBy;
        private set => SetProperty(ref _detailCreatedBy, value);
    }

    public string DetailPostedBy
    {
        get => _detailPostedBy;
        private set => SetProperty(ref _detailPostedBy, value);
    }

    public string DetailPostedAt
    {
        get => _detailPostedAt;
        private set => SetProperty(ref _detailPostedAt, value);
    }

    public ICommand SearchBalancesCommand { get; }
    public ICommand SearchFinishedProductsCommand { get; }
    public ICommand SearchSaleItemsCommand { get; }
    public ICommand SearchDocumentsCommand { get; }
    public ICommand SearchMovementsCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ClearBalanceFiltersCommand { get; }
    public ICommand ClearFinishedFiltersCommand { get; }
    public ICommand ClearSaleFiltersCommand { get; }
    public ICommand ClearDocumentFiltersCommand { get; }
    public ICommand ClearMovementFiltersCommand { get; }
    public ICommand NavigateToTabCommand { get; }
    public ICommand OpenReceiptTabCommand { get; }
    public ICommand OpenIssueTabCommand { get; }
    public ICommand OpenTransferTabCommand { get; }
    public ICommand OpenFinishedProductTransferCommand { get; }
    public ICommand OpenSalesIntakeApprovalCommand { get; }
    public ICommand OpenProductionFinishedReceiptCommand { get; }
    public ICommand OpenWarehouseGoldPricesCommand { get; }
    public ICommand ViewAllRawMaterialsCommand { get; }
    public ICommand ViewAllFinishedProductsCommand { get; }
    public ICommand ViewAllSalesGoodsCommand { get; }
    public ICommand ViewAllRecentHistoryCommand { get; }
    public ICommand ConfigureGoldTypeChartCommand { get; }
    public ICommand ApplyOverviewRawFiltersCommand { get; }
    public ICommand ApplyOverviewFinishedFiltersCommand { get; }
    public ICommand ApplyOverviewSaleFiltersCommand { get; }

    private async Task InitializeAsync()
    {
        await LoadLocationsAsync();
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải dữ liệu kho...");
        try
        {
            // Dashboard KPIs/charts require full branch stock; tab filters stay client-side until explicit Search.
            var balanceFilter = new MaterialBalanceSearchFilter(string.Empty, string.Empty, 0, false);
            var finishedFilter = new FinishedProductSearchFilter(string.Empty, string.Empty, string.Empty, string.Empty);
            var saleFilter = new SaleItemSearchFilter(string.Empty, string.Empty, string.Empty, string.Empty);
            var documentFilter = BuildDocumentFilterForCurrentTab();
            var movementFilter = new StockMovementSearchFilter(0, 0, string.Empty);

            var balancesTask = CanView("INV_STOCK_VIEW") ? _repository.SearchMaterialBalancesAsync(balanceFilter, session, scope.Token) : Task.FromResult<IReadOnlyList<MaterialBalanceItem>>([]);
            var finishedTask = CanView("INV_STOCK_VIEW") ? _repository.SearchFinishedProductStockAsync(finishedFilter, session, scope.Token) : Task.FromResult<IReadOnlyList<FinishedProductStockItem>>([]);
            var saleTask = CanView("INV_STOCK_VIEW") ? _repository.SearchSaleItemStockAsync(saleFilter, session, scope.Token) : Task.FromResult<IReadOnlyList<SaleItemStockItem>>([]);
            var documentsTask = CanView("INV_HISTORY") ? _repository.SearchDocumentsAsync(documentFilter, session, scope.Token) : Task.FromResult<IReadOnlyList<StockDocumentSummary>>([]);
            var movementsTask = CanView("INV_HISTORY") ? _repository.SearchMovementsAsync(movementFilter, session, scope.Token) : Task.FromResult<IReadOnlyList<StockMovementItem>>([]);

            await Task.WhenAll(balancesTask, finishedTask, saleTask, documentsTask, movementsTask);
            scope.Token.ThrowIfCancellationRequested();

            ApplyBalances(balancesTask.Result);
            ApplyFinishedProducts(finishedTask.Result);
            ApplySaleItems(saleTask.Result);
            ApplyDocuments(documentsTask.Result);
            ApplyMovements(movementsTask.Result);
            RebuildDashboardMetrics();

            StatusMessage = $"Đã tải: {Balances.Count} NVL, {FinishedProducts.Count} thành phẩm, {SaleItems.Count} hàng bán.";
            await LoadDocumentDetailAsync(SelectedDocument);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Balances.Clear();
            FinishedProducts.Clear();
            SaleItems.Clear();
            Documents.Clear();
            Movements.Clear();
            OverviewRawBalances.Clear();
            OverviewFinishedProducts.Clear();
            OverviewSaleItems.Clear();
            RecentMovements.Clear();
            RecentDocuments.Clear();
            CompositionSlices.Clear();
            GoldTypeBars.Clear();
            IsBalancesEmpty = true;
            IsFinishedEmpty = true;
            IsSalesEmpty = true;
            IsDocumentsEmpty = true;
            IsMovementsEmpty = true;
            IsRecentHistoryEmpty = true;
            RawMaterialStockValue = 0;
            FinishedProductStockValue = 0;
            SalesGoodsStockValue = 0;
            ErrorMessage = "Không thể tải dữ liệu kho. Vui lòng thử lại.";
            StatusMessage = string.Empty;
            NotifyOverviewEmptyFlags();
        }
    }

    private async Task LoadLocationsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        try
        {
            var locations = await _repository.GetLocationsAsync(session);
            var selectedId = SelectedLocation?.Id ?? 0;
            var selectedMovementId = SelectedMovementLocation?.Id ?? 0;

            while (Locations.Count > 1)
                Locations.RemoveAt(Locations.Count - 1);
            while (MovementLocations.Count > 1)
                MovementLocations.RemoveAt(MovementLocations.Count - 1);

            foreach (var location in locations)
            {
                Locations.Add(location);
                MovementLocations.Add(location);
            }

            SelectedLocation = Locations.FirstOrDefault(x => x.Id == selectedId) ?? Locations[0];
            SelectedMovementLocation =
                MovementLocations.FirstOrDefault(x => x.Id == selectedMovementId) ?? MovementLocations[0];
        }
        catch (Exception)
        {
            ErrorMessage = "Không thể tải danh sách vị trí kho.";
        }
    }

    private async Task SearchBalancesAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải tồn nguyên vật liệu...");
        try
        {
            var filter = new MaterialBalanceSearchFilter(
                BalanceSearchText,
                SelectedMaterialType == "Tất cả" ? string.Empty : SelectedMaterialType,
                SelectedLocation?.Id ?? 0,
                LowStockOnly);

            var rows = await _repository.SearchMaterialBalancesAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            ApplyBalances(rows);
            RebuildDashboardMetrics();
            StatusMessage = IsBalancesEmpty
                ? "Không có tồn nguyên vật liệu phù hợp."
                : $"Hiển thị {Balances.Count} dòng tồn NVL.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Balances.Clear();
            OverviewRawBalances.Clear();
            SelectedBalance = null;
            IsBalancesEmpty = true;
            BalanceCount = 0;
            LowStockCount = 0;
            RawMaterialStockValue = 0;
            ErrorMessage = "Không thể tải tồn nguyên vật liệu. Vui lòng thử lại.";
            StatusMessage = string.Empty;
            NotifyOverviewEmptyFlags();
        }
    }

    private async Task SearchFinishedProductsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải tồn thành phẩm...");
        try
        {
            var filter = new FinishedProductSearchFilter(
                FinishedSearchText,
                SelectedFinishedGroup?.Code ?? string.Empty,
                NormalizeGoldType(SelectedFinishedGoldType),
                NormalizeStatusCode(SelectedFinishedStatus));
            var rows = await _repository.SearchFinishedProductStockAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            ApplyFinishedProducts(rows);
            RebuildDashboardMetrics();
            StatusMessage = IsFinishedEmpty
                ? "Không có thành phẩm tồn kho."
                : $"Hiển thị {FinishedProducts.Count} thành phẩm.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            FinishedProducts.Clear();
            OverviewFinishedProducts.Clear();
            SelectedFinishedProduct = null;
            IsFinishedEmpty = true;
            FinishedProductStockValue = 0;
            ErrorMessage = "Không thể tải tồn thành phẩm. Vui lòng thử lại.";
            StatusMessage = string.Empty;
            NotifyOverviewEmptyFlags();
        }
    }

    private async Task SearchSaleItemsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải kho bán hàng...");
        try
        {
            var filter = new SaleItemSearchFilter(
                SaleSearchText,
                SelectedSaleGroup?.Code ?? string.Empty,
                NormalizeGoldType(SelectedSaleGoldType),
                NormalizeStatusCode(SelectedSaleStatus));
            var rows = await _repository.SearchSaleItemStockAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            ApplySaleItems(rows);
            RebuildDashboardMetrics();
            StatusMessage = IsSalesEmpty
                ? "Không có hàng bán trưng bày."
                : $"Hiển thị {SaleItems.Count} hàng bán.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            SaleItems.Clear();
            OverviewSaleItems.Clear();
            SelectedSaleItem = null;
            IsSalesEmpty = true;
            SalesGoodsStockValue = 0;
            ErrorMessage = "Không thể tải kho bán hàng. Vui lòng thử lại.";
            StatusMessage = string.Empty;
            NotifyOverviewEmptyFlags();
        }
    }

    private async Task SearchDocumentsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải phiếu kho...");
        try
        {
            var filter = BuildDocumentFilterForCurrentTab();
            var rows = await _repository.SearchDocumentsAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            ApplyDocuments(rows);
            StatusMessage = IsDocumentsEmpty
                ? "Không có phiếu kho phù hợp."
                : $"Hiển thị {Documents.Count} phiếu kho.";
            await LoadDocumentDetailAsync(SelectedDocument);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Documents.Clear();
            RecentDocuments.Clear();
            _suppressDocumentDetailLoad = true;
            try
            {
                SelectedDocument = null;
            }
            finally
            {
                _suppressDocumentDetailLoad = false;
            }
            ClearDocumentDetail();
            IsDocumentsEmpty = true;
            IsRecentHistoryEmpty = RecentMovements.Count == 0;
            DocumentCount = 0;
            TodayDocumentCount = 0;
            ErrorMessage = "Không thể tải danh sách phiếu kho. Vui lòng thử lại.";
            StatusMessage = string.Empty;
        }
    }

    private async Task LoadDocumentDetailAsync(StockDocumentRowViewModel? row)
    {
        if (row is null)
        {
            ClearDocumentDetail();
            return;
        }

        var session = RequireSession();
        if (session is null)
            return;

        var previousStatus = StatusMessage;
        using var scope = BeginLoad("Đang tải chi tiết phiếu...");
        try
        {
            var detail = await _repository.GetDocumentAsync(row.Id, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            if (detail is null)
            {
                ClearDocumentDetail();
                ErrorMessage = "Không đọc được phiếu kho (sai chi nhánh hoặc đã bị xóa).";
                return;
            }

            var lines = await _repository.GetDocumentLinesAsync(row.Id, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();

            DetailCode = detail.Code;
            DetailDate = detail.DocumentDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            DetailType = row.DocumentTypeDisplay;
            DetailStatus = row.StatusDisplay;
            DetailReference = detail.ReferenceNo;
            DetailCounterparty = detail.CounterpartyName;
            DetailSource = FormatSourceType(detail.SourceType);
            DetailNotes = detail.Notes;
            DetailCreatedBy = detail.CreatedByName;
            DetailPostedBy = string.IsNullOrWhiteSpace(detail.PostedByName) ? "—" : detail.PostedByName;
            DetailPostedAt = detail.PostedAt?.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) ?? "—";
            HasDocumentDetail = true;

            DocumentLines.Clear();
            foreach (var line in lines)
                DocumentLines.Add(new StockDocumentLineRowViewModel(line));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ClearDocumentDetail();
            ErrorMessage = "Không thể tải chi tiết phiếu kho.";
        }
        finally
        {
            if (string.Equals(StatusMessage, "Đang tải chi tiết phiếu...", StringComparison.Ordinal))
                StatusMessage = previousStatus ?? string.Empty;
        }
    }

    private async Task SearchMovementsAsync()
    {
        var session = RequireSession();
        if (session is null)
            return;

        using var scope = BeginLoad("Đang tải sổ giao dịch...");
        try
        {
            var filter = new StockMovementSearchFilter(
                0,
                SelectedMovementLocation?.Id ?? 0,
                MovementSearchText);

            var rows = await _repository.SearchMovementsAsync(filter, session, scope.Token);
            scope.Token.ThrowIfCancellationRequested();
            ApplyMovements(rows);
            StatusMessage = IsMovementsEmpty
                ? "Không có giao dịch kho phù hợp."
                : $"Hiển thị {Movements.Count} giao dịch kho.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            Movements.Clear();
            RecentMovements.Clear();
            SelectedMovement = null;
            IsMovementsEmpty = true;
            IsRecentHistoryEmpty = RecentDocuments.Count == 0;
            ErrorMessage = "Không thể tải sổ giao dịch kho. Vui lòng thử lại.";
            StatusMessage = string.Empty;
        }
    }

    private void ApplyBalances(IReadOnlyList<MaterialBalanceItem> rows)
    {
        (long MaterialId, long LocationId)? previousKey = SelectedBalance is null
            ? null
            : (SelectedBalance.MaterialId, SelectedBalance.LocationId);
        Balances.Clear();
        var index = 1;
        foreach (var row in rows)
            Balances.Add(new MaterialBalanceRowViewModel(row, index++));

        SelectedBalance = previousKey is { } key
            ? Balances.FirstOrDefault(x => x.MaterialId == key.MaterialId && x.LocationId == key.LocationId)
              ?? Balances.FirstOrDefault()
            : Balances.FirstOrDefault();

        IsBalancesEmpty = Balances.Count == 0;
        BalanceCount = Balances.Count;
        LowStockCount = Balances.Count(x => x.IsLowStock);
        RawMaterialStockValue = Balances.Sum(x => x.StockValue);
        ApplyOverviewRawFilters();
    }

    private void ApplyFinishedProducts(IReadOnlyList<FinishedProductStockItem> rows)
    {
        var previousId = SelectedFinishedProduct?.Id;
        FinishedProducts.Clear();
        var index = 1;
        foreach (var row in rows)
            FinishedProducts.Add(new FinishedProductRowViewModel(row, index++));

        SelectedFinishedProduct = previousId is long id
            ? FinishedProducts.FirstOrDefault(x => x.Id == id) ?? FinishedProducts.FirstOrDefault()
            : FinishedProducts.FirstOrDefault();

        IsFinishedEmpty = FinishedProducts.Count == 0;
        FinishedProductStockValue = FinishedProducts.Sum(x => x.StockValue);
        ApplyOverviewFinishedFilters();
    }

    private void ApplySaleItems(IReadOnlyList<SaleItemStockItem> rows)
    {
        var previousId = SelectedSaleItem?.Id;
        SaleItems.Clear();
        var index = 1;
        foreach (var row in rows)
            SaleItems.Add(new SaleItemRowViewModel(row, index++));

        SelectedSaleItem = previousId is long id
            ? SaleItems.FirstOrDefault(x => x.Id == id) ?? SaleItems.FirstOrDefault()
            : SaleItems.FirstOrDefault();

        IsSalesEmpty = SaleItems.Count == 0;
        SalesGoodsStockValue = SaleItems.Sum(x => x.StockValue);
        ApplyOverviewSaleFilters();
    }

    private void ApplyDocuments(IReadOnlyList<StockDocumentSummary> rows)
    {
        var previousId = SelectedDocument?.Id;
        Documents.Clear();
        var index = 1;
        foreach (var row in rows)
            Documents.Add(new StockDocumentRowViewModel(row, index++));

        _suppressDocumentDetailLoad = true;
        try
        {
            SelectedDocument = previousId is long id
                ? Documents.FirstOrDefault(x => x.Id == id) ?? Documents.FirstOrDefault()
                : Documents.FirstOrDefault();
        }
        finally
        {
            _suppressDocumentDetailLoad = false;
        }

        IsDocumentsEmpty = Documents.Count == 0;
        DocumentCount = Documents.Count;
        var today = DateTime.Today;
        TodayDocumentCount = Documents.Count(x => x.DocumentDate.Date == today);

        RecentDocuments.Clear();
        foreach (var doc in Documents.Take(RecentHistoryLimit))
            RecentDocuments.Add(doc);
        IsRecentHistoryEmpty = RecentDocuments.Count == 0 && RecentMovements.Count == 0;
    }

    private void ApplyMovements(IReadOnlyList<StockMovementItem> rows)
    {
        var previousId = SelectedMovement?.Id;
        Movements.Clear();
        var index = 1;
        foreach (var row in rows)
            Movements.Add(new StockMovementRowViewModel(row, index++));

        SelectedMovement = previousId is long id
            ? Movements.FirstOrDefault(x => x.Id == id) ?? Movements.FirstOrDefault()
            : Movements.FirstOrDefault();

        IsMovementsEmpty = Movements.Count == 0;

        RecentMovements.Clear();
        foreach (var movement in Movements.Take(RecentHistoryLimit))
            RecentMovements.Add(movement);
        IsRecentHistoryEmpty = RecentDocuments.Count == 0 && RecentMovements.Count == 0;
    }

    private void RebuildDashboardMetrics()
    {
        CompositionSlices.Clear();
        CompositionSlices.Add(new ChartSliceViewModel("NVL", RawMaterialStockValue, "ChartBrush.Raw", "Nguyên liệu"));
        CompositionSlices.Add(new ChartSliceViewModel("Thành phẩm", FinishedProductStockValue, "ChartBrush.Finished"));
        CompositionSlices.Add(new ChartSliceViewModel("Trưng bày", SalesGoodsStockValue, "ChartBrush.Sale", "Trưng bày"));

        var millions = TotalStockValue / 1_000_000m;
        DonutCenterText = millions.ToString("N0", ViCulture);
        DonutCenterSubText = "Triệu VNĐ";

        // Phân loại theo mã danh mục GOLD_TYPES + Bạc/Đá quý/Khác.
        var goldGroups = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var knownCodes = new HashSet<string>(
            _goldTypeCatalog.Select(t => t.Code), StringComparer.OrdinalIgnoreCase);
        foreach (var row in Balances)
        {
            var key = ClassifyGoldTypeKey(row.PurityCode, row.MaterialType, knownCodes);
            goldGroups[key] = goldGroups.GetValueOrDefault(key) + row.StockValue;
        }

        foreach (var row in FinishedProducts)
        {
            var key = ClassifyGoldTypeKey(row.GoldPurity, string.Empty, knownCodes);
            goldGroups[key] = goldGroups.GetValueOrDefault(key) + row.StockValue;
        }

        foreach (var row in SaleItems)
        {
            var key = ClassifyGoldTypeKey(row.GoldPurity, string.Empty, knownCodes);
            goldGroups[key] = goldGroups.GetValueOrDefault(key) + row.StockValue;
        }

        GoldTypeBars.Clear();
        // Thứ tự cột: danh mục loại vàng (theo DisplayOrder) rồi Bạc, Đá quý.
        var orderedTypes = new List<(string Key, string Label)>();
        foreach (var t in _goldTypeCatalog)
            orderedTypes.Add((t.Code, t.DisplayName));
        orderedTypes.Add(("Bạc", "Bạc"));
        orderedTypes.Add(("Đá quý", "Đá quý"));

        var brushCycle = new[] { "ChartBrush.Raw", "ChartBrush.Finished", "ChartBrush.Sale", "ChartBrush.Purple" };
        var visibleKeys = GetVisibleGoldTypeKeys();
        decimal otherTotal = 0;
        var brushIndex = 0;
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, label) in orderedTypes)
        {
            seenKeys.Add(key);
            goldGroups.TryGetValue(key, out var value);
            if (visibleKeys.Contains(key))
            {
                GoldTypeBars.Add(new ChartSliceViewModel(
                    label, value, brushCycle[brushIndex % brushCycle.Length]));
                brushIndex++;
            }
            else
            {
                otherTotal += value;
            }
        }
        // Các key trong dữ liệu nhưng không thuộc danh mục cũng gộp vào "Khác".
        foreach (var kvp in goldGroups)
        {
            if (!seenKeys.Contains(kvp.Key))
                otherTotal += kvp.Value;
        }
        // Cột "Khác" = tổng các loại không được chọn hiển thị (luôn hiện khi > 0).
        if (otherTotal > 0)
        {
            GoldTypeBars.Add(new ChartSliceViewModel("Khác", otherTotal, "ChartBrush.Muted"));
        }
    }

    /// <summary>
    /// Phân loại một dòng tồn kho thành key loại vàng: mã danh mục GOLD_TYPES,
    /// "Bạc" (bạc), "Đá quý" (đá), hoặc "Khác".
    /// </summary>
    private static string ClassifyGoldTypeKey(
        string purityOrType, string materialType, HashSet<string> knownCodes)
    {
        var text = $"{purityOrType} {materialType}".ToUpperInvariant();
        if (text.Contains("SILVER", StringComparison.Ordinal)
            || text.Contains("BẠC", StringComparison.Ordinal)
            || text.Contains("BAC", StringComparison.Ordinal)
            || text.Contains("925", StringComparison.Ordinal)
            || text.Contains("950", StringComparison.Ordinal))
            return "Bạc";
        if (text.Contains("STONE", StringComparison.Ordinal)
            || text.Contains("ĐÁ", StringComparison.Ordinal)
            || materialType.Equals("STONE", StringComparison.OrdinalIgnoreCase))
            return "Đá quý";

        var code = (purityOrType ?? string.Empty).Trim().ToUpperInvariant();
        // Alias phổ biến: 24K → 9999, 18K → 750.
        if (code is "24K" or "24") code = "9999";
        else if (code is "18K" or "18") code = "750";
        if (code.Length > 0 && knownCodes.Contains(code))
            return code;
        return "Khác";
    }

    /// <summary>Tất cả key loại có thể hiển thị: mã danh mục + Bạc + Đá quý.</summary>
    private List<string> GetAllChartTypeKeys()
    {
        var keys = _goldTypeCatalog.Select(t => t.Code).ToList();
        keys.Add("Bạc");
        keys.Add("Đá quý");
        return keys;
    }

    /// <summary>Lấy danh sách key loại vàng được cấu hình hiển thị trên biểu đồ.</summary>
    private HashSet<string> GetVisibleGoldTypeKeys()
    {
        var allKeys = GetAllChartTypeKeys();
        var configured = _preferences.Load().ChartVisibleGoldTypes;
        if (string.IsNullOrWhiteSpace(configured))
            return new HashSet<string>(allKeys, StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(
            configured.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        // Migrate định dạng cũ (24K/18K) → hiện tất cả.
        if (keys.Contains("24K") || keys.Contains("18K"))
            return new HashSet<string>(allKeys, StringComparer.OrdinalIgnoreCase);
        return keys;
    }

    /// <summary>Mở dialog cấu hình loại vàng hiển thị trên biểu đồ cột.</summary>
    private async Task OpenGoldTypeChartConfigAsync()
    {
        var catalog = _goldTypeCatalog;
        if (catalog.Count == 0)
        {
            try
            {
                catalog = await _goldTypes.ListAsync(activeOnly: true);
                _goldTypeCatalog = catalog;
            }
            catch
            {
                catalog = Array.Empty<GoldTypeOption>();
            }
        }

        var items = new List<GoldTypeChartTypeItem>();
        foreach (var t in catalog)
            items.Add(new GoldTypeChartTypeItem(t.Code, t.DisplayName));
        items.Add(new GoldTypeChartTypeItem("Bạc", "Bạc"));
        items.Add(new GoldTypeChartTypeItem("Đá quý", "Đá quý"));

        var current = GetVisibleGoldTypeKeys();
        var dialog = new GoldTypeChartConfigDialog(items, current)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialog.SelectedKeys is null)
            return;

        var prefs = _preferences.Load();
        var updated = prefs with { ChartVisibleGoldTypes = string.Join(",", dialog.SelectedKeys) };
        _preferences.Save(updated);
        RebuildDashboardMetrics();
    }

    private void ApplyOverviewRawFilters()
    {
        OverviewRawBalances.Clear();
        var search = OverviewRawSearchText.Trim();
        var gold = NormalizeGoldType(OverviewRawGoldType);
        var status = OverviewRawStatus;
        var index = 1;
        foreach (var row in Balances)
        {
            if (search.Length > 0
                && row.MaterialCode.Contains(search, StringComparison.OrdinalIgnoreCase) == false
                && row.MaterialName.Contains(search, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            if (gold.Length > 0
                && !row.GoldTypeDisplay.Contains(gold, StringComparison.OrdinalIgnoreCase)
                && !row.PurityCode.Contains(gold, StringComparison.OrdinalIgnoreCase))
                continue;
            if (status == "Thấp" && !row.IsLowStock)
                continue;
            if (status == "OK" && row.IsLowStock)
                continue;

            OverviewRawBalances.Add(new MaterialBalanceRowViewModel(row.Item, index++));
            if (OverviewRawBalances.Count >= OverviewPreviewLimit)
                break;
        }

        NotifyOverviewEmptyFlags();
    }

    private void ApplyOverviewFinishedFilters()
    {
        OverviewFinishedProducts.Clear();
        var search = OverviewFinishedSearchText.Trim();
        var gold = NormalizeGoldType(OverviewFinishedGoldType);
        var group = OverviewFinishedGroup?.Code ?? string.Empty;
        var status = NormalizeStatusCode(OverviewFinishedStatus);
        var index = 1;
        foreach (var row in FinishedProducts)
        {
            if (search.Length > 0
                && row.Code.Contains(search, StringComparison.OrdinalIgnoreCase) == false
                && row.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            if (gold.Length > 0 && !row.GoldPurity.Contains(gold, StringComparison.OrdinalIgnoreCase))
                continue;
            if (group.Length > 0 && !row.ProductGroupCode.Equals(group, StringComparison.OrdinalIgnoreCase))
                continue;
            if (status.Length > 0 && !row.StatusCode.Equals(status, StringComparison.OrdinalIgnoreCase))
                continue;

            OverviewFinishedProducts.Add(new FinishedProductRowViewModel(row.Item, index++));
            if (OverviewFinishedProducts.Count >= OverviewPreviewLimit)
                break;
        }

        NotifyOverviewEmptyFlags();
    }

    private void ApplyOverviewSaleFilters()
    {
        OverviewSaleItems.Clear();
        var search = OverviewSaleSearchText.Trim();
        var gold = NormalizeGoldType(OverviewSaleGoldType);
        var group = OverviewSaleGroup?.Code ?? string.Empty;
        var status = NormalizeStatusCode(OverviewSaleStatus);
        var index = 1;
        foreach (var row in SaleItems)
        {
            if (search.Length > 0
                && row.Code.Contains(search, StringComparison.OrdinalIgnoreCase) == false
                && row.ProductName.Contains(search, StringComparison.OrdinalIgnoreCase) == false)
                continue;
            if (gold.Length > 0 && !row.GoldPurity.Contains(gold, StringComparison.OrdinalIgnoreCase))
                continue;
            if (group.Length > 0 && !row.ProductGroupCode.Equals(group, StringComparison.OrdinalIgnoreCase))
                continue;
            if (status.Length > 0 && !row.StatusCode.Equals(status, StringComparison.OrdinalIgnoreCase))
                continue;

            OverviewSaleItems.Add(new SaleItemRowViewModel(row.Item, index++));
            if (OverviewSaleItems.Count >= OverviewPreviewLimit)
                break;
        }

        NotifyOverviewEmptyFlags();
    }

    private void NotifyOverviewEmptyFlags()
    {
        OnPropertyChanged(nameof(IsOverviewRawEmpty));
        OnPropertyChanged(nameof(IsOverviewFinishedEmpty));
        OnPropertyChanged(nameof(IsOverviewSaleEmpty));
    }

    private void NavigateToTab(int? tabIndex)
    {
        if (tabIndex is null)
            return;
        NavigateToTab(tabIndex.Value);
    }

    private void NavigateToTab(int tabIndex)
    {
        if (tabIndex is WarehouseTabs.Receipt)
        {
            OpenDocumentTab("RECEIPT", WarehouseTabs.Receipt);
            return;
        }

        if (tabIndex is WarehouseTabs.Issue)
        {
            OpenDocumentTab("ISSUE", WarehouseTabs.Issue);
            return;
        }

        if (tabIndex is WarehouseTabs.Transfer)
        {
            OpenDocumentTab("TRANSFER", WarehouseTabs.Transfer);
            return;
        }

        _suppressTabSideEffects = true;
        try
        {
            SelectedTabIndex = tabIndex;
        }
        finally
        {
            _suppressTabSideEffects = false;
        }
    }

    private void OpenMaterialReceiptDialog()
    {
        var session = RequireSession();
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var viewModel = new MaterialReceiptDialogViewModel(
            _materialReceipts,
            _repository,
            _suppliers,
            _supplierEditor,
            _goldTypeEditor,
            _funds,
            _banks,
            session);
        var dialog = new MaterialReceiptDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            OpenDocumentTab("RECEIPT", WarehouseTabs.Receipt);
            _ = RefreshAllAsync();
        }
    }

    private void OpenFinishedProductTransferDialog()
    {
        var session = RequireSession();
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var dialog = new FinishedProductTransferDialog(
            new FinishedProductTransferDialogViewModel(_finishedProductTransfers, _repository, session))
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
            _ = RefreshAllAsync();
    }

    private void OpenSalesIntakeApprovalDialog()
    {
        var session = RequireSession();
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var viewModel = new SalesIntakeApprovalDialogViewModel(_finishedProductTransfers, session);
        var dialog = new SalesIntakeApprovalDialog(viewModel)
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
        if (viewModel.Changed)
        {
            OpenDocumentTab("TRANSFER", WarehouseTabs.Transfer);
            _ = RefreshAllAsync();
        }
    }

    private void OpenProductionFinishedReceiptDialog()
    {
        var session = RequireSession();
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var dialog = new ProductionFinishedReceiptDialog(
            new ProductionFinishedReceiptDialogViewModel(_repository, _productionQueries, _productionApplication, session))
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() == true)
        {
            OpenDocumentTab("PRODUCTION_FINISHED_RECEIPT", WarehouseTabs.Receipt);
            _ = RefreshAllAsync();
        }
    }

    private void OpenWarehouseGoldPriceDialog()
    {
        var session = RequireSession();
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var dialog = new WarehouseGoldPriceDialog(
            new WarehouseGoldPriceDialogViewModel(_warehouseGoldPrices, session))
        {
            Owner = Application.Current?.MainWindow
        };
        dialog.ShowDialog();
    }

    private void OpenDocumentTab(string documentType, int tabIndex)
    {
        _suppressTabSideEffects = true;
        try
        {
            SelectedDocumentType = documentType;
            SelectedTabIndex = tabIndex;
        }
        finally
        {
            _suppressTabSideEffects = false;
        }

        _ = SearchDocumentsAsync();
    }

    private void OnTabChanged(int tabIndex)
    {
        switch (tabIndex)
        {
            case WarehouseTabs.Receipt:
                SelectedDocumentType = "RECEIPT";
                _ = SearchDocumentsAsync();
                break;
            case WarehouseTabs.Issue:
                SelectedDocumentType = "ISSUE";
                _ = SearchDocumentsAsync();
                break;
            case WarehouseTabs.Transfer:
                SelectedDocumentType = "TRANSFER";
                _ = SearchDocumentsAsync();
                break;
        }
    }

    private StockDocumentSearchFilter BuildDocumentFilterForCurrentTab()
    {
        var type = SelectedDocumentType == "Tất cả" ? string.Empty : SelectedDocumentType;
        if (SelectedTabIndex == WarehouseTabs.Receipt)
        {
            type = SelectedDocumentType == "PRODUCTION_FINISHED_RECEIPT"
                ? "PRODUCTION_FINISHED_RECEIPT"
                : "RECEIPT,PRODUCTION_FINISHED_RECEIPT";
        }
        else if (SelectedTabIndex == WarehouseTabs.Issue)
        {
            // Issue tab: XK (ISSUE) + GX giao NVL SX (PRODUCTION_MATERIAL_ISSUE) by default.
            // Narrow via SelectedDocumentType when user picks a specific issue-family type.
            type = SelectedDocumentType switch
            {
                "PRODUCTION_MATERIAL_ISSUE" => "PRODUCTION_MATERIAL_ISSUE",
                "FINISHED_GOODS_ISSUE" => "FINISHED_GOODS_ISSUE",
                "OUTSOURCED_CRAFT_ISSUE" => "OUTSOURCED_CRAFT_ISSUE",
                "ISSUE" or "Tất cả" or "" or null => "ISSUE,PRODUCTION_MATERIAL_ISSUE",
                _ => string.IsNullOrWhiteSpace(SelectedDocumentType)
                    ? "ISSUE,PRODUCTION_MATERIAL_ISSUE"
                    : SelectedDocumentType
            };
        }
        else if (SelectedTabIndex == WarehouseTabs.Transfer)
        {
            type = "TRANSFER";
        }

        return new StockDocumentSearchFilter(
            DocumentSearchText,
            type,
            SelectedDocumentStatus == "Tất cả" ? string.Empty : SelectedDocumentStatus,
            null,
            null);
    }

    private void ClearBalanceFilters()
    {
        BalanceSearchText = string.Empty;
        SelectedMaterialType = "Tất cả";
        SelectedLocation = Locations.FirstOrDefault();
        LowStockOnly = false;
        _ = SearchBalancesAsync();
    }

    private void ClearFinishedFilters()
    {
        FinishedSearchText = string.Empty;
        SelectedFinishedGroup = ProductGroupFilterOptions.FirstOrDefault();
        SelectedFinishedGoldType = "Tất cả loại vàng";
        SelectedFinishedStatus = "Tất cả trạng thái";
        _ = SearchFinishedProductsAsync();
    }

    private void ClearSaleFilters()
    {
        SaleSearchText = string.Empty;
        SelectedSaleGroup = ProductGroupFilterOptions.FirstOrDefault();
        SelectedSaleGoldType = "Tất cả loại vàng";
        SelectedSaleStatus = "Tất cả trạng thái";
        _ = SearchSaleItemsAsync();
    }

    private void ClearDocumentFilters()
    {
        DocumentSearchText = string.Empty;
        if (SelectedTabIndex is WarehouseTabs.Receipt or WarehouseTabs.Issue or WarehouseTabs.Transfer)
        {
            // Keep the tab-forced document type.
        }
        else
        {
            SelectedDocumentType = "Tất cả";
        }

        SelectedDocumentStatus = "Tất cả";
        _ = SearchDocumentsAsync();
    }

    private void ClearMovementFilters()
    {
        MovementSearchText = string.Empty;
        SelectedMovementLocation = MovementLocations.FirstOrDefault();
        _ = SearchMovementsAsync();
    }

    private void ClearDocumentDetail()
    {
        HasDocumentDetail = false;
        DetailCode = string.Empty;
        DetailDate = string.Empty;
        DetailType = string.Empty;
        DetailStatus = string.Empty;
        DetailReference = string.Empty;
        DetailCounterparty = string.Empty;
        DetailSource = string.Empty;
        DetailNotes = string.Empty;
        DetailCreatedBy = string.Empty;
        DetailPostedBy = string.Empty;
        DetailPostedAt = string.Empty;
        DocumentLines.Clear();
    }

    private async Task LoadGoldTypeFilterOptionsAsync()
    {
        try
        {
            var types = await _goldTypes.ListAsync(activeOnly: true);
            _goldTypeCatalog = types;
            foreach (var type in types)
            {
                if (!GoldTypeFilterOptions.Contains(type.Code))
                    GoldTypeFilterOptions.Add(type.Code);
            }
        }
        catch
        {
            // Catalog chưa migrate: giữ tùy chọn mặc định.
        }
    }

    private async Task LoadProductGroupFilterOptionsAsync()
    {
        try
        {
            var session = _sessionAccessor.Current;
            if (session is null)
                return;
            var groups = await _repository.GetFinishedProductGroupsAsync(session);
            foreach (var group in groups)
            {
                if (ProductGroupFilterOptions.All(x => !x.Code.Equals(group.Code, StringComparison.OrdinalIgnoreCase)))
                    ProductGroupFilterOptions.Add(group);
            }
        }
        catch
        {
            // Catalog chưa migrate: giữ tùy chọn mặc định.
        }
    }

    private static string NormalizeGoldType(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.StartsWith("Tất cả", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return text.ToUpperInvariant();
    }

    private static string NormalizeStatusCode(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text.StartsWith("Tất cả", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return text.ToUpperInvariant();
    }

    private static string FormatSourceType(string? sourceType) =>
        (sourceType ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "SUPPLIER" => "Nhà cung cấp (NCC)",
            "OLD_GOLD_CUSTOMER" => "Vàng cũ khách hàng",
            "" => "—",
            var other => other
        };

    private static string FormatMoney(decimal value) =>
        value.ToString("N0", ViCulture) + " VNĐ";

    private AuthenticatedSession? RequireSession()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Phiên đăng nhập đã hết hạn. Vui lòng đăng nhập lại.";
            return null;
        }

        return session;
    }

    private LoadScope BeginLoad(string status)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = status;
        return new LoadScope(this, _loadCts);
    }

    private sealed class LoadScope : IDisposable
    {
        private readonly WarehouseViewModel _owner;
        private readonly CancellationTokenSource _cts;
        private bool _disposed;

        public LoadScope(WarehouseViewModel owner, CancellationTokenSource cts)
        {
            _owner = owner;
            _cts = cts;
        }

        public CancellationToken Token => _cts.Token;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (!_cts.IsCancellationRequested)
                _owner.IsLoading = false;
        }
    }
}
