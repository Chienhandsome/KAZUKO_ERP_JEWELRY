using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Nhập kho thành phẩm luồng mới (14/10/2026): nhập tay từng sản phẩm.
/// - Form nhập tay: bấm Thêm để đưa vào bảng. SL=N → N dòng, mỗi dòng 1 mã TP riêng.
/// - Mã TP tự sinh trong phiên: prefix = chữ cái đầu nhóm hàng (N/L/D/B/V) + số thứ tự.
/// - TL vàng tự tính = TL tổng − TL đá, cho phép sửa tay.
/// - Lưu: ghi sổ qua <c>PostManualFinishedReceiptAsync</c> (không qua QC).
/// </summary>
public sealed class ProductionFinishedReceiptDialogViewModel : ObservableObject
{
    private static readonly string[] GoldPurityOptions =
        ["9999", "999", "750", "680", "610", "585", "18K", "24K"];

    private static readonly string[] WeightUnitOptions =
        ["Chỉ", "Phân", "Ly", "Gram"];

    private static readonly string[] GoldAgeOptions =
        ["9999", "999", "750", "680", "610", "585", "416", "375"];

    private static readonly string[] SupplierOptions =
        ["Nội bộ", "Xưởng chế tác"];

    private readonly IWarehouseRepository _warehouse;
    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly AuthenticatedSession _session;
    private readonly Dictionary<string, int> _codeSequences = new(StringComparer.OrdinalIgnoreCase);

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
    private string _goldAge = string.Empty;
    private string _weightUnitCode = "Chỉ";
    private string _totalWeightText = string.Empty;
    private string _stoneWeightText = string.Empty;
    private string _goldWeightText = string.Empty;
    private string _quantityText = "1";
    private string _costPriceText = string.Empty;
    private string _salePriceText = string.Empty;
    private string _craftLaborCostText = string.Empty;
    private string _saleLaborCostText = string.Empty;
    private string _purchaseLaborCostText = string.Empty;
    private string _supplierName = string.Empty;
    private string _modelCode = string.Empty;
    private string _symbolCode = string.Empty;
    private string _notes = string.Empty;
    private bool _goldWeightManual;
    private bool _isEditing;
    private int _editingIndex = -1;
    private ManualFinishedProductItem? _selectedProduct;
    private bool _generateSeparateCodes = true;
    private bool _loadFromProductionOrder;

    // Đối chiếu vàng
    private ProductionOrderWorkerDto? _selectedWorker;
    private decimal _issuedGold;
    private string _lossWeightText = string.Empty;
    private string _reconcileMessage = string.Empty;
    private bool _debtRecorded;

    public ProductionFinishedReceiptDialogViewModel(
        IWarehouseRepository warehouse,
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        AuthenticatedSession session)
    {
        _warehouse = warehouse;
        _queries = queries;
        _application = application;
        _session = session;

        AddCommand = new RelayCommand(AddOrUpdateProduct);
        EditCommand = new RelayCommand(LoadSelectedForEdit, () => SelectedProduct is not null && !IsBusy);
        DeleteCommand = new RelayCommand(DeleteSelectedProduct, () => SelectedProduct is not null && !IsBusy);
        CancelEditCommand = new RelayCommand(CancelEdit, () => IsEditing);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        SelectImageCommand = new RelayCommand<ReceiptImageItem>(item =>
        {
            if (item is not null)
                SelectedImage = item;
        });
        RecordDebtCommand = new AsyncRelayCommand(RecordDebtAsync, () => !IsBusy && !_debtRecorded);
        PrintLabelCommand = new RelayCommand(() => MessageBox.Show(
            "Chức năng in tem đang phát triển.", "In lại tem",
            MessageBoxButton.OK, MessageBoxImage.Information));
        PrintReceiptCommand = new RelayCommand(() => MessageBox.Show(
            "Chức năng in phiếu đang phát triển.", "In phiếu",
            MessageBoxButton.OK, MessageBoxImage.Information));

        Products.CollectionChanged += OnProductsChanged;
        _ = LoadLookupsAsync();
    }

    public ObservableCollection<ProductionFinishedReceiptOrderOption> Orders { get; } = [];
    public ObservableCollection<ManualFinishedProductItem> Products { get; } = [];
    public ObservableCollection<ReceiptImageItem> ImageItems { get; } = [];
    public ObservableCollection<WarehouseLocationOption> Locations { get; } = [];
    public ObservableCollection<FinishedProductGroupOption> ProductGroups { get; } = [];
    public ObservableCollection<ProductionOrderWorkerDto> WorkerOptions { get; } = [];

    public IReadOnlyList<string> GoldPurityChoices => GoldPurityOptions;
    public IReadOnlyList<string> WeightUnitChoices => WeightUnitOptions;
    public IReadOnlyList<string> GoldAgeChoices => GoldAgeOptions;
    public IReadOnlyList<string> SupplierChoices => SupplierOptions;

