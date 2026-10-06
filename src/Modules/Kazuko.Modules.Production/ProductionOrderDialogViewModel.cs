using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Inventory;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
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
    private long _sourceLocationId;
    private long _finishedLocationId;
    private string? _deliveredByName;
    private string? _receivedByName;
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
    private ProductionQcUnitEditorViewModel? _selectedQcUnit;
    private string _qcResultCode = ProductionQcResultCodes.Pass;
    private decimal? _qcGoldWeight;
    private string _qcGoldWeightText = string.Empty;
    private string _qcWeightUnit = "CHI";
    private string _qcPurityCode = string.Empty;
    private string _qcDefectNotes = string.Empty;
    private string _completionPreview = string.Empty;
    private bool _isQcAdjustmentMode;

    private readonly IGoldTypeCatalogService _goldTypes;
    private GoldTypeOption? _selectedQcGoldType;

    public ProductionOrderDialogViewModel(
        long? orderId,
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        IProductionAttachmentStore attachments,
        IProductionPrintDataService printData,
        ICurrentSessionAccessor sessionAccessor,
        IExternalWorkerEditorService externalWorkerEditor,
        IGoldTypeCatalogService goldTypes,
        long branchId)
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
        RefreshSalesOrdersCommand = new AsyncRelayCommand(LoadSalesOrdersAsync);
        RefreshEmployeesCommand = new AsyncRelayCommand(LoadEmployeesAsync);
        RefreshWorkersCommand = new AsyncRelayCommand(LoadExternalWorkersAsync);
        CreateExternalWorkerCommand = new RelayCommand(CreateExternalWorker, () => IsDraft && !IsBusy);
        RefreshStockMaterialsCommand = new AsyncRelayCommand(LoadStockMaterialsAsync, () => CanIssueMaterials && !IsBusy);
        AddIssueLineCommand = new RelayCommand(AddIssueLine, () => CanIssueMaterials && !IsBusy);
        RemoveIssueLineCommand = new ParameterRelayCommand(
            p =>
            {
                if (p is ProductionIssueLineEditorViewModel row)
                    IssueLines.Remove(row);
            },
            p => CanIssueMaterials && p is ProductionIssueLineEditorViewModel);
        PostMaterialIssueCommand = new AsyncRelayCommand(PostMaterialIssueAsync, () => CanIssueMaterials && !IsBusy);
        PostWorkerReturnCommand = new AsyncRelayCommand(PostWorkerReturnAsync, () => CanPostReturn && !IsBusy);
        AddQcImageCommand = new AsyncRelayCommand(AddQcImageAsync, () => CanEditSelectedQc && !IsBusy);
        RecordQcCommand = new AsyncRelayCommand(RecordQcAsync, () => CanEditSelectedQc && !IsBusy);
        StartQcAdjustmentCommand = new AsyncRelayCommand(
            StartQcAdjustmentAsync,
            () => CanStartQcAdjustment && !IsBusy);
        CancelQcAdjustmentCommand = new RelayCommand(
            CancelQcAdjustment,
            () => IsQcAdjustmentMode && !IsBusy);
        RecordReworkCommand = new AsyncRelayCommand(RecordReworkAsync, () => CanWriteQc && SelectedQcUnit is { CanRework: true } && !IsBusy);
        CompleteOrderCommand = new AsyncRelayCommand(CompleteOrderAsync, () => CanComplete && !IsBusy);
        PrintMaterialIssueCommand = new AsyncRelayCommand(() => PrintAsync(1), () => _id is > 0 && !IsBusy);
        PrintReturnCommand = new AsyncRelayCommand(() => PrintAsync(2), () => _id is > 0 && !IsBusy);
        PrintQcCommand = new AsyncRelayCommand(() => PrintAsync(3), () => _id is > 0 && !IsBusy);
        PrintFinishedCommand = new AsyncRelayCommand(() => PrintAsync(4), () => _id is > 0 && !IsBusy);
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
    public ObservableCollection<ProductionEmployeePickViewModel> Employees { get; } = [];
    public ObservableCollection<ProductionExternalWorkerItemViewModel> ExternalWorkers { get; } = [];
    public ObservableCollection<ProductionLineEditorViewModel> Lines { get; } = [];
    public ObservableCollection<ProductionGalleryItemViewModel> GalleryImages { get; } = [];
    public ObservableCollection<ProductionMaterialStockOption> StockMaterials { get; } = [];
    public ObservableCollection<ProductionIssueLineEditorViewModel> IssueLines { get; } = [];
    public ObservableCollection<ProductionReturnLineEditorViewModel> ReturnLines { get; } = [];
    public ObservableCollection<ProductionQcUnitEditorViewModel> QcUnits { get; } = [];
    public ObservableCollection<ProductionQcComponentEditorViewModel> QcComponents { get; } = [];
    public ObservableCollection<ProductionQcImageEditorViewModel> QcImages { get; } = [];
    public ObservableCollection<GoldTypeOption> GoldTypeOptions { get; } = [];

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
    private bool _hasReturn;

    public bool HasMaterialIssue => _hasMaterialIssue;
    public bool HasReturn => _hasReturn;
    public bool CanIssueMaterials => IsDraft && _id is > 0 && !_hasMaterialIssue;
    public bool CanPostReturn => IsInProgress && _hasMaterialIssue && !_hasReturn && ReturnLines.Count > 0;
    public bool CanWriteQc => IsInProgress && _hasReturn && QcUnits.Count > 0;
    public bool IsQcAdjustmentMode
    {
        get => _isQcAdjustmentMode;
        private set
        {
            if (SetProperty(ref _isQcAdjustmentMode, value))
            {
                OnPropertyChanged(nameof(CanEditSelectedQc));
                OnPropertyChanged(nameof(CanStartQcAdjustment));
                OnPropertyChanged(nameof(QcChecklistText));
                ((AsyncRelayCommand)StartQcAdjustmentCommand).NotifyCanExecuteChanged();
                ((RelayCommand)CancelQcAdjustmentCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)AddQcImageCommand).NotifyCanExecuteChanged();
                ((AsyncRelayCommand)RecordQcCommand).NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanStartQcAdjustment =>
        CanWriteQc
        && !IsCompleted
        && SelectedQcUnit is { IsPassed: true, FinishedProductId: null }
        && !IsQcAdjustmentMode;

    public bool CanEditSelectedQc =>
        CanWriteQc
        && SelectedQcUnit is not null
        && (!SelectedQcUnit.IsPassed || IsQcAdjustmentMode);
    public bool CanComplete => IsInProgress && _hasReturn && QcUnits.Count > 0 && QcUnits.All(x => x.IsPassed);
    public bool CanCancel => (IsDraft || IsInProgress) && _id is > 0;
    public bool CanUseExecutionTabs => _id is > 0;
    public bool ShowIssueComposer => CanIssueMaterials;
    public bool ShowIssueHistory => HasMaterialIssue;
    public bool ShowReturnReady => CanPostReturn || (HasMaterialIssue && HasReturn);
    public bool ShowReturnBlocked => !HasMaterialIssue;
    public bool ShowQcReady => CanWriteQc;
    public bool ShowQcBlocked => !CanWriteQc;
    public bool ShowCompleteReady => CanComplete || IsCompleted;

    public ObservableCollection<ProductionWorkflowStepViewModel> WorkflowSteps { get; } = [];

    public string WorkflowHint
    {
        get
        {
            if (IsCompleted)
                return "Lệnh đã hoàn tất. Có thể in chứng từ ở tab Tổng kết.";
            if (_id is not > 0)
                return "Bước 1: điền thông tin, thêm mẫu, rồi Lưu bản nháp.";
            if (IsDraft && Lines.Count == 0)
                return "Bước 1: thêm ít nhất một mẫu sản phẩm, rồi Lưu bản nháp.";
            if (CanIssueMaterials)
                return "Bước 2: chọn NVL còn tồn → Thêm dòng → Ghi sổ giao NVL (một lần).";
            if (HasMaterialIssue && !HasReturn)
                return "Bước 3: đối soát đã giao = vào TP + trả lại + phế liệu + hao hụt, rồi Ghi nhận nhận hàng.";
            if (CanWriteQc && !CanComplete)
                return "Bước 4: QC từng món (Đạt cần TL vàng, tuổi, cấu thành > 0 và ≥ 1 ảnh).";
            if (CanComplete)
                return "Bước 5: kiểm tra tổng kết rồi Hoàn tất lệnh để sinh serial/thành phẩm.";
            return "Theo dõi tiến trình lệnh trên thanh bước phía trên.";
        }
    }

    public string IssueTabEmptyMessage =>
        _id is not > 0
            ? "Cần Lưu bản nháp trước khi giao nguyên liệu."
            : "Đã ghi sổ giao NVL. Dữ liệu bên dưới chỉ đọc.";

    public string ReturnTabEmptyMessage =>
        !HasMaterialIssue
            ? "Cần ghi sổ giao NVL trước khi nhận & đối soát."
            : HasReturn
                ? "Đã ghi nhận nhận hàng. Bảng đối soát bên dưới chỉ đọc."
                : string.Empty;

    public string QcTabEmptyMessage =>
        !HasMaterialIssue
            ? "Cần giao NVL và nhận hàng trước khi QC."
            : !HasReturn
                ? "Cần ghi nhận nhận hàng trước — hệ thống sẽ tạo đơn vị QC theo số lượng mẫu."
                : "Chọn một món bên trái để nhập hoặc xem kết quả QC.";

    public string CompleteTabBlockedMessage
    {
        get
        {
            if (IsCompleted)
                return string.Empty;
            if (!HasMaterialIssue)
                return "Chưa giao NVL — chưa thể hoàn tất.";
            if (!HasReturn)
                return "Chưa nhận hàng / đối soát — chưa thể hoàn tất.";
            if (QcUnits.Count == 0)
                return "Chưa có đơn vị QC.";
            var pending = QcUnits.Count(x => !x.IsPassed);
            return pending > 0
                ? $"Còn {pending}/{QcUnits.Count} món chưa đạt QC. Hoàn tất khi mọi món đều Đạt."
                : string.Empty;
        }
    }

    public string ReturnBalanceSummary
    {
        get
        {
            if (ReturnLines.Count == 0)
                return "Chưa có dòng đối soát.";
            var balanced = ReturnLines.Count(x => x.IsBalanced);
            if (balanced == ReturnLines.Count)
                return $"Đối soát cân bằng: {balanced}/{ReturnLines.Count} dòng.";
            return $"Còn lệch: {ReturnLines.Count - balanced}/{ReturnLines.Count} dòng (đã giao phải bằng tổng các cột còn lại).";
        }
    }

    public bool IsReturnFullyBalanced =>
        ReturnLines.Count > 0 && ReturnLines.All(x => x.IsBalanced);

    public string QcChecklistText
    {
        get
        {
            if (SelectedQcUnit is null)
                return "Chọn một món để xem checklist QC.";
            if (SelectedQcUnit.IsPassed && !IsQcAdjustmentMode)
            {
                return "Đã đạt QC — form chỉ xem. Bấm Điều chỉnh QC để sửa (tạo lần điều chỉnh có audit).";
            }

            var isPass = string.Equals(QcResultCode, ProductionQcResultCodes.Pass, StringComparison.OrdinalIgnoreCase);
            if (IsQcAdjustmentMode)
            {
                var reasonOk = !string.IsNullOrWhiteSpace(QcDefectNotes);
                if (!isPass)
                    return $"Điều chỉnh → Không đạt: {(reasonOk ? "✓" : "○")} Lý do";

                var weightOk = QcGoldWeight is > 0;
                var unitOk = !string.IsNullOrWhiteSpace(QcWeightUnit);
                var purityOk = !string.IsNullOrWhiteSpace(QcPurityCode);
                var componentOk = QcComponents.Any(x => x.QuantityUsed > 0);
                var imageOk = QcImages.Count > 0;
                return $"Điều chỉnh → Đạt: {(reasonOk ? "✓" : "○")} Lý do  {(weightOk ? "✓" : "○")} TL  {(unitOk ? "✓" : "○")} ĐVT  {(purityOk ? "✓" : "○")} Tuổi  {(componentOk ? "✓" : "○")} Cấu thành  {(imageOk ? "✓" : "○")} Ảnh mới";
            }

            if (!isPass)
            {
                var noteOk = !string.IsNullOrWhiteSpace(QcDefectNotes);
                return $"Không đạt: {(noteOk ? "✓" : "○")} Lý do lỗi";
            }

            {
                var weightOk = QcGoldWeight is > 0;
                var unitOk = !string.IsNullOrWhiteSpace(QcWeightUnit);
                var purityOk = !string.IsNullOrWhiteSpace(QcPurityCode);
                var componentOk = QcComponents.Any(x => x.QuantityUsed > 0);
                var imageOk = QcImages.Count > 0;
                return $"Đạt: {(weightOk ? "✓" : "○")} TL vàng  {(unitOk ? "✓" : "○")} ĐVT  {(purityOk ? "✓" : "○")} Tuổi vàng  {(componentOk ? "✓" : "○")} Cấu thành  {(imageOk ? "✓" : "○")} Ảnh QC";
            }
        }
    }

    public IReadOnlyList<ProductionNamedOption> OrderSourceOptions { get; } =
    [
        new(ProductionOrderSources.Stock, "Nhập kho"),
        new(ProductionOrderSources.SalesOrder, "Theo đơn bán")
    ];

    public IReadOnlyList<ProductionNamedOption> ExecutionTypeOptions { get; } =
    [
        new(ProductionExecutionTypes.InHouse, "Nội bộ"),
        new(ProductionExecutionTypes.Outsourced, "Thợ ngoài")
    ];

    public IReadOnlyList<ProductionNamedOption> QcResultOptions { get; } =
    [
        new(ProductionQcResultCodes.Pass, "Đạt"),
        new(ProductionQcResultCodes.Fail, "Không đạt")
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
                    SalesOrderId = null;
                OnPropertyChanged(nameof(IsSalesOrderSource));
                OnPropertyChanged(nameof(SelectedOrderSourceOption));
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

    public ProductionNamedOption? SelectedQcResultOption
    {
        get => QcResultOptions.FirstOrDefault(x =>
            string.Equals(x.Code, QcResultCode, StringComparison.OrdinalIgnoreCase));
        set
        {
            if (value is not null)
                QcResultCode = value.Code;
        }
    }

    public bool IsSalesOrderSource =>
        string.Equals(OrderSource, ProductionOrderSources.SalesOrder, StringComparison.OrdinalIgnoreCase);

    public long? SalesOrderId
    {
        get => _salesOrderId;
        set => SetProperty(ref _salesOrderId, value);
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

    public string? DeliveredByName
    {
        get => _deliveredByName;
        set => SetProperty(ref _deliveredByName, value);
    }

    public string? ReceivedByName
    {
        get => _receivedByName;
        set => SetProperty(ref _receivedByName, value);
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
                return "Chọn kho xuất NVL ở tab Thông tin lệnh trước.";
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
        set => SetProperty(ref _selectedStockMaterial, value);
    }

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

    public ProductionQcUnitEditorViewModel? SelectedQcUnit
    {
        get => _selectedQcUnit;
        set
        {
            if (SetProperty(ref _selectedQcUnit, value))
            {
                _isQcAdjustmentMode = false;
                OnPropertyChanged(nameof(IsQcAdjustmentMode));
                OnPropertyChanged(nameof(CanStartQcAdjustment));
                _ = HydrateQcFormFromSelectedUnitAsync();
                NotifyCommands();
            }
        }
    }

    public string QcResultCode
    {
        get => _qcResultCode;
        set
        {
            if (SetProperty(ref _qcResultCode, value))
            {
                OnPropertyChanged(nameof(SelectedQcResultOption));
                OnPropertyChanged(nameof(QcChecklistText));
            }
        }
    }

    public decimal? QcGoldWeight
    {
        get => _qcGoldWeight;
        set
        {
            if (!SetProperty(ref _qcGoldWeight, value))
                return;

            var text = value is null
                ? string.Empty
                : value.Value.ToString("0.####", CultureInfo.CurrentCulture);
            if (!string.Equals(_qcGoldWeightText, text, StringComparison.Ordinal))
            {
                _qcGoldWeightText = text;
                OnPropertyChanged(nameof(QcGoldWeightText));
            }

            OnPropertyChanged(nameof(QcChecklistText));
        }
    }

    /// <summary>Cho phép gõ số thập phân (dấu phẩy/chấm theo locale) ở ô trọng lượng vàng.</summary>
    public string QcGoldWeightText
    {
        get => _qcGoldWeightText;
        set
        {
            var text = value ?? string.Empty;
            if (!SetProperty(ref _qcGoldWeightText, text))
                return;

            if (string.IsNullOrWhiteSpace(text))
            {
                if (_qcGoldWeight is not null)
                    SetProperty(ref _qcGoldWeight, null, nameof(QcGoldWeight));
                OnPropertyChanged(nameof(QcChecklistText));
                return;
            }

            if (decimal.TryParse(
                    text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var parsed)
                || decimal.TryParse(
                    text.Trim().Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                if (_qcGoldWeight != parsed)
                    SetProperty(ref _qcGoldWeight, parsed, nameof(QcGoldWeight));
            }

            OnPropertyChanged(nameof(QcChecklistText));
        }
    }

    public string QcWeightUnit
    {
        get => _qcWeightUnit;
        set
        {
            if (SetProperty(ref _qcWeightUnit, value))
                OnPropertyChanged(nameof(QcChecklistText));
        }
    }

    public string QcPurityCode
    {
        get => _qcPurityCode;
        set
        {
            if (SetProperty(ref _qcPurityCode, value))
            {
                SyncSelectedQcGoldTypeFromCode();
                OnPropertyChanged(nameof(QcChecklistText));
            }
        }
    }

    public GoldTypeOption? SelectedQcGoldType
    {
        get => _selectedQcGoldType;
        set
        {
            if (!SetProperty(ref _selectedQcGoldType, value))
                return;
            if (value is not null
                && !string.Equals(_qcPurityCode, value.Code, StringComparison.OrdinalIgnoreCase))
            {
                _qcPurityCode = value.Code;
                OnPropertyChanged(nameof(QcPurityCode));
                OnPropertyChanged(nameof(QcChecklistText));
            }
        }
    }

    public string QcDefectNotes
    {
        get => _qcDefectNotes;
        set
        {
            if (SetProperty(ref _qcDefectNotes, value))
                OnPropertyChanged(nameof(QcChecklistText));
        }
    }

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
    public ICommand RefreshSalesOrdersCommand { get; }
    public ICommand RefreshEmployeesCommand { get; }
    public ICommand RefreshWorkersCommand { get; }
    public ICommand CreateExternalWorkerCommand { get; }
    public ICommand RefreshStockMaterialsCommand { get; }
    public ICommand AddIssueLineCommand { get; }
    public ICommand RemoveIssueLineCommand { get; }
    public ICommand PostMaterialIssueCommand { get; }
    public ICommand PostWorkerReturnCommand { get; }
    public ICommand AddQcImageCommand { get; }
    public ICommand RecordQcCommand { get; }
    public ICommand StartQcAdjustmentCommand { get; }
    public ICommand CancelQcAdjustmentCommand { get; }
    public ICommand RecordReworkCommand { get; }
    public ICommand CompleteOrderCommand { get; }
    public ICommand PrintMaterialIssueCommand { get; }
    public ICommand PrintReturnCommand { get; }
    public ICommand PrintQcCommand { get; }
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
            await LoadSalesOrdersAsync();
            await LoadEmployeesAsync();
            await LoadExternalWorkersAsync();

            if (_orderId is > 0)
                await LoadDetailAsync(_orderId.Value);
            else
                ApplyDefaultLocations();

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
            // Catalog chưa sẵn sàng — QC vẫn có thể để trống.
        }

        SyncSelectedQcGoldTypeFromCode();
    }

    private void SyncSelectedQcGoldTypeFromCode()
    {
        if (string.IsNullOrWhiteSpace(_qcPurityCode))
        {
            if (_selectedQcGoldType is not null)
            {
                _selectedQcGoldType = null;
                OnPropertyChanged(nameof(SelectedQcGoldType));
            }
            return;
        }

        var match = GoldTypeOptions.FirstOrDefault(x =>
            string.Equals(x.Code, _qcPurityCode, StringComparison.OrdinalIgnoreCase));
        if (!Equals(_selectedQcGoldType, match))
        {
            _selectedQcGoldType = match;
            OnPropertyChanged(nameof(SelectedQcGoldType));
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

    private async Task LoadSalesOrdersAsync()
    {
        var items = await _queries.SearchEligibleSalesOrdersAsync(_branchId, null);
        SalesOrders.Clear();
        foreach (var item in items)
            SalesOrders.Add(new ProductionSalesOrderItemViewModel(item));
    }

    private async Task LoadEmployeesAsync()
    {
        var selected = Employees.Where(x => x.IsSelected).Select(x => x.Id).ToHashSet();
        var items = await _queries.SearchWorkingEmployeesAsync(null);
        Employees.Clear();
        foreach (var item in items)
        {
            var vm = new ProductionEmployeePickViewModel(item);
            vm.IsSelected = selected.Contains(item.Id);
            vm.SelectionChanged += (_, _) => OnPropertyChanged(nameof(Employees));
            Employees.Add(vm);
        }
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
        ExecutionType = detail.ExecutionType;
        ExternalWorkerId = detail.ExternalWorkerId;
        OrderDate = detail.OrderDate;
        SourceLocationId = detail.SourceLocationId;
        FinishedLocationId = detail.FinishedLocationId;
        DeliveredByName = detail.DeliveredByName;
        ReceivedByName = detail.ReceivedByName;
        Notes = detail.Notes;
        _rowVersion = detail.RowVersion;
        OnPropertyChanged(nameof(WindowTitle));

        if (detail.SalesOrderId is > 0
            && SalesOrders.All(x => x.Id != detail.SalesOrderId)
            && !string.IsNullOrWhiteSpace(detail.SalesOrderCode))
        {
            SalesOrders.Insert(0, new ProductionSalesOrderItemViewModel(
                detail.SalesOrderId.Value,
                detail.SalesOrderCode,
                "(đơn đã gắn)",
                string.Empty,
                detail.OrderDate));
        }

        Lines.Clear();
        foreach (var line in detail.Lines)
            Lines.Add(new ProductionLineEditorViewModel(line));

        foreach (var employee in Employees)
            employee.IsSelected = detail.Workers.Any(w => w.EmployeeId == employee.Id);

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

        _hasMaterialIssue = detail.MaterialIssueDocumentId is > 0;
        _hasReturn = detail.ReturnDocumentId is > 0;
        IssueLines.Clear();
        ReturnLines.Clear();
        foreach (var balance in detail.MaterialBalances)
        {
            if (_hasMaterialIssue)
            {
                IssueLines.Add(ProductionIssueLineEditorViewModel.FromPostedBalance(balance));
            }

            var returnLine = new ProductionReturnLineEditorViewModel(balance, !_hasReturn && IsInProgress);
            returnLine.PropertyChanged += OnReturnLinePropertyChanged;
            ReturnLines.Add(returnLine);
        }

        var previousQcId = SelectedQcUnit?.Id;
        QcUnits.Clear();
        foreach (var unit in detail.QcUnits)
            QcUnits.Add(new ProductionQcUnitEditorViewModel(unit));

        RefreshCompletionPreview();
        if (CanIssueMaterials)
            await LoadStockMaterialsAsync();

        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(HasMaterialIssue));
        OnPropertyChanged(nameof(HasReturn));
        OnPropertyChanged(nameof(CanIssueMaterials));
        OnPropertyChanged(nameof(CanPostReturn));
        OnPropertyChanged(nameof(CanWriteQc));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanUseExecutionTabs));
        OnPropertyChanged(nameof(ReturnBalanceSummary));
        OnPropertyChanged(nameof(IsReturnFullyBalanced));
        RefreshWorkflowSteps();
        NotifyCommands();

        SelectedQcUnit = previousQcId is > 0
            ? QcUnits.FirstOrDefault(x => x.Id == previousQcId) ?? QcUnits.FirstOrDefault()
            : QcUnits.FirstOrDefault();
    }

    private async Task LoadStockMaterialsAsync()
    {
        try
        {
            if (SourceLocationId <= 0)
            {
                StockMaterials.Clear();
                OnPropertyChanged(nameof(StockMaterialsHint));
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

        if (IssueLines.Any(x => x.MaterialId == SelectedStockMaterial.MaterialId))
        {
            ErrorMessage = "NVL đã có trong phiếu giao.";
            return;
        }

        IssueLines.Add(new ProductionIssueLineEditorViewModel(SelectedStockMaterial, IssueQuantity));
        ErrorMessage = string.Empty;
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
                DeliveredByName,
                ReceivedByName,
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

    private async Task PostWorkerReturnAsync()
    {
        if (_id is not > 0)
            return;

        if (MessageBox.Show(
                "Ghi nhận nhận hàng và đối soát? Thao tác chỉ thực hiện một lần.",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var draft = new ProductionWorkerReturnDraft(
                _id.Value,
                _rowVersion,
                DateTime.Now,
                DeliveredByName,
                ReceivedByName,
                Notes,
                ReturnLines.Select(x => x.ToDraft()).ToList());
            var result = await _application.PostWorkerReturnAsync(draft, RequireSession());
            _rowVersion = result.RowVersion;
            await LoadDetailAsync(_id.Value);
            MessageBox.Show(
                $"Đã ghi nhận nhận hàng {result.DocumentCode}. Tạo {result.QcUnitCount} đơn vị QC.",
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

    private async Task AddQcImageAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.webp",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        var staging = await _attachments.ImportToStagingAsync(dialog.FileName);
        QcImages.Add(new ProductionQcImageEditorViewModel(
            staging, Path.GetFileName(dialog.FileName), QcImages.Count));
        OnPropertyChanged(nameof(QcChecklistText));
    }

    private async Task RecordQcAsync()
    {
        if (SelectedQcUnit is null || _id is not > 0)
            return;

        var adjusting = IsQcAdjustmentMode && SelectedQcUnit.IsPassed;
        if (adjusting)
        {
            if (MessageBox.Show(
                    "Lưu điều chỉnh QC? Hệ thống tạo lần QC mới (audit), không ghi đè lịch sử cũ.",
                    "Xác nhận điều chỉnh",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            var components = QcComponents
                .Where(x => x.QuantityUsed > 0)
                .Select(x => new ProductionQcComponentDraft(x.MaterialBalanceId, x.ComponentKind, x.QuantityUsed))
                .ToList();
            var images = QcImages
                .Select(x => new ProductionQcImageDraft(x.StagingPath, x.OriginalFileName, null, null, x.DisplayOrder))
                .ToList();
            var draft = new ProductionQcDraft(
                SelectedQcUnit.Id,
                SelectedQcUnit.RowVersion,
                QcResultCode,
                QcGoldWeight,
                QcWeightUnit,
                QcPurityCode,
                QcDefectNotes,
                components,
                images,
                IsAdjustment: adjusting);
            await _application.RecordQcAsync(draft, RequireSession());
            IsQcAdjustmentMode = false;
            await LoadDetailAsync(_id.Value);
            SelectedQcUnit = QcUnits.FirstOrDefault(x => x.Id == draft.QcUnitId);
            MessageBox.Show(
                adjusting ? "Đã lưu điều chỉnh QC." : "Đã lưu kết quả QC.",
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

    private async Task StartQcAdjustmentAsync()
    {
        if (SelectedQcUnit is null || !CanStartQcAdjustment)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            IsQcAdjustmentMode = true;
            QcImages.Clear();

            if (string.IsNullOrWhiteSpace(QcDefectNotes))
                QcDefectNotes = string.Empty;

            await RebuildQcComponentsAsync(SelectedQcUnit, isAdjustment: true);
            OnPropertyChanged(nameof(QcChecklistText));
            OnPropertyChanged(nameof(CanEditSelectedQc));
        }
        catch (Exception ex)
        {
            IsQcAdjustmentMode = false;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelQcAdjustment()
    {
        IsQcAdjustmentMode = false;
        _ = HydrateQcFormFromSelectedUnitAsync();
    }

    private async Task RecordReworkAsync()
    {
        if (SelectedQcUnit is null || _id is not > 0)
            return;

        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;
            await _application.RecordReworkReturnAsync(
                new ProductionReworkReturnDraft(SelectedQcUnit.Id, SelectedQcUnit.RowVersion, QcDefectNotes),
                RequireSession());
            await LoadDetailAsync(_id.Value);
            SelectedQcUnit = QcUnits.FirstOrDefault(x => x.Id == SelectedQcUnit.Id);
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
                "Hoàn tất lệnh? Sẽ sinh serial/thành phẩm và công nợ (nếu thợ ngoài).",
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
                $"Hoàn tất {result.Code}. Nhập TP {result.FinishedReceiptDocumentCode}, {result.FinishedProductCount} món.",
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
                case 2:
                    var ret = await _printData.GetReturnPrintAsync(_id.Value);
                    if (ret is null) { ErrorMessage = "Chưa có phiếu nhận hàng để in."; break; }
                    _printer.PrintReturn(ret);
                    break;
                case 3:
                    var qc = await _printData.GetQcPrintAsync(_id.Value);
                    if (qc is null) { ErrorMessage = "Không có dữ liệu QC để in."; break; }
                    _printer.PrintQc(qc);
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
        if (QcUnits.Count == 0)
        {
            CompletionPreview = !HasReturn
                ? "Chưa nhận hàng — chưa có đơn vị QC / serial dự kiến."
                : "Chưa có đơn vị QC.";
            return;
        }

        var passed = QcUnits.Count(x => x.IsPassed);
        var labor = QcUnits.Where(x => x.IsPassed).Sum(x => x.LaborCostSnapshot);
        var serialPreview = CanComplete || IsCompleted
            ? $"Sẽ tạo {passed} serial/thành phẩm (mỗi món đạt = 1)."
            : $"Dự kiến tối đa {QcUnits.Count} serial khi mọi món đạt ({passed} đã đạt).";
        CompletionPreview =
            $"{serialPreview} Tổng tiền công dự kiến: {labor:N0} VND. " +
            (IsOutsourced && labor > 0
                ? "Hoàn tất sẽ sinh một công nợ tiền công thợ ngoài."
                : "Lệnh nội bộ không sinh công nợ tiền công.");
    }

    private async Task HydrateQcFormFromSelectedUnitAsync()
    {
        var unit = SelectedQcUnit;
        QcComponents.Clear();
        QcImages.Clear();

        if (unit is null)
        {
            QcResultCode = ProductionQcResultCodes.Pass;
            QcGoldWeight = null;
            QcWeightUnit = "CHI";
            QcPurityCode = string.Empty;
            SelectedQcGoldType = null;
            QcDefectNotes = string.Empty;
            OnPropertyChanged(nameof(CanEditSelectedQc));
            OnPropertyChanged(nameof(QcChecklistText));
            return;
        }

        QcGoldWeight = unit.ActualGoldWeight;
        QcWeightUnit = string.IsNullOrWhiteSpace(unit.ActualWeightUnitCode)
            ? "CHI"
            : unit.ActualWeightUnitCode!;
        QcPurityCode = unit.GoldPurityCode ?? string.Empty;
        QcDefectNotes = unit.DefectNotes ?? string.Empty;
        QcResultCode = unit.IsPassed
            ? ProductionQcResultCodes.Pass
            : unit.CanRework
                ? ProductionQcResultCodes.Fail
                : ProductionQcResultCodes.Pass;

        try
        {
            await RebuildQcComponentsAsync(unit, isAdjustment: false);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }

        OnPropertyChanged(nameof(CanEditSelectedQc));
        OnPropertyChanged(nameof(QcChecklistText));
    }

    private async Task RebuildQcComponentsAsync(ProductionQcUnitEditorViewModel unit, bool isAdjustment)
    {
        if (_id is not > 0)
            return;

        // Khi xem/điều chỉnh món Đạt: loại trừ chính món đó khỏi "đã dùng" để hiện trần còn lại đúng.
        long? excludeId = unit.IsPassed ? unit.Id : null;
        var allocations = await _queries.GetMaterialBalanceAllocationsAsync(_id.Value, excludeId);
        var remainingByBalance = allocations.ToDictionary(x => x.MaterialBalanceId, x => x);

        IReadOnlyDictionary<long, ProductionUnitComponentDto> savedByBalance =
            new Dictionary<long, ProductionUnitComponentDto>();
        if (unit.IsPassed || isAdjustment)
        {
            var saved = await _queries.GetQcUnitComponentsAsync(unit.Id);
            savedByBalance = saved.ToDictionary(x => x.MaterialBalanceId);
        }

        QcComponents.Clear();
        foreach (var balance in ReturnLines)
        {
            remainingByBalance.TryGetValue(balance.MaterialBalanceId, out var allocation);
            var remaining = allocation?.QuantityRemaining ?? balance.QuantityInProduct;
            // Trần nhập = còn lại (đã cộng lại phần của món Đạt đang xem/điều chỉnh).
            var maxForEdit = remaining < 0 ? 0 : remaining;

            var component = new ProductionQcComponentEditorViewModel(
                balance.MaterialBalanceId,
                balance.MaterialCode,
                balance.MaterialName,
                InferComponentKind(balance.MaterialType),
                maxForEdit,
                balance.UnitCode,
                allocation?.QuantityUsedByPassed ?? 0m,
                balance.QuantityInProduct);

            if (savedByBalance.TryGetValue(balance.MaterialBalanceId, out var prior))
            {
                component.ComponentKind = prior.ComponentKind;
                component.QuantityUsed = prior.QuantityUsed;
            }

            component.PropertyChanged += (_, _) => OnPropertyChanged(nameof(QcChecklistText));
            QcComponents.Add(component);
        }
    }

    private void OnReturnLinePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProductionReturnLineEditorViewModel.QuantityInProduct)
            or nameof(ProductionReturnLineEditorViewModel.QuantityReturned)
            or nameof(ProductionReturnLineEditorViewModel.QuantityScrap)
            or nameof(ProductionReturnLineEditorViewModel.QuantityLoss)
            or nameof(ProductionReturnLineEditorViewModel.IsBalanced)
            or nameof(ProductionReturnLineEditorViewModel.BalanceStatus))
        {
            OnPropertyChanged(nameof(ReturnBalanceSummary));
            OnPropertyChanged(nameof(IsReturnFullyBalanced));
        }
    }

    private void RefreshWorkflowSteps()
    {
        var current = ResolveWorkflowStepIndex();
        var definitions = new (string Title, int TabIndex)[]
        {
            ("1. Nháp", 0),
            ("2. Giao NVL", 2),
            ("3. Nhận hàng", 3),
            ("4. QC", 4),
            ("5. Hoàn tất", 5)
        };

        WorkflowSteps.Clear();
        for (var i = 0; i < definitions.Length; i++)
        {
            var (title, tabIndex) = definitions[i];
            var isDone = i < current || (i == 4 && IsCompleted);
            var isCurrent = IsCompleted ? i == 4 : i == current;
            var canNavigate = tabIndex <= 1 || (_id is > 0 && CanUseExecutionTabs);

            WorkflowSteps.Add(new ProductionWorkflowStepViewModel(
                i, title, tabIndex, isCurrent, isDone, canNavigate));
        }

        OnPropertyChanged(nameof(WorkflowHint));
        OnPropertyChanged(nameof(ShowIssueComposer));
        OnPropertyChanged(nameof(ShowIssueHistory));
        OnPropertyChanged(nameof(ShowReturnReady));
        OnPropertyChanged(nameof(ShowReturnBlocked));
        OnPropertyChanged(nameof(ShowQcReady));
        OnPropertyChanged(nameof(ShowQcBlocked));
        OnPropertyChanged(nameof(ShowCompleteReady));
        OnPropertyChanged(nameof(IssueTabEmptyMessage));
        OnPropertyChanged(nameof(ReturnTabEmptyMessage));
        OnPropertyChanged(nameof(QcTabEmptyMessage));
        OnPropertyChanged(nameof(CompleteTabBlockedMessage));
        OnPropertyChanged(nameof(CanEditSelectedQc));
    }

    private int ResolveWorkflowStepIndex()
    {
        if (IsCompleted)
            return 4;
        if (_id is not > 0 || (IsDraft && Lines.Count == 0))
            return 0;
        if (CanIssueMaterials || (IsDraft && !_hasMaterialIssue))
            return 1;
        if (HasMaterialIssue && !HasReturn)
            return 2;
        if (CanWriteQc && !CanComplete)
            return 3;
        if (CanComplete)
            return 4;
        return 0;
    }

    private void GoToCurrentWorkflowStep()
    {
        var current = ResolveWorkflowStepIndex();
        SelectedTabIndex = current switch
        {
            0 => Lines.Count == 0 ? 1 : 0,
            1 => 2,
            2 => 3,
            3 => 4,
            _ => 5
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
        RefreshWorkflowSteps();
    }

    private async Task EditLineAsync(ProductionLineEditorViewModel? line)
    {
        if (line is null)
            return;

        if (await ShowLineDialogAsync(line, isNew: false))
            RefreshGalleryImages();
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

        var dialogVm = new ProductionLineEditDialogViewModel(
            _attachments,
            line.LineNumber,
            IsDraft,
            goldStock,
            isNew ? null : line);

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

            var employeeIds = Employees.Where(x => x.IsSelected).Select(x => x.Id).ToList();
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
                SourceLocationId,
                FinishedLocationId,
                DeliveredByName,
                ReceivedByName,
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
        OnPropertyChanged(nameof(CanPostReturn));
        OnPropertyChanged(nameof(CanWriteQc));
        OnPropertyChanged(nameof(CanEditSelectedQc));
        OnPropertyChanged(nameof(CanStartQcAdjustment));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(StockMaterialsHint));
        OnPropertyChanged(nameof(ReturnBalanceSummary));
        OnPropertyChanged(nameof(IsReturnFullyBalanced));
        OnPropertyChanged(nameof(QcChecklistText));
        RefreshWorkflowSteps();
        ((AsyncRelayCommand)SaveDraftCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CancelDraftCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddLineCommand).NotifyCanExecuteChanged();
        ((ParameterRelayCommand)EditLineCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RefreshStockMaterialsCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PostMaterialIssueCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PostWorkerReturnCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddQcImageCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RecordQcCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)StartQcAdjustmentCommand).NotifyCanExecuteChanged();
        ((RelayCommand)CancelQcAdjustmentCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RecordReworkCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CompleteOrderCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintMaterialIssueCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintReturnCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintQcCommand).NotifyCanExecuteChanged();
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

public sealed class ProductionEmployeePickViewModel : ObservableObject
{
    public ProductionEmployeePickViewModel(ProductionEmployeeOption option)
    {
        Id = option.Id;
        DisplayName = $"{option.FullName} ({option.Code})";
    }

    public event EventHandler? SelectionChanged;

    public long Id { get; }
    public string DisplayName { get; }

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }
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
    private int _plannedQuantity = 1;
    private decimal _laborCostPerUnit;
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

    public int PlannedQuantity
    {
        get => _plannedQuantity;
        set => SetProperty(ref _plannedQuantity, value);
    }

    public decimal LaborCostPerUnit
    {
        get => _laborCostPerUnit;
        set => SetProperty(ref _laborCostPerUnit, value);
    }

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

    public ProductionOrderLineDraft ToDraft() =>
        new(Id, LineNumber, ModelCode, ModelName, GoldTypeCode, GoldTypeName,
            PlannedQuantity, LaborCostPerUnit, Description, Notes);
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

public sealed class ProductionIssueLineEditorViewModel
{
    public ProductionIssueLineEditorViewModel(ProductionMaterialStockOption stock, decimal quantity)
    {
        MaterialId = stock.MaterialId;
        MaterialCode = stock.Code;
        MaterialName = stock.Name;
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
    public string UnitCode { get; }
    public decimal SnapshotCostHint { get; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public bool IsPostedHistory { get; }
    public string DisplayCostHint => IsPostedHistory
        ? $"{SnapshotCostHint:N0} / {UnitCode} (snapshot khi giao)"
        : $"{SnapshotCostHint:N0} / {UnitCode} (ước tính tồn)";
}

public sealed class ProductionReturnLineEditorViewModel : ObservableObject
{
    public ProductionReturnLineEditorViewModel(ProductionMaterialBalanceDto balance, bool editable)
    {
        MaterialBalanceId = balance.Id;
        MaterialCode = balance.MaterialCode;
        MaterialName = balance.MaterialName;
        MaterialType = balance.MaterialType;
        UnitCode = balance.UnitCode;
        QuantityIssued = balance.QuantityIssued;
        QuantityInProduct = balance.QuantityInProduct;
        QuantityReturned = balance.QuantityReturned;
        QuantityScrap = balance.QuantityScrap;
        QuantityLoss = balance.QuantityLoss;
        IsEditable = editable;
        UnitCostSnapshot = balance.UnitCostSnapshot;
    }

    public long MaterialBalanceId { get; }
    public string MaterialCode { get; }
    public string MaterialName { get; }
    public string MaterialType { get; }
    public string UnitCode { get; }
    public decimal QuantityIssued { get; }
    public decimal UnitCostSnapshot { get; }
    public bool IsEditable { get; }

    private decimal _quantityInProduct;
    private decimal _quantityReturned;
    private decimal _quantityScrap;
    private decimal _quantityLoss;

    public decimal QuantityInProduct
    {
        get => _quantityInProduct;
        set
        {
            if (SetProperty(ref _quantityInProduct, value))
                NotifyBalanceChanged();
        }
    }

    public decimal QuantityReturned
    {
        get => _quantityReturned;
        set
        {
            if (SetProperty(ref _quantityReturned, value))
                NotifyBalanceChanged();
        }
    }

    public decimal QuantityScrap
    {
        get => _quantityScrap;
        set
        {
            if (SetProperty(ref _quantityScrap, value))
                NotifyBalanceChanged();
        }
    }

    public decimal QuantityLoss
    {
        get => _quantityLoss;
        set
        {
            if (SetProperty(ref _quantityLoss, value))
                NotifyBalanceChanged();
        }
    }

    public bool IsBalanced =>
        ProductionMaterialBalanceValidator.IsBalanced(
            QuantityIssued, QuantityInProduct, QuantityReturned, QuantityScrap, QuantityLoss);

    public string BalanceStatus => IsBalanced ? "Đủ" : "Lệch";

    private void NotifyBalanceChanged()
    {
        OnPropertyChanged(nameof(IsBalanced));
        OnPropertyChanged(nameof(BalanceStatus));
    }

    public ProductionReturnBalanceLineDraft ToDraft() =>
        new(MaterialBalanceId, QuantityInProduct, QuantityReturned, QuantityScrap, QuantityLoss);
}

public sealed class ProductionQcUnitEditorViewModel
{
    public ProductionQcUnitEditorViewModel(ProductionQcUnitDto dto)
    {
        Id = dto.Id;
        UnitNumber = dto.UnitNumber;
        ModelName = dto.ModelName;
        StatusCode = dto.StatusCode;
        StatusDisplay = ProductionQcUnitStatuses.ToDisplay(dto.StatusCode);
        LaborCostSnapshot = dto.LaborCostSnapshot;
        RowVersion = dto.RowVersion;
        DefectNotes = dto.DefectNotes;
        ActualGoldWeight = dto.ActualGoldWeight;
        ActualWeightUnitCode = dto.ActualWeightUnitCode;
        GoldPurityCode = dto.GoldPurityCode;
        FinishedProductId = dto.FinishedProductId;
        IsPassed = string.Equals(dto.StatusCode, ProductionQcUnitStatuses.Passed, StringComparison.OrdinalIgnoreCase);
        CanRework = string.Equals(dto.StatusCode, ProductionQcUnitStatuses.ReworkRequired, StringComparison.OrdinalIgnoreCase);
        DisplayName = $"{dto.ModelName} — món {dto.UnitNumber} ({StatusDisplay})";
    }

    public long Id { get; }
    public int UnitNumber { get; }
    public string ModelName { get; }
    public string StatusCode { get; }
    public string StatusDisplay { get; }
    public decimal LaborCostSnapshot { get; }
    public long RowVersion { get; }
    public string? DefectNotes { get; }
    public decimal? ActualGoldWeight { get; }
    public string? ActualWeightUnitCode { get; }
    public string? GoldPurityCode { get; }
    public long? FinishedProductId { get; }
    public bool IsPassed { get; }
    public bool CanRework { get; }
    public string DisplayName { get; }
}

public sealed class ProductionQcComponentEditorViewModel : ObservableObject
{
    private decimal _quantityUsed;
    private string _quantityUsedText = "0";

    public ProductionQcComponentEditorViewModel(
        long materialBalanceId,
        string materialCode,
        string materialName,
        string componentKind,
        decimal maxQuantity,
        string unitCode,
        decimal quantityUsedByOthers = 0m,
        decimal quantityInProduct = 0m)
    {
        MaterialBalanceId = materialBalanceId;
        MaterialCode = materialCode;
        MaterialName = materialName;
        ComponentKind = componentKind;
        MaxQuantity = maxQuantity;
        UnitCode = unitCode;
        QuantityUsedByOthers = quantityUsedByOthers;
        QuantityInProduct = quantityInProduct > 0 ? quantityInProduct : maxQuantity;
    }

    public long MaterialBalanceId { get; }
    public string MaterialCode { get; }
    public string MaterialName { get; }
    public string ComponentKind { get; set; }
    public decimal MaxQuantity { get; }
    public string UnitCode { get; }
    public decimal QuantityUsedByOthers { get; }
    public decimal QuantityInProduct { get; }

    public decimal QuantityUsed
    {
        get => _quantityUsed;
        set
        {
            if (!SetProperty(ref _quantityUsed, value))
                return;
            var text = value.ToString("0.####", CultureInfo.CurrentCulture);
            if (!string.Equals(_quantityUsedText, text, StringComparison.Ordinal))
            {
                _quantityUsedText = text;
                OnPropertyChanged(nameof(QuantityUsedText));
            }
        }
    }

    /// <summary>Cho phép gõ số thập phân (dấu phẩy/chấm theo locale) trong DataGrid.</summary>
    public string QuantityUsedText
    {
        get => _quantityUsedText;
        set
        {
            var text = value ?? string.Empty;
            if (!SetProperty(ref _quantityUsedText, text))
                return;

            if (string.IsNullOrWhiteSpace(text))
            {
                if (_quantityUsed != 0m)
                    SetProperty(ref _quantityUsed, 0m, nameof(QuantityUsed));
                return;
            }

            if (decimal.TryParse(
                    text.Trim(),
                    NumberStyles.Number,
                    CultureInfo.CurrentCulture,
                    out var parsed)
                || decimal.TryParse(
                    text.Trim().Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out parsed))
            {
                if (_quantityUsed != parsed)
                    SetProperty(ref _quantityUsed, parsed, nameof(QuantityUsed));
            }
        }
    }

    public string DisplayName =>
        $"{MaterialCode} — {MaterialName} (còn lại {MaxQuantity:0.####}/{QuantityInProduct:0.####} {UnitCode}; món khác đã dùng {QuantityUsedByOthers:0.####})";
}

public sealed class ProductionQcImageEditorViewModel
{
    public ProductionQcImageEditorViewModel(string stagingPath, string originalFileName, int displayOrder)
    {
        StagingPath = stagingPath;
        OriginalFileName = originalFileName;
        DisplayOrder = displayOrder;
    }

    public string StagingPath { get; }
    public string OriginalFileName { get; }
    public int DisplayOrder { get; }
}
