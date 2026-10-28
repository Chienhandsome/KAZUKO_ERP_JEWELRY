using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Warehouse.Printing;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Nhập kho thành phẩm luồng mới (14/10/2026): nhập tay từng sản phẩm.
/// - Form nhập tay: bấm Thêm để đưa vào bảng. SL=N → N dòng, mỗi dòng 1 mã TP riêng.
/// - Mã TP tiếp tục từ mã lớn nhất trong kho: prefix = chữ cái đầu nhóm hàng (N/L/D/B/V) + số thứ tự.
/// - TL vàng tự tính = TL tổng − TL đá, cho phép sửa tay.
/// - Lưu: ghi sổ qua <c>PostManualFinishedReceiptAsync</c> (không qua QC).
/// </summary>
public sealed partial class ProductionFinishedReceiptDialogViewModel : ObservableObject
{
    private static readonly string[] GoldPurityOptions =
        ["9999", "999", "750", "680", "610", "585", "416", "375", "18K", "24K"];

    private static readonly string[] SupplierOptions =
        ["Nội bộ", "Xưởng chế tác"];

    private readonly IWarehouseRepository _warehouse;
    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly AuthenticatedSession _session;
    private readonly IPrintTemplateStore _printTemplates;
    private readonly ICompanyProfileService _companyProfile;
    private readonly FinishedGoodsLabelPrintService _labelPrintService;
    private readonly Dictionary<string, int> _codeSequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ManualFinishedProductItem> _printChecked = new();

    private ProductionFinishedReceiptOrderOption? _selectedOrder;
    private ProductionOrderDetail? _detail;
    private ReceiptImageItem? _selectedImage;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;
    private DateTime _receiptDate = DateTime.Today;
    private bool _isBusy;

    // Form nhập tay
    private WarehouseLocationOption? _selectedLocation;
    private FinishedProductGroupOption? _selectedProductGroup;
    private string _productName = string.Empty;
    private string _goldPurityCode = string.Empty;
    private string _weightUnitCode = "Chỉ";
    private string _totalWeightText = string.Empty;
    private string _stoneWeightText = string.Empty;
    private string _goldWeightText = string.Empty;
    private string _quantityText = "1";
    private string _costPriceText = string.Empty;
    private string _salePriceText = string.Empty;
    private string _saleLaborCostText = string.Empty;
    private string _purchaseLaborCostText = string.Empty;
    private string _supplierName = string.Empty;
    private string _modelCode = string.Empty;
    private string _symbolCode = string.Empty;
    private FinishedProductSymbolOption? _selectedSymbol;
    private string _notes = string.Empty;
    private bool _goldWeightManual;
    private bool _isEditing;
    private int _editingIndex = -1;
    private ManualFinishedProductItem? _selectedProduct;
    private bool _generateSeparateCodes = true;

    // Đối chiếu vàng theo từng loại vàng
    private ProductionOrderWorkerDto? _selectedWorker;
    private ProductionOrderWorkerDto? _externalWorkerOption;
    private string _reconcileMessage = string.Empty;
    private string _issuedStoneSummaryText = string.Empty;
    private bool _debtRecorded;

    public ProductionFinishedReceiptDialogViewModel(
        IWarehouseRepository warehouse,
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        AuthenticatedSession session,
        IPrintTemplateStore printTemplates,
        ICompanyProfileService companyProfile, IGoldTypeCatalogService? goldTypes=null, IFinishedReceiptDraftStore? draftStore=null, IFinishedReceiptDocuments? documents=null, long? preferredOrderId=null)
    {
        _goldTypes=goldTypes;_draftStore=draftStore;_documents=documents;_preferredOrderId=preferredOrderId;
        _warehouse = warehouse;
        _queries = queries;
        _application = application;
        _session = session;
        _printTemplates = printTemplates;
        _companyProfile = companyProfile;
        _labelPrintService = new FinishedGoodsLabelPrintService(printTemplates);

        AddCommand = new RelayCommand(AddOrUpdateProduct,()=>CanEdit);
        EditCommand = new RelayCommand(LoadSelectedForEdit, () => SelectedProduct is not null && CanEdit);
        DeleteCommand = new RelayCommand(DeleteSelectedProduct, () => SelectedProduct is not null && CanEdit);
        CancelEditCommand = new RelayCommand(CancelEdit, () => CanEdit);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy&&!IsDetailLoading&&!IsPosted);
        CancelCommand = new RelayCommand(() => {if(CanClose())DialogResult=false;},()=>!IsBusy);
        SelectImageCommand = new RelayCommand<ReceiptImageItem>(item =>
        {
            if (item is not null)
                SelectedImage = item;
        });
        RecordDebtCommand = new AsyncRelayCommand(RecordDebtAsync, () => CanEdit&&_reconcileLoaded);
        OpenReconcileCommand = new RelayCommand(OpenReconcileDialog,()=>CanEdit&&_reconcileLoaded);
        CreateProductGroupCommand = new RelayCommand(CreateProductGroup, () => !IsBusy);
        CreateSymbolCommand = new RelayCommand(CreateSymbol, () => !IsBusy);
        PrintSelectedLabelsCommand = new AsyncRelayCommand(
            () => PrintLabelsAsync(onlyChecked: true),
            () => !IsBusy && IsPosted && Products.Count > 0);
        PrintAllLabelsCommand = new AsyncRelayCommand(
            () => PrintLabelsAsync(onlyChecked: false),
            () => !IsBusy && IsPosted && Products.Count > 0);
        PrintReceiptCommand = new RelayCommand(PrintPostedReceipt,()=>IsPosted&&!IsBusy);
        SaveDraftCommand=new RelayCommand(()=>{try{SaveDraft();}catch(Exception ex){ErrorMessage=ex.Message;}},()=>CanEdit&&SelectedOrder is not null);

