using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;
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

    private ProductionMaterialStockOption? _selectedStockMaterial;
    private decimal _issueQuantity = 1m;
    private ProductionQcUnitEditorViewModel? _selectedQcUnit;
    private string _qcResultCode = ProductionQcResultCodes.Pass;
    private decimal? _qcGoldWeight;
    private string _qcWeightUnit = "CHI";
    private string _qcPurityCode = string.Empty;
    private string _qcDefectNotes = string.Empty;
    private string _completionPreview = string.Empty;

    public ProductionOrderDialogViewModel(
        long? orderId,
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        IProductionAttachmentStore attachments,
        IProductionPrintDataService printData,
        ICurrentSessionAccessor sessionAccessor,
        long branchId)
    {
        _orderId = orderId;
        _queries = queries;
        _application = application;
        _attachments = attachments;
        _printData = printData;
        _sessionAccessor = sessionAccessor;
        _branchId = branchId;

        SaveDraftCommand = new AsyncRelayCommand(SaveDraftAsync, () => IsDraft && !IsBusy);
        CancelDraftCommand = new AsyncRelayCommand(CancelDraftAsync, () => CanCancel && !IsBusy);
        CloseCommand = new RelayCommand(() => RequestClose?.Invoke(false));
        AddLineCommand = new RelayCommand(AddLine, () => IsDraft);
        RemoveLineCommand = new ParameterRelayCommand(
            p => RemoveLine(p as ProductionLineEditorViewModel),
            p => IsDraft && p is ProductionLineEditorViewModel);
        AddAttachmentCommand = new AsyncRelayCommand(AddAttachmentAsync, () => IsDraft);
        RemoveAttachmentCommand = new ParameterRelayCommand(
            p => RemoveAttachment(p as ProductionAttachmentEditorViewModel),
            p => IsDraft && p is ProductionAttachmentEditorViewModel);
        RefreshSalesOrdersCommand = new AsyncRelayCommand(LoadSalesOrdersAsync);
        RefreshEmployeesCommand = new AsyncRelayCommand(LoadEmployeesAsync);
        RefreshWorkersCommand = new AsyncRelayCommand(LoadExternalWorkersAsync);
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
        AddQcImageCommand = new AsyncRelayCommand(AddQcImageAsync, () => CanWriteQc && SelectedQcUnit is not null && !IsBusy);
        RecordQcCommand = new AsyncRelayCommand(RecordQcAsync, () => CanWriteQc && SelectedQcUnit is not null && !IsBusy);
        RecordReworkCommand = new AsyncRelayCommand(RecordReworkAsync, () => CanWriteQc && SelectedQcUnit is { CanRework: true } && !IsBusy);
        CompleteOrderCommand = new AsyncRelayCommand(CompleteOrderAsync, () => CanComplete && !IsBusy);
        PrintMaterialIssueCommand = new AsyncRelayCommand(() => PrintAsync(1), () => _id is > 0 && !IsBusy);
        PrintReturnCommand = new AsyncRelayCommand(() => PrintAsync(2), () => _id is > 0 && !IsBusy);
        PrintQcCommand = new AsyncRelayCommand(() => PrintAsync(3), () => _id is > 0 && !IsBusy);
        PrintFinishedCommand = new AsyncRelayCommand(() => PrintAsync(4), () => _id is > 0 && !IsBusy);
        PrintSettlementCommand = new AsyncRelayCommand(() => PrintAsync(5), () => _id is > 0 && !IsBusy);

        Lines.Add(new ProductionLineEditorViewModel(1));
        _ = InitializeAsync();
    }

    public event Action<bool>? RequestClose;

    public ObservableCollection<ProductionLocationItemViewModel> Locations { get; } = [];
    public ObservableCollection<ProductionSalesOrderItemViewModel> SalesOrders { get; } = [];
    public ObservableCollection<ProductionEmployeePickViewModel> Employees { get; } = [];
    public ObservableCollection<ProductionExternalWorkerItemViewModel> ExternalWorkers { get; } = [];
    public ObservableCollection<ProductionLineEditorViewModel> Lines { get; } = [];
    public ObservableCollection<ProductionAttachmentEditorViewModel> Attachments { get; } = [];
    public ObservableCollection<ProductionMaterialStockOption> StockMaterials { get; } = [];
    public ObservableCollection<ProductionIssueLineEditorViewModel> IssueLines { get; } = [];
    public ObservableCollection<ProductionReturnLineEditorViewModel> ReturnLines { get; } = [];
    public ObservableCollection<ProductionQcUnitEditorViewModel> QcUnits { get; } = [];
    public ObservableCollection<ProductionQcComponentEditorViewModel> QcComponents { get; } = [];
    public ObservableCollection<ProductionQcImageEditorViewModel> QcImages { get; } = [];

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
    public bool CanIssueMaterials => IsDraft && _id is > 0 && !_hasMaterialIssue;
    public bool CanPostReturn => IsInProgress && _hasMaterialIssue && !_hasReturn && ReturnLines.Count > 0;
    public bool CanWriteQc => IsInProgress && _hasReturn && QcUnits.Count > 0;
    public bool CanComplete => IsInProgress && _hasReturn && QcUnits.Count > 0 && QcUnits.All(x => x.IsPassed);
    public bool CanCancel => (IsDraft || IsInProgress) && _id is > 0;
    public bool CanUseExecutionTabs => _id is > 0;

    public string OrderSource
    {
        get => _orderSource;
        set
        {
            if (SetProperty(ref _orderSource, value))
            {
                OnPropertyChanged(nameof(IsSalesOrderSource));
                NotifyCommands();
            }
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
            if (SetProperty(ref _executionType, value))
            {
                OnPropertyChanged(nameof(IsInHouse));
                OnPropertyChanged(nameof(IsOutsourced));
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
        set => SetProperty(ref _selectedTabIndex, value);
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

    public ProductionQcUnitEditorViewModel? SelectedQcUnit
    {
        get => _selectedQcUnit;
        set
        {
            if (SetProperty(ref _selectedQcUnit, value))
            {
                QcComponents.Clear();
                QcImages.Clear();
                if (value is not null)
                {
                    foreach (var balance in ReturnLines)
                    {
                        QcComponents.Add(new ProductionQcComponentEditorViewModel(
                            balance.MaterialBalanceId,
                            balance.MaterialCode,
                            balance.MaterialName,
                            InferComponentKind(balance.MaterialType),
                            balance.QuantityInProduct,
                            balance.UnitCode));
                    }
                }

                NotifyCommands();
            }
        }
    }

    public string QcResultCode
    {
        get => _qcResultCode;
        set => SetProperty(ref _qcResultCode, value);
    }

    public decimal? QcGoldWeight
    {
        get => _qcGoldWeight;
        set => SetProperty(ref _qcGoldWeight, value);
    }

    public string QcWeightUnit
    {
        get => _qcWeightUnit;
        set => SetProperty(ref _qcWeightUnit, value);
    }

    public string QcPurityCode
    {
        get => _qcPurityCode;
        set => SetProperty(ref _qcPurityCode, value);
    }

    public string QcDefectNotes
    {
        get => _qcDefectNotes;
        set => SetProperty(ref _qcDefectNotes, value);
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
    public ICommand RemoveLineCommand { get; }
    public ICommand AddAttachmentCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand RefreshSalesOrdersCommand { get; }
    public ICommand RefreshEmployeesCommand { get; }
    public ICommand RefreshWorkersCommand { get; }
    public ICommand RefreshStockMaterialsCommand { get; }
    public ICommand AddIssueLineCommand { get; }
    public ICommand RemoveIssueLineCommand { get; }
    public ICommand PostMaterialIssueCommand { get; }
    public ICommand PostWorkerReturnCommand { get; }
    public ICommand AddQcImageCommand { get; }
    public ICommand RecordQcCommand { get; }
    public ICommand RecordReworkCommand { get; }
    public ICommand CompleteOrderCommand { get; }
    public ICommand PrintMaterialIssueCommand { get; }
    public ICommand PrintReturnCommand { get; }
    public ICommand PrintQcCommand { get; }
    public ICommand PrintFinishedCommand { get; }
    public ICommand PrintSettlementCommand { get; }

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

    private void ApplyDefaultLocations()
    {
        var raw = Locations.FirstOrDefault(x =>
            string.Equals(x.InventoryScope, "RAW_MATERIAL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(x.InventoryScope, "GENERAL", StringComparison.OrdinalIgnoreCase));
        var finished = Locations.FirstOrDefault(x =>
            string.Equals(x.InventoryScope, "FINISHED_GOODS", StringComparison.OrdinalIgnoreCase));
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
        {
            Lines.Add(new ProductionLineEditorViewModel(line));
        }

        if (Lines.Count == 0)
            Lines.Add(new ProductionLineEditorViewModel(1));

        foreach (var employee in Employees)
            employee.IsSelected = detail.Workers.Any(w => w.EmployeeId == employee.Id);

        Attachments.Clear();
        foreach (var attachment in detail.Attachments)
            Attachments.Add(new ProductionAttachmentEditorViewModel(attachment));

        _hasMaterialIssue = detail.MaterialIssueDocumentId is > 0;
        _hasReturn = detail.ReturnDocumentId is > 0;
        IssueLines.Clear();
        ReturnLines.Clear();
        foreach (var balance in detail.MaterialBalances)
        {
            ReturnLines.Add(new ProductionReturnLineEditorViewModel(balance, !_hasReturn && IsInProgress));
        }

        QcUnits.Clear();
        foreach (var unit in detail.QcUnits)
            QcUnits.Add(new ProductionQcUnitEditorViewModel(unit));

        RefreshCompletionPreview();
        if (CanIssueMaterials)
            await LoadStockMaterialsAsync();

        OnPropertyChanged(nameof(IsInProgress));
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(HasMaterialIssue));
        OnPropertyChanged(nameof(CanIssueMaterials));
        OnPropertyChanged(nameof(CanPostReturn));
        OnPropertyChanged(nameof(CanWriteQc));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanUseExecutionTabs));
        NotifyCommands();
    }

    private async Task LoadStockMaterialsAsync()
    {
        if (SourceLocationId <= 0)
            return;
        var items = await _queries.SearchMaterialStockAsync(_branchId, SourceLocationId, null);
        StockMaterials.Clear();
        foreach (var item in items)
            StockMaterials.Add(item);
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
    }

    private async Task RecordQcAsync()
    {
        if (SelectedQcUnit is null || _id is not > 0)
            return;

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
                images);
            await _application.RecordQcAsync(draft, RequireSession());
            await LoadDetailAsync(_id.Value);
            SelectedQcUnit = QcUnits.FirstOrDefault(x => x.Id == draft.QcUnitId);
            MessageBox.Show("Đã lưu kết quả QC.", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
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
            CompletionPreview = "Chưa có đơn vị QC.";
            return;
        }

        var passed = QcUnits.Count(x => x.IsPassed);
        var labor = QcUnits.Where(x => x.IsPassed).Sum(x => x.LaborCostSnapshot);
        CompletionPreview =
            $"Đạt QC: {passed}/{QcUnits.Count}. Tổng tiền công dự kiến: {labor:N0} VND. " +
            (IsOutsourced && labor > 0
                ? "Hoàn tất sẽ sinh một công nợ tiền công thợ ngoài."
                : "Lệnh nội bộ không sinh công nợ tiền công.");
    }

    private void AddLine()
    {
        var next = Lines.Count == 0 ? 1 : Lines.Max(x => x.LineNumber) + 1;
        Lines.Add(new ProductionLineEditorViewModel(next));
    }

    private void RemoveLine(ProductionLineEditorViewModel? line)
    {
        if (line is null || Lines.Count <= 1)
            return;
        Lines.Remove(line);
        var no = 1;
        foreach (var item in Lines.OrderBy(x => x.LineNumber))
            item.LineNumber = no++;
    }

    private async Task AddAttachmentAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Ảnh|*.jpg;*.jpeg;*.png;*.webp",
            Multiselect = false
        };
        if (dialog.ShowDialog() != true)
            return;

        var staging = await _attachments.ImportToStagingAsync(dialog.FileName);
        Attachments.Add(new ProductionAttachmentEditorViewModel(
            staging,
            Path.GetFileName(dialog.FileName)));
    }

    private void RemoveAttachment(ProductionAttachmentEditorViewModel? attachment)
    {
        if (attachment is null)
            return;
        attachment.MarkDeleted = true;
        Attachments.Remove(attachment);
        if (attachment.Id is > 0)
            _removedAttachments.Add(attachment);
        else
            _attachments.TryDeleteStagingFile(attachment.StagingPath);
    }

    private async Task SaveDraftAsync()
    {
        try
        {
            IsBusy = true;
            ErrorMessage = string.Empty;

            var employeeIds = Employees.Where(x => x.IsSelected).Select(x => x.Id).ToList();
            var lineDrafts = Lines.Select(x => x.ToDraft()).ToList();
            var attachmentDrafts = Attachments
                .Select((x, index) => x.ToDraft(index))
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

        if (MessageBox.Show(
                "Hủy lệnh bản nháp này?",
                "Xác nhận",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
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
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(CanCancel));
        ((AsyncRelayCommand)SaveDraftCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CancelDraftCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RefreshStockMaterialsCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PostMaterialIssueCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PostWorkerReturnCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)AddQcImageCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RecordQcCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)RecordReworkCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)CompleteOrderCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintMaterialIssueCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintReturnCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintQcCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintFinishedCommand).NotifyCanExecuteChanged();
        ((AsyncRelayCommand)PrintSettlementCommand).NotifyCanExecuteChanged();
    }
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
    public ProductionLineEditorViewModel(int lineNumber)
    {
        LineNumber = lineNumber;
    }

    public ProductionLineEditorViewModel(ProductionOrderLineDto line)
    {
        Id = line.Id;
        LineNumber = line.LineNumber;
        ModelCode = line.ModelCode;
        ModelName = line.ModelName;
        GoldTypeCode = line.GoldTypeCode;
        GoldTypeName = line.GoldTypeName;
        PlannedQuantity = line.PlannedQuantity;
        LaborCostPerUnit = line.LaborCostPerUnit;
        Description = line.Description;
        Notes = line.Notes;
    }

    public long? Id { get; }
    public int LineNumber { get; set; }
    public string? ModelCode { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string GoldTypeCode { get; set; } = string.Empty;
    public string GoldTypeName { get; set; } = string.Empty;
    public int PlannedQuantity { get; set; } = 1;
    public decimal LaborCostPerUnit { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }

    public ProductionOrderLineDraft ToDraft() =>
        new(Id, LineNumber, ModelCode, ModelName, GoldTypeCode, GoldTypeName,
            PlannedQuantity, LaborCostPerUnit, Description, Notes);
}

