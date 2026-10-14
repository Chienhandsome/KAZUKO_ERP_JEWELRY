using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Warehouse;

/// <summary>
/// Màn nhập kho thành phẩm (M3, redesign 12/10/2026).
/// Mỗi dòng = 1 món thành phẩm (1 mã = 1 món): không cột SL, không cột trạng thái.
/// "Công chế tác" và "Giá bán" là textfield nhập tay; "Giá vốn" tự tính bằng
/// <see cref="ProductionCostCalculator"/> từ cấu thành của từng món QC.
/// Luồng post giữ nguyên bulk-post của backend (<c>PostFinishedReceiptAsync</c>).
/// </summary>
public sealed class ProductionFinishedReceiptDialogViewModel : ObservableObject
{
    private readonly IProductionQueryRepository _queries;
    private readonly IProductionApplicationService _application;
    private readonly AuthenticatedSession _session;
    private ProductionFinishedReceiptOrderOption? _selectedOrder;
    private ProductionOrderDetail? _detail;
    private ReceiptImageItem? _selectedImage;
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;
    private DateTime _receiptDate = DateTime.Today;

    public ProductionFinishedReceiptDialogViewModel(
        IProductionQueryRepository queries,
        IProductionApplicationService application,
        AuthenticatedSession session)
    {
        _queries = queries;
        _application = application;
        _session = session;
        SaveCommand = new AsyncRelayCommand(SaveAsync);
        CancelCommand = new RelayCommand(() => DialogResult = false);
        SelectImageCommand = new RelayCommand<ReceiptImageItem>(item =>
        {
            if (item is not null)
                SelectedImage = item;
        });
        Products.CollectionChanged += OnProductsChanged;
        _ = LoadOrdersAsync();
    }

    public ObservableCollection<ProductionFinishedReceiptOrderOption> Orders { get; } = [];
    public ObservableCollection<FinishedReceiptLineViewModel> Products { get; } = [];
    public ObservableCollection<ReceiptImageItem> ImageItems { get; } = [];

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
    /// Ngày nhập (hiển thị cho phiếu). Lưu ý: backend hiện post dùng CURRENT_TIMESTAMP.
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

    public string FooterLineCountText => $"{Products.Count} món";
    public string FooterGoldWeightText => $"{Products.Sum(x => x.ActualGoldWeight ?? 0):0.###} chỉ";
    public string FooterCraftLaborText => $"{Products.Sum(x => x.CraftLaborCost):N0}";
    public string FooterCostPriceText => Products.Count == 0 || Products.Any(x => x.CostPrice is null)
        ? "—" : $"{Products.Sum(x => x.CostPrice!.Value):N0}";
    public string FooterSalePriceText => $"{Products.Sum(x => x.SalePrice):N0}";

    public string ErrorMessage { get => _errorMessage; private set => SetProperty(ref _errorMessage, value); }
    public bool? DialogResult { get => _dialogResult; private set => SetProperty(ref _dialogResult, value); }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand SelectImageCommand { get; }

    private async Task LoadOrdersAsync()
    {
        try
        {
            var items = await _queries.GetCompletedUnreceivedOrdersAsync(_session.BranchId);
            Orders.Clear();
            foreach (var item in items)
                Orders.Add(item);
            SelectedOrder = Orders.FirstOrDefault();
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
            foreach (var line in Products)
                line.PropertyChanged -= OnLinePropertyChanged;
            Products.Clear();
            ImageItems.Clear();
            SelectedImage = null;
            NotifyDetailChanged();
            NotifyTotalsChanged();
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
                foreach (var unit in _detail.QcUnits)
                    Products.Add(new FinishedReceiptLineViewModel(unit));
                foreach (var line in Products)
                {
                    if (SelectedOrder?.Id != requestedId)
                        return;
                    try
                    {
                        var components = await _queries.GetQcUnitComponentsAsync(line.UnitId);
                        if (SelectedOrder?.Id != requestedId)
                            return;
                        line.SetComponents(components);
                    }
                    catch
                    {
                        // Không tính được giá vốn cho món này -> hiển thị "—", không chặn dialog.
                    }
                }
                foreach (var attachment in _detail.Attachments.Where(IsImageAttachment))
                    ImageItems.Add(new ReceiptImageItem(attachment.OriginalFileName, attachment.StoredPath));
                SelectedImage = ImageItems.FirstOrDefault();
            }
            NotifyDetailChanged();
            NotifyTotalsChanged();
            OnPropertyChanged(nameof(HasImages));
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
        if (e.OldItems is not null)
            foreach (FinishedReceiptLineViewModel line in e.OldItems)
                line.PropertyChanged -= OnLinePropertyChanged;
        if (e.NewItems is not null)
            foreach (FinishedReceiptLineViewModel line in e.NewItems)
                line.PropertyChanged += OnLinePropertyChanged;
        NotifyTotalsChanged();
    }