        Products.CollectionChanged += OnProductsChanged;
        _ = LoadLookupsAsync();
    }

    public ObservableCollection<ProductionFinishedReceiptOrderOption> Orders { get; } = [];
    public ObservableCollection<ManualFinishedProductItem> Products { get; } = [];
    public ObservableCollection<ReceiptImageItem> ImageItems { get; } = [];
    public ObservableCollection<WarehouseLocationOption> Locations { get; } = [];
    public ObservableCollection<FinishedProductGroupOption> ProductGroups { get; } = [];
    public ObservableCollection<FinishedProductSymbolOption> Symbols { get; } = [];
    public ObservableCollection<ProductionOrderWorkerDto> WorkerOptions { get; } = [];

    public ObservableCollection<string> GoldPurityChoices {get;}=[];
    public IReadOnlyList<string> SupplierChoices => SupplierOptions;

    public ProductionFinishedReceiptOrderOption? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if(value==_selectedOrder)return;
            if(IsBusy||_pendingPost is not null||IsPosted||HasPendingEditor){ErrorMessage="Hoàn tất hoặc hủy món đang nhập trước khi đổi LSX.";OnPropertyChanged();return;}
            try {if(_selectedOrder is not null&&Products.Count>0)SaveDraft();}
            catch(Exception ex){ErrorMessage=ex.Message;OnPropertyChanged();return;}
            if (SetProperty(ref _selectedOrder, value)) {Products.Clear();ClearForm();_requestId=Guid.NewGuid();_ = LoadDetailAsync(value);}
        }
    }

    public ReceiptImageItem? SelectedImage
    {
        get => _selectedImage;
        set
        {
            if (ReferenceEquals(_selectedImage, value))
                return;
            if (_selectedImage is not null)
                _selectedImage.IsSelected = false;
            _selectedImage = value;
            if (_selectedImage is not null)
                _selectedImage.IsSelected = true;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Ngày nhập (hiển thị cho phiếu).
    /// </summary>
    public DateTime ReceiptDate
    {
        get => _receiptDate;
        set => SetProperty(ref _receiptDate, value);
    }

    public string OrderCode => _detail?.Code ?? string.Empty;
    public string ProductionType => _detail is null ? string.Empty
        : _detail.OrderSource == ProductionOrderSources.SalesOrder
            ? "Chế tác theo đơn" : "Chế tác nhập kho";
    public string SalesOrderCode => _detail?.SalesOrderCode ?? "—";
    public string ProductionDate => _detail?.OrderDate.ToString("dd/MM/yyyy") ?? string.Empty;
    public string Supplier => _detail is null ? string.Empty
        : _detail.ExecutionType == ProductionExecutionTypes.Outsourced
            ? $"Xưởng chế tác ngoài: {_detail.ExternalWorkerName}" : "Nội bộ";
    public string FinishedLocation => _detail?.FinishedLocationName ?? string.Empty;
    public string Notes => _detail?.Notes ?? string.Empty;
    public bool HasImages => ImageItems.Count > 0;

    #region Form nhập tay

    public WarehouseLocationOption? SelectedLocation
    {
        get => _selectedLocation;
        set => SetProperty(ref _selectedLocation, value);
    }

    public FinishedProductGroupOption? SelectedProductGroup
    {
        get => _selectedProductGroup;
        set
        {
            if (SetProperty(ref _selectedProductGroup, value))
                OnPropertyChanged(nameof(PreviewProductCode));
        }
    }

    public string ProductName
    {
        get => _productName;
        set => SetProperty(ref _productName, value);
    }

    /// <summary>Loại/tuổi vàng (cùng danh mục GOLD_TYPES).</summary>
    public string GoldPurityCode
    {
        get => _goldPurityCode;
        set => SetProperty(ref _goldPurityCode, value);
    }

    public string WeightUnitCode
    {
        get => _weightUnitCode;
        set => SetProperty(ref _weightUnitCode, value);
    }

    public string TotalWeightText
    {
        get => _totalWeightText;
        set
        {
            if (SetProperty(ref _totalWeightText, value))
                RecalculateGoldWeight();
        }
    }

    public string StoneWeightText
    {
        get => _stoneWeightText;
        set
        {
            if (SetProperty(ref _stoneWeightText, value))
                RecalculateGoldWeight();
        }
    }

    /// <summary>TL vàng: tự tính = TL tổng − TL đá, nhưng người dùng được sửa tay.</summary>
    public string GoldWeightText
    {
        get => _goldWeightText;
        set
        {
            if (SetProperty(ref _goldWeightText, value))
                _goldWeightManual = true;
        }
    }

    public string QuantityText
    {
        get => _quantityText;
        set => SetProperty(ref _quantityText, value);
    }

    public string CostPriceText
    {
        get => _costPriceText;
        set => SetProperty(ref _costPriceText, value);
    }

    public string SalePriceText
    {
        get => _salePriceText;
        set => SetProperty(ref _salePriceText, value);
    }

    public string SaleLaborCostText
    {
        get => _saleLaborCostText;
        set => SetProperty(ref _saleLaborCostText, value);
    }

    public string PurchaseLaborCostText
    {
        get => _purchaseLaborCostText;
        set => SetProperty(ref _purchaseLaborCostText, value);
    }

    public string SupplierName
    {
        get => _supplierName;
        set => SetProperty(ref _supplierName, value);
    }

    public string ModelCode
    {
        get => _modelCode;
        set => SetProperty(ref _modelCode, value);
    }

    public string SymbolCode
    {
        get => _symbolCode;
        set => SetProperty(ref _symbolCode, value);
    }

    public FinishedProductSymbolOption? SelectedSymbol
    {
        get => _selectedSymbol;
        set
        {
            if (SetProperty(ref _selectedSymbol, value))
                SymbolCode = value?.Code ?? string.Empty;
        }
    }

    public string FormNotes
    {
        get => _notes;
        set => SetProperty(ref _notes, value);
    }

    /// <summary>Mã TP xem trước: mã sẽ gán cho dòng tiếp theo khi bấm Thêm.</summary>
    public string PreviewProductCode => NextProductCode(peek: true);

    /// <summary>Tạo mã riêng cho từng món: SL=N → N dòng, mỗi dòng 1 mã riêng.</summary>
    public bool GenerateSeparateCodes
    {
        get => _generateSeparateCodes;
        set
        {
            if (SetProperty(ref _generateSeparateCodes, value))
                OnPropertyChanged(nameof(PreviewProductCode));
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(AddButtonContent));
                (CancelEditCommand as RelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public string AddButtonContent => IsEditing ? "Lưu (F1)" : "Thêm (F1)";

    public ManualFinishedProductItem? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetProperty(ref _selectedProduct, value))
            {
                NotifyReceiptCommands();
                (EditCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.NotifyCanExecuteChanged();
                if(IsPosted&&value is not null)LoadSelectedForEdit();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyReceiptCommands();
                (EditCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (CreateProductGroupCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (CreateSymbolCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (RecordDebtCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (PrintSelectedLabelsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (PrintAllLabelsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand CreateProductGroupCommand { get; }
    public ICommand CreateSymbolCommand { get; }
    public ICommand PrintSelectedLabelsCommand { get; }
    public ICommand PrintAllLabelsCommand { get; }
    public ICommand PrintReceiptCommand { get; }

    #endregion

    #region Label printing

    /// <summary>Đánh dấu / bỏ đánh dấu một SP để in tem.</summary>
    public void SetPrintChecked(ManualFinishedProductItem item, bool isChecked)
    {
        if (isChecked)
            _printChecked.Add(item);
        else
            _printChecked.Remove(item);
        (PrintSelectedLabelsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    public bool IsPrintChecked(ManualFinishedProductItem item) => _printChecked.Contains(item);

    public int PrintCheckedCount => _printChecked.Count;

    private async Task PrintLabelsAsync(bool onlyChecked)
    {
        IReadOnlyList<ManualFinishedProductItem> items = onlyChecked
            ? Products.Where(p => _printChecked.Contains(p)).ToList()
            : Products.ToList();

        if (items.Count == 0)
        {
            MessageBox.Show(
                "Vui lòng chọn ít nhất một sản phẩm để in tem.",
                "In tem",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialogVm = new LabelPrintDialogViewModel(items.Count);
        var dialog = new LabelPrintDialog(dialogVm)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dialog.ShowDialog() != true || dialogVm.DialogResult != true)
            return;

        var printerName = dialogVm.SelectedPrinter;
        var copies = dialogVm.Copies;
        if (string.IsNullOrWhiteSpace(printerName))
            return;

        try
        {
            IsBusy = true;

            var profile = await _companyProfile.GetAsync(_session);

            // Mỗi SP in riêng 1 report 1 trang → vẽ vector trực tiếp lên máy in
            // (không qua PNG trung gian) để chữ mịn, không răng cưa.
            var reports = new List<Report>();
            try
            {
                foreach (var item in items)
                {
                    reports.Add(await _labelPrintService.CreatePreparedReportAsync(
                        [item], ReceiptDate, _session, profile, dialogVm.CompactWeightUnits, dialogVm.SelectedTemplate.Code));
                }

                using var document = new PrintDocument();
                document.DocumentName = "Tem nữ trang KAZUKO";
                document.PrinterSettings.PrinterName = printerName;
                document.PrinterSettings.Copies = (short)copies;
                document.DefaultPageSettings.Landscape = false;
                document.DefaultPageSettings.PaperSize = new PaperSize("Tem 86 x 12 mm",
                    (int)Math.Round(FinishedGoodsLabelPrintService.PaperWidthMm * 100 / 25.4),
                    (int)Math.Round(FinishedGoodsLabelPrintService.PaperHeightMm * 100 / 25.4));
                document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
                document.OriginAtMargins = false;

                var pageIndex = 0;
                document.PrintPage += (_, e) =>
                {
                    var g = e.Graphics!;
                    // Canh theo khổ tem vật lý, bù hard margin của driver rồi cộng lệch dọc của máy in.
                    LabelPrintOrigin.Apply(g, e.PageSettings, dialogVm.VerticalOffsetMm);
                    VectorLabelRenderer.DrawPreparedLabel(g, reports[pageIndex]);
                    pageIndex++;
                    e.HasMorePages = pageIndex < reports.Count;
                };
                document.Print();
            }
            finally
            {
                foreach (var report in reports)
                    report.Dispose();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"In tem thất bại: {ex.Message}",
                "In tem",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Footer totals

    public string FooterLineCountText => $"{Products.Count} món";
    public string FooterTotalWeightText => $"{Products.Sum(x => x.TotalWeight):0.###} Chỉ";
    public string FooterGoldWeightText => $"{Products.Sum(x => x.GoldWeight):0.###} Chỉ";
    public string FooterStoneWeightText => $"{Products.Sum(x => x.StoneWeight):0.###} Chỉ";
    public string FooterCraftLaborText => $"{Products.Sum(x => x.PurchaseLaborCost ?? x.CraftLaborCost ?? 0):N0}";
    public string FooterCostPriceText => Products.Count == 0
        ? "—" : $"{Products.Sum(x => x.CostPrice ?? 0):N0}";
    public string FooterSalePriceText => $"{Products.Sum(x => x.SalePrice ?? 0):N0}";

    #endregion

    #region Đối chiếu vàng

    /// <summary>
    /// Thợ thực hiện lệnh (chọn để ghi công nợ khi có nợ vàng).
    /// </summary>
    public ProductionOrderWorkerDto? SelectedWorker
    {
        get => _selectedWorker;
        set {if(SetProperty(ref _selectedWorker,value))DebtRecorded=false;}
    }

    /// <summary>Các dòng đối chiếu vàng theo từng loại vàng.</summary>
    public ObservableCollection<GoldReconcileRowViewModel> GoldReconcileRows { get; } = [];

    /// <summary>Tổng nợ vàng các loại (chỉ), để hiển thị nhanh.</summary>
    public decimal TotalDebtGold => GoldReconcileRows.Sum(r => r.DebtWeight > 0 ? r.DebtWeight : 0);

    public decimal TotalIssuedGold => GoldReconcileRows.Sum(r => r.IssuedWeight);

    public decimal TotalReceivedGold => GoldReconcileRows.Sum(r => r.ReceivedWeight);

    public string TotalDebtGoldDisplay => $"{TotalDebtGold:0.###} chỉ";

    public bool HasAnyDebt => GoldReconcileRows.Any(r => r.HasDebt);

    public bool DebtRecorded
    {
        get => _debtRecorded;
        private set
        {
            if (SetProperty(ref _debtRecorded, value))
                (RecordDebtCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(ReceiptState));
        }
    }

    public string ReconcileMessage
    {
        get => _reconcileMessage;
        private set => SetProperty(ref _reconcileMessage, value);
    }

    /// <summary>
    /// Tóm tắt đá đã giao (tham khảo, không tham gia đối chiếu). Rỗng → ẩn khỏi UI.
    /// </summary>
    public string IssuedStoneSummaryText
    {
        get => _issuedStoneSummaryText;
        private set => SetProperty(ref _issuedStoneSummaryText, value);
    }

    public ICommand RecordDebtCommand { get; }
    public ICommand OpenReconcileCommand { get; }

    #endregion

    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool? DialogResult { get => _dialogResult; private set => SetProperty(ref _dialogResult, value); }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectImageCommand { get; }

    private async Task LoadLookupsAsync()
    {
        try
        {
            if(_goldTypes is null)throw new InvalidOperationException("Thiếu danh mục loại vàng.");
            foreach(var gold in await _goldTypes.ListAsync())GoldPurityChoices.Add(gold.Code);
            var locations = await _warehouse.GetLocationsAsync(_session);
            Locations.Clear();
            foreach (var location in locations.Where(location =>
                         string.Equals(
                             location.InventoryScope,
                             InventoryScopes.FinishedGoods,
                             StringComparison.OrdinalIgnoreCase)))
                Locations.Add(location);
            SelectedLocation = Locations.FirstOrDefault();

            var groups = await _warehouse.GetFinishedProductGroupsAsync(_session);
            ProductGroups.Clear();
            foreach (var group in groups)
                ProductGroups.Add(group);
            SelectedProductGroup = ProductGroups.FirstOrDefault();

            var symbols = await _warehouse.GetFinishedProductSymbolsAsync(_session);
            Symbols.Clear();
            foreach (var symbol in symbols)
                Symbols.Add(symbol);

            var existingCodes = await _queries.GetExistingFinishedProductCodesAsync(_session.BranchId);
            InitializeCodeSequences(existingCodes);
            OnPropertyChanged(nameof(PreviewProductCode));

            var orders = await _queries.GetCompletedUnreceivedOrdersAsync(_session.BranchId);
            Orders.Clear();
            foreach (var item in orders)
                Orders.Add(item);
            if(_preferredOrderId is >0 && Orders.All(x=>x.Id!=_preferredOrderId))Orders.Add(new(_preferredOrderId.Value,"LSX đã nhập kho"));
            SelectedOrder = Orders.FirstOrDefault(x=>x.Id==_preferredOrderId);
            if(_preferredOrderId is null)ErrorMessage="Chọn LSX hoàn thành để nhập hoặc tiếp tục bản nháp.";

            if (Locations.Count == 0)
                ErrorMessage = "Chưa cấu hình kho thành phẩm cho chi nhánh.";
            else if (Orders.Count == 0)
                ErrorMessage = "Không có lệnh đã hoàn thành đang chờ nhập kho.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private static bool IsImageAttachment(ProductionAttachmentDto attachment)
    {
        var path = attachment.StoredPath;
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".webp" or ".bmp" or ".gif";
    }

    private void OnProductsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        DebtRecorded=false;
        NotifyTotalsChanged();
        RefreshReconcileReceived();
        (PrintSelectedLabelsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        (PrintAllLabelsCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Dựng lại các dòng đối chiếu theo loại vàng:
    /// union purity từ vàng đã giao và từ SP đã nhập.
    /// </summary>
    private void RebuildReconcileRows(IReadOnlyList<IssuedGoldByPurityDto> issuedByPurity)
    {
        var receivedByPurity = Products
            .GroupBy(p => NormalizePurity(p.GoldPurityCode))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.GoldWeight));

        var purities = issuedByPurity
            .Select(d => NormalizePurity(d.PurityCode))
            .Union(receivedByPurity.Keys)
            .OrderBy(p => p)
            .ToList();

        GoldReconcileRows.Clear();
        foreach (var purity in purities)
        {
            var issued = issuedByPurity
                .Where(d => NormalizePurity(d.PurityCode) == purity)
                .Sum(d => d.WeightChi);
            receivedByPurity.TryGetValue(purity, out var received);
            var row = new GoldReconcileRowViewModel(purity)
            {
                IssuedWeight = issued,
                ReceivedWeight = received,
            };
            row.PropertyChanged += OnReconcileRowPropertyChanged;
            GoldReconcileRows.Add(row);
        }
        NotifyReconcileTotalsChanged();
    }

    private static string NormalizePurity(string? purity) =>
        string.IsNullOrWhiteSpace(purity) ? string.Empty : purity.Trim().ToUpperInvariant();

    /// <summary>
    /// Tính lại vàng đã nhận từng dòng khi bảng SP thay đổi (giữ nguyên hao hụt đã nhập).
    /// </summary>
    private void RefreshReconcileReceived()
    {
        var receivedByPurity = Products
            .GroupBy(p => NormalizePurity(p.GoldPurityCode))
            .ToDictionary(g => g.Key, g => g.Sum(p => p.GoldWeight));

        var known = new HashSet<string>(GoldReconcileRows.Select(r => NormalizePurity(r.PurityCode)));
        foreach (var row in GoldReconcileRows)
        {
            receivedByPurity.TryGetValue(NormalizePurity(row.PurityCode), out var received);
            row.ReceivedWeight = received;
        }

        // Thêm dòng mới nếu SP có loại vàng chưa từng xuất hiện.
        foreach (var kv in receivedByPurity.Where(kv => !known.Contains(kv.Key)).OrderBy(kv => kv.Key))
        {
            var row = new GoldReconcileRowViewModel(kv.Key)
            {
                IssuedWeight = 0,
                ReceivedWeight = kv.Value,
            };
            row.PropertyChanged += OnReconcileRowPropertyChanged;
            GoldReconcileRows.Add(row);
        }
        NotifyReconcileTotalsChanged();
    }

    private void OnReconcileRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GoldReconcileRowViewModel.LossWeightText)
            or nameof(GoldReconcileRowViewModel.DebtWeight)
            or nameof(GoldReconcileRowViewModel.DebtDisplay))
        {
            DebtRecorded=false;
            NotifyReconcileTotalsChanged();
        }
    }

    private void NotifyReconcileTotalsChanged()
    {
        OnPropertyChanged(nameof(TotalDebtGold));
        OnPropertyChanged(nameof(TotalIssuedGold));
        OnPropertyChanged(nameof(TotalReceivedGold));
        OnPropertyChanged(nameof(TotalDebtGoldDisplay));
        OnPropertyChanged(nameof(HasAnyDebt));
        (RecordDebtCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
    }

    private void NotifyDetailChanged()
    {
        foreach (var name in new[] { nameof(OrderCode), nameof(ProductionType), nameof(SalesOrderCode),
                     nameof(ProductionDate), nameof(Supplier), nameof(FinishedLocation), nameof(Notes) })
            OnPropertyChanged(name);
    }

    private void NotifyTotalsChanged()
    {
        foreach (var name in new[] { nameof(FooterLineCountText), nameof(FooterTotalWeightText),
                     nameof(FooterGoldWeightText), nameof(FooterStoneWeightText),
                     nameof(FooterCraftLaborText), nameof(FooterCostPriceText),
                     nameof(FooterSalePriceText) })
            OnPropertyChanged(name);
    }

    private void RecalculateGoldWeight()
    {
        if (_goldWeightManual)
            return;
        if (TryParseDecimal(TotalWeightText, out var total)
            && TryParseDecimal(StoneWeightText, out var stone))
        {
            _goldWeightText = FormatDecimal(total - stone);
            OnPropertyChanged(nameof(GoldWeightText));
        }
        else
        {
            _goldWeightText = string.Empty;
            OnPropertyChanged(nameof(GoldWeightText));
        }
    }

    private static string CodePrefix(FinishedProductGroupOption? group)
    {
        var name = $"{group?.Name} {group?.Code}".Trim();
        if (name.Contains("Nhẫn", StringComparison.OrdinalIgnoreCase)) return "N";
        if (name.Contains("Lắc", StringComparison.OrdinalIgnoreCase)) return "L";
        if (name.Contains("Dây chuyền", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dây", StringComparison.OrdinalIgnoreCase)) return "D";
        if (name.Contains("Bông tai", StringComparison.OrdinalIgnoreCase)) return "B";
        if (name.Contains("Vòng", StringComparison.OrdinalIgnoreCase)) return "V";
        return "SP";
    }

    /// <summary>
    /// Mã TP tạm trong phiên: {PREFIX}-{số thứ tự:000}. Backend chốt mã chính thức khi ghi sổ.
    /// </summary>
    private string NextProductCode(bool peek)
    {
        var prefix = CodePrefix(SelectedProductGroup);
        var next = _codeSequences.TryGetValue(prefix, out var current) ? current + 1 : 1;
        if (!peek)
            _codeSequences[prefix] = next;
        return $"{prefix}-{next:000}";
    }

    private void InitializeCodeSequences(IEnumerable<string> existingCodes)
    {
        _codeSequences.Clear();
        foreach (var rawCode in existingCodes)
        {
            var code = (rawCode ?? string.Empty).Trim();
            var separator = code.LastIndexOf('-');
            if (separator <= 0
                || separator == code.Length - 1
                || !int.TryParse(code[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                continue;

            var prefix = code[..separator];
            if (!_codeSequences.TryGetValue(prefix, out var current) || number > current)
                _codeSequences[prefix] = number;
        }
    }

    private void AddOrUpdateProduct()
    {
        ErrorMessage = string.Empty;
        if(!CanEdit)return;
        if(SelectedLine is null){ErrorMessage="Chọn dòng mẫu của LSX.";return;}
        if (string.IsNullOrWhiteSpace(ProductName))
        {
            ErrorMessage = "Nhập tên sản phẩm.";
            return;
        }
        if (!TryParseDecimal(GoldWeightText, out var goldWeight) || goldWeight <= 0)
        {
            ErrorMessage = "Trọng lượng vàng phải lớn hơn 0.";
            return;
        }
        if(!int.TryParse(QuantityText,System.Globalization.NumberStyles.None,CultureInfo.InvariantCulture,out var quantity)||quantity<=0||quantity>10000){ErrorMessage="Số lượng phải là số nguyên từ 1 đến 10.000.";return;}

        var validTotal = TryParseDecimal(TotalWeightText, out var totalWeight);
        var validStone = TryParseDecimal(StoneWeightText, out var stoneWeight);
        if ((!string.IsNullOrWhiteSpace(TotalWeightText) && !validTotal)
            || (!string.IsNullOrWhiteSpace(StoneWeightText) && !validStone)
            || totalWeight < 0 || stoneWeight < 0)
        {
            ErrorMessage = "Trọng lượng tổng/đá không hợp lệ. Dùng dấu chấm hoặc dấu phẩy cho phần thập phân, không ngăn cách hàng nghìn.";
            return;
        }
        if(!TryParseMoney(CostPriceText,out var costPrice)||costPrice<=0){ErrorMessage="Nhập giá vốn nguyên VND lớn hơn 0.";return;}
        foreach(var money in new[]{SalePriceText,SaleLaborCostText,PurchaseLaborCostText})if(!string.IsNullOrWhiteSpace(money)&&!TryParseMoney(money,out _)){ErrorMessage="Giá và tiền công phải là số tiền nguyên VND không âm.";return;}
        TryParseMoney(SalePriceText, out var salePrice);
        TryParseMoney(SaleLaborCostText, out var saleLabor);
        TryParseMoney(PurchaseLaborCostText, out var purchaseLabor);
        var hasPurchaseLabor = !string.IsNullOrWhiteSpace(PurchaseLaborCostText);

        if(!GoldPurityChoices.Contains(GoldPurityCode)||GoldPurityCode!=SelectedLine.GoldTypeCode){ErrorMessage="Chọn loại vàng đúng mẫu của LSX.";return;}
        if(Math.Abs(totalWeight-goldWeight-stoneWeight)>.0001m||totalWeight<=0||purchaseLabor>costPrice){ErrorMessage="Kiểm tra tổng = vàng + đá; công nhập không vượt giá vốn.";return;}
        if(Products.Count(p=>p.OrderLineId==SelectedLine.Id)-(IsEditing?1:0)+quantity>SelectedLine.PlannedQuantity){ErrorMessage="Số món vượt kế hoạch của dòng mẫu.";return;}
        var items = new List<ManualFinishedProductItem>(quantity);
        // Bỏ check "Tạo mã riêng cho từng món" → cả N dòng dùng chung 1 mã.
        string? sharedCode = null; // Each piece has an independent serial.
        for (var i = 0; i < quantity; i++)
        {
            string code;
            if (IsEditing && i == 0 && Products.Count > _editingIndex && _editingIndex >= 0)
            {
                code = Products[_editingIndex].ProductCode;
            }
            else if (sharedCode is not null)
            {
                code = sharedCode;
            }
            else
            {
                code = NextProductCode(peek: false);
            }
            items.Add(new ManualFinishedProductItem(
                ProductCode: code,
                BarcodeValue: code,
                ProductName: ProductName.Trim(),
                ProductGroup: SelectedProductGroup?.Name,
                GoldPurityCode: string.IsNullOrWhiteSpace(GoldPurityCode) ? null : GoldPurityCode.Trim(),
                GoldAge: string.IsNullOrWhiteSpace(GoldPurityCode) ? null : GoldPurityCode.Trim(),
                WeightUnitCode: WeightUnitCode,
                TotalWeight: totalWeight,
                StoneWeight: stoneWeight,
                GoldWeight: goldWeight,
                CostPrice: string.IsNullOrWhiteSpace(CostPriceText) ? null : costPrice,
                SalePrice: string.IsNullOrWhiteSpace(SalePriceText) ? null : salePrice,
                CraftLaborCost: hasPurchaseLabor ? purchaseLabor : null,
                SaleLaborCost: string.IsNullOrWhiteSpace(SaleLaborCostText) ? null : saleLabor,
                PurchaseLaborCost: hasPurchaseLabor ? purchaseLabor : null,
                SupplierName: string.IsNullOrWhiteSpace(SupplierName) ? null : SupplierName.Trim(),
                ModelCode: string.IsNullOrWhiteSpace(ModelCode) ? null : ModelCode.Trim(),
                SymbolCode: string.IsNullOrWhiteSpace(SymbolCode) ? null : SymbolCode.Trim(),
                Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim(),OrderLineId:SelectedLine.Id));
        }

        if (IsEditing && _editingIndex >= 0 && _editingIndex < Products.Count)
        {
            Products.RemoveAt(_editingIndex);
            for (var i = 0; i < items.Count; i++)
                Products.Insert(_editingIndex + i, items[i]);
        }
        else
        {
            foreach (var item in items)
                Products.Add(item);
        }

        ClearForm();
    }

    private void LoadSelectedForEdit()
    {
        if (SelectedProduct is null)
            return;
        var index = Products.IndexOf(SelectedProduct);
        if (index < 0)
            return;

        var item = Products[index];
        SelectedLine=OrderLines.FirstOrDefault(l=>l.Id==item.OrderLineId);
        ProductName = item.ProductName;
        SelectedProductGroup = ProductGroups.FirstOrDefault(g => g.Name == item.ProductGroup);
        GoldPurityCode = item.GoldPurityCode ?? item.GoldAge ?? string.Empty;
        WeightUnitCode = item.WeightUnitCode;
        TotalWeightText = FormatDecimal(item.TotalWeight);
        StoneWeightText = FormatDecimal(item.StoneWeight);
        _goldWeightManual = true;
        _goldWeightText = FormatDecimal(item.GoldWeight);
        OnPropertyChanged(nameof(GoldWeightText));
        QuantityText = "1";
        CostPriceText = item.CostPrice?.ToString("N0",CultureInfo.GetCultureInfo("vi-VN")) ?? string.Empty;
        SalePriceText = item.SalePrice?.ToString("N0",CultureInfo.GetCultureInfo("vi-VN")) ?? string.Empty;
        SaleLaborCostText = item.SaleLaborCost?.ToString("N0",CultureInfo.GetCultureInfo("vi-VN")) ?? string.Empty;
        PurchaseLaborCostText = (item.PurchaseLaborCost ?? item.CraftLaborCost)?.ToString("N0",CultureInfo.GetCultureInfo("vi-VN")) ?? string.Empty;
        SupplierName = item.SupplierName ?? string.Empty;
        ModelCode = item.ModelCode ?? string.Empty;
        SelectSymbol(item.SymbolCode);
        FormNotes = item.Notes ?? string.Empty;

        _editingIndex = index;
        IsEditing = true;
        ErrorMessage = string.Empty;
    }

    private void CancelEdit()
    {
        ClearForm();
    }

    private void DeleteSelectedProduct()
    {
        if (SelectedProduct is null)
            return;
        var answer = MessageBox.Show(
            $"Xóa sản phẩm '{SelectedProduct.ProductName}' ({SelectedProduct.ProductCode}) khỏi phiếu?",
            "Xóa sản phẩm",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;
        Products.Remove(SelectedProduct);
        SelectedProduct = null;
        if (IsEditing)
            ClearForm();
    }

    private void ClearForm()
    {
        SelectedLine=null;
        ProductName = string.Empty;
        GoldPurityCode = string.Empty;
        WeightUnitCode = "Chỉ";
        TotalWeightText = string.Empty;
        StoneWeightText = string.Empty;
        _goldWeightManual = false;
        _goldWeightText = string.Empty;
        OnPropertyChanged(nameof(GoldWeightText));
        QuantityText = "1";
        CostPriceText = string.Empty;
        SalePriceText = string.Empty;
        SaleLaborCostText = string.Empty;
        PurchaseLaborCostText = string.Empty;
        SupplierName = string.Empty;
        ModelCode = string.Empty;
        SelectedSymbol = null;
        SymbolCode = string.Empty;
        FormNotes = string.Empty;
        _editingIndex = -1;
        IsEditing = false;
        OnPropertyChanged(nameof(PreviewProductCode));
    }

    private Task SaveAsync()=>PostReceiptAsync();

    private void OpenReconcileDialog()
    {
        var dialog = new GoldReconcileDialog(this);
        var owner = Application.Current.Windows
            .OfType<ProductionFinishedReceiptDialog>()
            .FirstOrDefault();
        if (owner is not null)
            dialog.Owner = owner;
        dialog.ShowDialog();
    }

    private void CreateProductGroup()
    {
        try
        {
            var viewModel = new FinishedProductGroupDialogViewModel(_warehouse, _session);
            var dialog = new FinishedProductGroupDialog(viewModel);
            var owner = Application.Current.Windows
                .OfType<ProductionFinishedReceiptDialog>()
                .FirstOrDefault();
            if (owner is not null)
                dialog.Owner = owner;

            if (dialog.ShowDialog() != true || viewModel.CreatedGroup is null)
                return;

            _ = ReloadProductGroupsAsync(viewModel.CreatedGroup.Id);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task ReloadProductGroupsAsync(long? selectId)
    {
        try
        {
            var groups = await _warehouse.GetFinishedProductGroupsAsync(_session);
            ProductGroups.Clear();
            foreach (var group in groups)
                ProductGroups.Add(group);

            SelectedProductGroup = selectId is > 0
                ? ProductGroups.FirstOrDefault(g => g.Id == selectId.Value) ?? ProductGroups.FirstOrDefault()
                : ProductGroups.FirstOrDefault();
            OnPropertyChanged(nameof(PreviewProductCode));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void CreateSymbol()
    {
        try
        {
            var viewModel = new FinishedProductSymbolDialogViewModel(_warehouse, _session);
            var dialog = new FinishedProductSymbolDialog(viewModel);
            var owner = Application.Current.Windows
                .OfType<ProductionFinishedReceiptDialog>()
                .FirstOrDefault();
            if (owner is not null)
                dialog.Owner = owner;

            if (dialog.ShowDialog() != true || viewModel.CreatedSymbol is null)
                return;

            var created = viewModel.CreatedSymbol;
            var existing = Symbols.FirstOrDefault(symbol =>
                string.Equals(symbol.Code, created.Code, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                Symbols.Add(created);
                existing = created;
            }
            SelectedSymbol = existing;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void SelectSymbol(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            SelectedSymbol = null;
            return;
        }

        var match = Symbols.FirstOrDefault(symbol =>
            string.Equals(symbol.Code, code, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            match = new FinishedProductSymbolOption(0, code.Trim());
            Symbols.Add(match);
        }
        SelectedSymbol = match;
    }

    private Task RecordDebtAsync(){PrepareSettlement();return Task.CompletedTask;}

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return WeightDecimalInput.TryParse(text, out value);
    }

    private static bool TryParseMoney(string? text,out decimal value)=>FinishedReceiptValidation.TryMoney(text,out value);

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var vi = CultureInfo.GetCultureInfo("vi-VN");
        var grouped = NumberStyles.Integer | NumberStyles.AllowThousands;
        return int.TryParse(text.Trim(), grouped, vi, out value)
            || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDecimal(decimal value) =>
        WeightDecimalInput.Format(value);
}

public sealed class ReceiptImageItem : ObservableObject
{
    private bool _isSelected;

    public ReceiptImageItem(string fileName, string imagePath)
    {
        FileName = fileName;
        ImagePath = imagePath;
    }

    public string FileName { get; }
    public string ImagePath { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

/// <summary>
/// Một dòng đối chiếu vàng theo loại vàng (PurityCode).
/// Nợ = Đã giao − Đã nhận − Hao hụt (hao hụt nhập tay từng dòng).
/// </summary>
public sealed class GoldReconcileRowViewModel : ObservableObject
{
    private string _lossWeightText = string.Empty;

    public GoldReconcileRowViewModel(string? purityCode)
    {
        PurityCode = purityCode;
    }

    /// <summary>Mã loại vàng (có thể null → hiển thị "Không rõ").</summary>
    public string? PurityCode { get; }

    public string PurityDisplay => string.IsNullOrWhiteSpace(PurityCode) ? "Không rõ" : PurityCode;

    private decimal _issuedWeight,_receivedWeight;
    public decimal IssuedWeight { get=>_issuedWeight; set{if(SetProperty(ref _issuedWeight,value))NotifyWeights();} }
    public decimal ReceivedWeight { get=>_receivedWeight; set{if(SetProperty(ref _receivedWeight,value))NotifyWeights();} }
    private void NotifyWeights(){OnPropertyChanged(nameof(DebtWeight));OnPropertyChanged(nameof(DebtDisplay));OnPropertyChanged(nameof(HasDebt));}

    /// <summary>Hao hụt (chỉ): nhân viên tự gõ từng dòng, không ghi công nợ.</summary>
    public string LossWeightText
    {
        get => _lossWeightText;
        set
        {
            if (SetProperty(ref _lossWeightText, value))
            {
                OnPropertyChanged(nameof(LossWeight));
                OnPropertyChanged(nameof(DebtWeight));
                OnPropertyChanged(nameof(DebtDisplay));
            }
        }
    }

    public decimal LossWeight => TryParseDecimal(LossWeightText, out var v) && v > 0 ? v : 0;

    /// <summary>Nợ vàng = Vàng giao − Vàng nhận − Hao hụt.</summary>
    public decimal DebtWeight => IssuedWeight - ReceivedWeight - LossWeight;

    public string DebtDisplay => $"{DebtWeight:0.###} chỉ";

    public bool HasDebt => DebtWeight > 0;

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return WeightDecimalInput.TryParse(text, out value);
    }
}