    public ProductionFinishedReceiptOrderOption? SelectedOrder
    {
        get => _selectedOrder;
        set
        {
            if (SetProperty(ref _selectedOrder, value))
                _ = LoadDetailAsync(value);
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

    public string GoldPurityCode
    {
        get => _goldPurityCode;
        set => SetProperty(ref _goldPurityCode, value);
    }

    public string GoldAge
    {
        get => _goldAge;
        set => SetProperty(ref _goldAge, value);
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

    public string CraftLaborCostText
    {
        get => _craftLaborCostText;
        set => SetProperty(ref _craftLaborCostText, value);
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

    /// <summary>Lấy dữ liệu từ lệnh sản xuất: tự điền tên SP / loại vàng / mã hiệu từ dòng đầu của lệnh.</summary>
    public bool LoadFromProductionOrder
    {
        get => _loadFromProductionOrder;
        set
        {
            if (SetProperty(ref _loadFromProductionOrder, value) && value)
                PrefillFromOrder();
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
                (EditCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.NotifyCanExecuteChanged();
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
                (EditCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (DeleteCommand as RelayCommand)?.NotifyCanExecuteChanged();
                (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (RecordDebtCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand PrintLabelCommand { get; }
    public ICommand PrintReceiptCommand { get; }

    #endregion

    #region Footer totals

    public string FooterLineCountText => $"{Products.Count} món";
    public string FooterTotalWeightText => $"{Products.Sum(x => x.TotalWeight):0.###} Chỉ";
    public string FooterGoldWeightText => $"{Products.Sum(x => x.GoldWeight):0.###} Chỉ";
    public string FooterStoneWeightText => $"{Products.Sum(x => x.StoneWeight):0.###} Chỉ";
    public string FooterCraftLaborText => $"{Products.Sum(x => x.CraftLaborCost ?? 0):N0}";
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
        set => SetProperty(ref _selectedWorker, value);
    }

    /// <summary>Tổng vàng đã giao cho thợ (chỉ), từ bản ghi giao NVL của lệnh.</summary>
    public decimal IssuedGold
    {
        get => _issuedGold;
        private set
        {
            if (SetProperty(ref _issuedGold, value))
                RefreshDebt();
        }
    }

    /// <summary>Tổng TL vàng đã nhận = tổng TL vàng các SP trong bảng.</summary>
    public decimal ReceivedGold => Products.Sum(p => p.GoldWeight);

    /// <summary>Hao hụt (chỉ): nhân viên tự gõ, không ghi công nợ.</summary>
    public string LossWeightText
    {
        get => _lossWeightText;
        set
        {
            if (SetProperty(ref _lossWeightText, value))
                RefreshDebt();
        }
    }

    public decimal LossGold => TryParseDecimal(LossWeightText, out var v) && v > 0 ? v : 0;

    /// <summary>Nợ vàng = Vàng giao − Vàng nhận − Hao hụt.</summary>
    public decimal DebtGold => IssuedGold - ReceivedGold - LossGold;

    public bool DebtIsPositive => DebtGold > 0;

    public bool DebtRecorded
    {
        get => _debtRecorded;
        private set
        {
            if (SetProperty(ref _debtRecorded, value))
                (RecordDebtCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    public string ReconcileMessage
    {
        get => _reconcileMessage;
        private set => SetProperty(ref _reconcileMessage, value);
    }

    public string DebtGoldDisplay => $"{DebtGold:0.###} chỉ";

    public ICommand RecordDebtCommand { get; }

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
            var orders = await _queries.GetCompletedUnreceivedOrdersAsync(_session.BranchId);
            Orders.Clear();
            foreach (var item in orders)
                Orders.Add(item);
            SelectedOrder = Orders.FirstOrDefault();

            var locations = await _warehouse.GetLocationsAsync(_session);
            Locations.Clear();
            foreach (var location in locations)
                Locations.Add(location);
            SelectedLocation = Locations.FirstOrDefault();

            var groups = await _warehouse.GetFinishedProductGroupsAsync(_session);
            ProductGroups.Clear();
            foreach (var group in groups)
                ProductGroups.Add(group);
            SelectedProductGroup = ProductGroups.FirstOrDefault();
            OnPropertyChanged(nameof(PreviewProductCode));

            if (Orders.Count == 0)
                ErrorMessage = "Không có lệnh đã hoàn thành đang chờ nhập kho.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private async Task LoadDetailAsync(ProductionFinishedReceiptOrderOption? item)
    {
        var requestedId = item?.Id;
        try
        {
            ErrorMessage = string.Empty;
            _detail = null;
            ImageItems.Clear();
            SelectedImage = null;
            WorkerOptions.Clear();
            SelectedWorker = null;
            IssuedGold = 0;
            DebtRecorded = false;
            ReconcileMessage = string.Empty;
            NotifyDetailChanged();
            OnPropertyChanged(nameof(HasImages));
            if (item is not null)
            {
                var detail = await _queries.GetOrderDetailAsync(item.Id, _session.BranchId);
                if (SelectedOrder?.Id != requestedId)
                    return;
                _detail = detail;
            }
            if (_detail is not null)
            {
                foreach (var attachment in _detail.Attachments.Where(IsImageAttachment))
                    ImageItems.Add(new ReceiptImageItem(attachment.OriginalFileName, attachment.StoredPath));
                SelectedImage = ImageItems.FirstOrDefault();

                // Đối chiếu vàng: thợ + số vàng đã giao từ bản ghi giao NVL.
                foreach (var worker in _detail.Workers)
                    WorkerOptions.Add(worker);
                SelectedWorker = WorkerOptions.FirstOrDefault();
                try
                {
                    IssuedGold = await _queries.GetIssuedGoldWeightAsync(item!.Id, _session.BranchId);
                }
                catch (Exception ex)
                {
                    ReconcileMessage = $"Không tải được số vàng đã giao: {ex.Message}";
                }

                if (LoadFromProductionOrder)
                    PrefillFromOrder();
            }
            NotifyDetailChanged();
            OnPropertyChanged(nameof(HasImages));
            RefreshDebt();
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
        NotifyTotalsChanged();
        RefreshDebt();
    }

    private void RefreshDebt()
    {
        OnPropertyChanged(nameof(ReceivedGold));
        OnPropertyChanged(nameof(LossGold));
        OnPropertyChanged(nameof(DebtGold));
        OnPropertyChanged(nameof(DebtIsPositive));
        OnPropertyChanged(nameof(DebtGoldDisplay));
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

    /// <summary>
    /// Điền form từ dòng đầu của lệnh sản xuất (khi bật "Lấy dữ liệu từ lệnh sản xuất").
    /// Chỉ điền các ô còn trống, không ghi đè dữ liệu người dùng đã nhập.
    /// </summary>
    private void PrefillFromOrder()
    {
        var line = _detail?.Lines.FirstOrDefault();
        if (line is null)
            return;
        if (string.IsNullOrWhiteSpace(ProductName))
            ProductName = line.ModelName;
        if (string.IsNullOrWhiteSpace(GoldPurityCode))
            GoldPurityCode = line.GoldTypeCode;
        if (string.IsNullOrWhiteSpace(ModelCode) && !string.IsNullOrWhiteSpace(line.ModelCode))
            ModelCode = line.ModelCode;
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

    private void AddOrUpdateProduct()
    {
        ErrorMessage = string.Empty;
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
        var quantity = TryParseInt(QuantityText, out var q) && q > 0 ? q : 1;

        TryParseDecimal(TotalWeightText, out var totalWeight);
        TryParseDecimal(StoneWeightText, out var stoneWeight);
        TryParseDecimal(CostPriceText, out var costPrice);
        TryParseDecimal(SalePriceText, out var salePrice);
        TryParseDecimal(CraftLaborCostText, out var craftLabor);
        TryParseDecimal(SaleLaborCostText, out var saleLabor);
        TryParseDecimal(PurchaseLaborCostText, out var purchaseLabor);

        var items = new List<ManualFinishedProductItem>(quantity);
        // Bỏ check "Tạo mã riêng cho từng món" → cả N dòng dùng chung 1 mã.
        var sharedCode = GenerateSeparateCodes ? null : NextProductCode(peek: false);
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
                GoldAge: string.IsNullOrWhiteSpace(GoldAge) ? null : GoldAge.Trim(),
                WeightUnitCode: WeightUnitCode,
                TotalWeight: totalWeight,
                StoneWeight: stoneWeight,
                GoldWeight: goldWeight,
                CostPrice: string.IsNullOrWhiteSpace(CostPriceText) ? null : costPrice,
                SalePrice: string.IsNullOrWhiteSpace(SalePriceText) ? null : salePrice,
                CraftLaborCost: string.IsNullOrWhiteSpace(CraftLaborCostText) ? null : craftLabor,
                SaleLaborCost: string.IsNullOrWhiteSpace(SaleLaborCostText) ? null : saleLabor,
                PurchaseLaborCost: string.IsNullOrWhiteSpace(PurchaseLaborCostText) ? null : purchaseLabor,
                SupplierName: string.IsNullOrWhiteSpace(SupplierName) ? null : SupplierName.Trim(),
                ModelCode: string.IsNullOrWhiteSpace(ModelCode) ? null : ModelCode.Trim(),
                SymbolCode: string.IsNullOrWhiteSpace(SymbolCode) ? null : SymbolCode.Trim(),
                Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim()));
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
        ProductName = item.ProductName;
        SelectedProductGroup = ProductGroups.FirstOrDefault(g => g.Name == item.ProductGroup);
        GoldPurityCode = item.GoldPurityCode ?? string.Empty;
        GoldAge = item.GoldAge ?? string.Empty;
        WeightUnitCode = item.WeightUnitCode;
        TotalWeightText = FormatDecimal(item.TotalWeight);
        StoneWeightText = FormatDecimal(item.StoneWeight);
        _goldWeightManual = true;
        _goldWeightText = FormatDecimal(item.GoldWeight);
        OnPropertyChanged(nameof(GoldWeightText));
        QuantityText = "1";
        CostPriceText = item.CostPrice?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SalePriceText = item.SalePrice?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        CraftLaborCostText = item.CraftLaborCost?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SaleLaborCostText = item.SaleLaborCost?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        PurchaseLaborCostText = item.PurchaseLaborCost?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        SupplierName = item.SupplierName ?? string.Empty;
        ModelCode = item.ModelCode ?? string.Empty;
        SymbolCode = item.SymbolCode ?? string.Empty;
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
        ProductName = string.Empty;
        GoldPurityCode = string.Empty;
        GoldAge = string.Empty;
        WeightUnitCode = "Chỉ";
        TotalWeightText = string.Empty;
        StoneWeightText = string.Empty;
        _goldWeightManual = false;
        _goldWeightText = string.Empty;
        OnPropertyChanged(nameof(GoldWeightText));
        QuantityText = "1";
        CostPriceText = string.Empty;
        SalePriceText = string.Empty;
        CraftLaborCostText = string.Empty;
        SaleLaborCostText = string.Empty;
        PurchaseLaborCostText = string.Empty;
        SupplierName = string.Empty;
        ModelCode = string.Empty;
        SymbolCode = string.Empty;
        FormNotes = string.Empty;
        _editingIndex = -1;
        IsEditing = false;
        OnPropertyChanged(nameof(PreviewProductCode));
    }

    private async Task SaveAsync()
    {
        ErrorMessage = string.Empty;
        if (Products.Count == 0)
        {
            ErrorMessage = "Chưa có sản phẩm nào để nhập kho.";
            return;
        }
        if (SelectedLocation is null)
        {
            ErrorMessage = "Chọn kho nhập.";
            return;
        }
        IsBusy = true;
        try
        {
            var request = new PostManualFinishedReceiptRequest(
                ReceiptDate: ReceiptDate.Date,
                LocationId: SelectedLocation.Id,
                ProductionOrderId: SelectedOrder?.Id,
                Items: Products.ToList(),
                Notes: string.IsNullOrWhiteSpace(FormNotes) ? null : FormNotes.Trim());
            var result = await _application.PostManualFinishedReceiptAsync(request, _session);
            MessageBox.Show(
                $"Đã lưu {result.ProductCount} sản phẩm. Số phiếu: {result.DocumentCode}.",
                "Nhập kho thành phẩm",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
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

    private async Task RecordDebtAsync()
    {
        ReconcileMessage = string.Empty;
        if (SelectedOrder is null)
        {
            ReconcileMessage = "Chọn lệnh sản xuất để đối chiếu vàng.";
            return;
        }
        if (SelectedWorker is null)
        {
            ReconcileMessage = "Chọn thợ.";
            return;
        }
        if (DebtGold <= 0)
        {
            ReconcileMessage = "Nợ vàng phải lớn hơn 0 mới ghi công nợ.";
            return;
        }
        IsBusy = true;
        try
        {
            var request = new RecordWorkerGoldDebtRequest(
                WorkerId: SelectedWorker.EmployeeId,
                WorkerName: SelectedWorker.FullName,
                GoldPurityCode: Products.FirstOrDefault()?.GoldPurityCode,
                WeightChi: DebtGold,
                WeightIssuedChi: IssuedGold,
                WeightReceivedChi: ReceivedGold,
                WeightLossChi: LossGold,
                ProductionOrderId: SelectedOrder.Id,
                ProductionOrderCode: SelectedOrder.Code,
                Notes: null);
            await _application.RecordWorkerGoldDebtAsync(request, _session);
            DebtRecorded = true;
            ReconcileMessage = "Đã ghi công nợ vàng thành công.";
        }
        catch (Exception ex)
        {
            ReconcileMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var styles = NumberStyles.Number;
        return decimal.TryParse(text.Trim(), styles, CultureInfo.CurrentCulture, out value)
            || decimal.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseInt(string? text, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value)
            || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string FormatDecimal(decimal value) =>
        value.ToString("0.###", CultureInfo.CurrentCulture);
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