public sealed class ProductionAttachmentEditorViewModel
{
    public ProductionAttachmentEditorViewModel(string stagingPath, string originalFileName)
    {
        StagingPath = stagingPath;
        OriginalFileName = originalFileName;
        AttachmentKind = ProductionAttachmentKinds.Design;
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
    }

    public long? Id { get; }
    public string AttachmentKind { get; set; }
    public string? StagingPath { get; }
    public string? StoredPath { get; }
    public string OriginalFileName { get; }
    public string? MimeType { get; }
    public string? Caption { get; set; }
    public int DisplayOrder { get; set; }
    public bool MarkDeleted { get; set; }

    public ProductionAttachmentDraft ToDraft(int displayOrder) =>
        new(Id, AttachmentKind, StagingPath, StoredPath, OriginalFileName, MimeType, Caption, displayOrder, MarkDeleted);
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
    }

    public long MaterialId { get; }
    public string MaterialCode { get; }
    public string MaterialName { get; }
    public string UnitCode { get; }
    public decimal SnapshotCostHint { get; }
    public decimal Quantity { get; set; }
    public string? Notes { get; set; }
    public string DisplayCostHint => $"{SnapshotCostHint:N0} / {UnitCode} (ước tính tồn)";
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
        set => SetProperty(ref _quantityInProduct, value);
    }

    public decimal QuantityReturned
    {
        get => _quantityReturned;
        set => SetProperty(ref _quantityReturned, value);
    }

    public decimal QuantityScrap
    {
        get => _quantityScrap;
        set => SetProperty(ref _quantityScrap, value);
    }

    public decimal QuantityLoss
    {
        get => _quantityLoss;
        set => SetProperty(ref _quantityLoss, value);
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
    public bool IsPassed { get; }
    public bool CanRework { get; }
    public string DisplayName { get; }
}

public sealed class ProductionQcComponentEditorViewModel : ObservableObject
{
    public ProductionQcComponentEditorViewModel(
        long materialBalanceId,
        string materialCode,
        string materialName,
        string componentKind,
        decimal maxQuantity,
        string unitCode)
    {
        MaterialBalanceId = materialBalanceId;
        MaterialCode = materialCode;
        MaterialName = materialName;
        ComponentKind = componentKind;
        MaxQuantity = maxQuantity;
        UnitCode = unitCode;
    }

    public long MaterialBalanceId { get; }
    public string MaterialCode { get; }
    public string MaterialName { get; }
    public string ComponentKind { get; set; }
    public decimal MaxQuantity { get; }
    public string UnitCode { get; }

    private decimal _quantityUsed;
    public decimal QuantityUsed
    {
        get => _quantityUsed;
        set => SetProperty(ref _quantityUsed, value);
    }

    public string DisplayName => $"{MaterialCode} — {MaterialName} (tối đa {MaxQuantity:0.####} {UnitCode})";
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