    private void OnLinePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FinishedReceiptLineViewModel.CraftLaborCost)
            or nameof(FinishedReceiptLineViewModel.SalePrice)
            or nameof(FinishedReceiptLineViewModel.CostPrice)
            or nameof(FinishedReceiptLineViewModel.ActualGoldWeight))
            NotifyTotalsChanged();
    }

    private void NotifyDetailChanged()
    {
        foreach (var name in new[] { nameof(OrderCode), nameof(ProductionType), nameof(SalesOrderCode),
                     nameof(ProductionDate), nameof(Supplier), nameof(FinishedLocation), nameof(Notes) })
            OnPropertyChanged(name);
    }

    private void NotifyTotalsChanged()
    {
        foreach (var name in new[] { nameof(FooterLineCountText), nameof(FooterGoldWeightText),
                     nameof(FooterCraftLaborText), nameof(FooterCostPriceText), nameof(FooterSalePriceText) })
            OnPropertyChanged(name);
    }

    private async Task SaveAsync()
    {
        if (_detail is null || _detail.FinishedReceiptDocumentId is not null)
        {
            ErrorMessage = "Chọn lệnh sản xuất đã hoàn thành và chưa nhập kho.";
            return;
        }
        try
        {
            ErrorMessage = string.Empty;
            await _application.PostFinishedReceiptAsync(_detail.Id, _detail.RowVersion, _session);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}

/// <summary>
/// 1 dòng = 1 món thành phẩm (1 mã = 1 món, không cột SL).
/// Công chế tác / Công bán / Công nhập / Giá bán nhập tay; Giá vốn tự tính.
/// </summary>
public sealed class FinishedReceiptLineViewModel : ObservableObject
{
    private readonly ProductionQcUnitDto _unit;
    private IReadOnlyList<ProductionUnitComponentDto> _components = Array.Empty<ProductionUnitComponentDto>();
    private decimal _craftLaborCost;
    private decimal _saleLaborCost;
    private decimal _purchaseLaborCost;
    private decimal _salePrice;
    private decimal? _costPrice;

    public FinishedReceiptLineViewModel(ProductionQcUnitDto unit)
    {
        _unit = unit;
        _craftLaborCost = unit.LaborCostSnapshot;
    }

    public long UnitId => _unit.Id;
    public int UnitNumber => _unit.UnitNumber;
    public string ModelName => _unit.ModelName;
    public string GoldPurityCode => string.IsNullOrWhiteSpace(_unit.GoldPurityCode) ? "—" : _unit.GoldPurityCode;
    /// <summary>
    /// QC snapshot chỉ lưu GoldPurityCode (chưa có tuổi vàng riêng);
    /// hiển thị cùng giá trị cho cột Tuổi vàng theo quy ước của phiếu QC in.
    /// </summary>
    public string GoldAgeDisplay => GoldPurityCode;
    public string WeightUnitCode => string.IsNullOrWhiteSpace(_unit.ActualWeightUnitCode) ? "chỉ" : _unit.ActualWeightUnitCode;
    public decimal? ActualGoldWeight => _unit.ActualGoldWeight;
    /// <summary>DB chưa có cột TL đá (backend gán TOTAL_WEIGHT = GOLD_WEIGHT).</summary>
    public string StoneWeightDisplay => "—";
    /// <summary>Mã TP-{năm}-{6 số} do backend tự sinh lúc post.</summary>
    public string ProductCodeDisplay => "—";
    public string BarcodeDisplay => "—";
    /// <summary>QC snapshot chưa có dữ liệu nhóm hàng.</summary>
    public string ProductGroupDisplay => "—";

    /// <summary>C8: textfield người dùng tự nhập (khởi tạo từ snapshot QC).</summary>
    public decimal CraftLaborCost
    {
        get => _craftLaborCost;
        set
        {
            if (SetProperty(ref _craftLaborCost, value))
                RecalculateCost();
        }
    }

    /// <summary>Công bán: nhập tay.</summary>
    public decimal SaleLaborCost
    {
        get => _saleLaborCost;
        set => SetProperty(ref _saleLaborCost, value);
    }

    /// <summary>Công nhập: nhập tay.</summary>
    public decimal PurchaseLaborCost
    {
        get => _purchaseLaborCost;
        set => SetProperty(ref _purchaseLaborCost, value);
    }

    /// <summary>C9: Giá bán nhập tay.</summary>
    public decimal SalePrice
    {
        get => _salePrice;
        set => SetProperty(ref _salePrice, value);
    }

    /// <summary>C9: Giá vốn tự tính bằng ProductionCostCalculator.</summary>
    public decimal? CostPrice
    {
        get => _costPrice;
        private set
        {
            if (SetProperty(ref _costPrice, value))
                OnPropertyChanged(nameof(CostPriceDisplay));
        }
    }

    public string CostPriceDisplay => CostPrice?.ToString("N0") ?? "—";

    public void SetComponents(IReadOnlyList<ProductionUnitComponentDto> components)
    {
        _components = components ?? Array.Empty<ProductionUnitComponentDto>();
        RecalculateCost();
    }

    private void RecalculateCost()
    {
        try
        {
            var result = ProductionCostCalculator.Calculate(
                _components.Select(c => new ProductionCostCalculator.ComponentCostInput(
                    c.ComponentKind, c.QuantityUsed, c.UnitCostSnapshot, c.GoldPricePerChiSnapshot)),
                _craftLaborCost,
                _unit.ActualGoldWeight);
            CostPrice = result.TotalCost;
        }
        catch
        {
            CostPrice = null;
        }
    }
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
