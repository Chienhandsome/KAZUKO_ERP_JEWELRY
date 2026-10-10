using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Inventory;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Production;
using Microsoft.Win32;

namespace Kazuko.Modules.Production.Dialogs;

public sealed class ProductionLineEditDialogViewModel : ObservableObject
{
    private readonly IProductionAttachmentStore _attachments;
    private readonly bool _isEditable;
    private readonly List<string> _sessionStagingPaths = [];
    private readonly List<ProductionAttachmentEditorViewModel> _removedAttachments = [];

    private string? _modelCode;
    private string _modelName = string.Empty;
    private string _goldTypeCode = string.Empty;
    private string _goldTypeName = string.Empty;
    private ProductionMaterialStockOption? _selectedGoldStock;
    private string _plannedQuantityText = "1";
    private string _laborCostPerUnitText = "0";
    private string _estimatedWeightChiText = "0";
    private string _goldUnitPricePerChiText = "0";
    private string _stoneAmountPerUnitVndText = "0";
    private string? _description;
    private string? _notes;
    private string _errorMessage = string.Empty;
    private readonly IReadOnlyDictionary<long, decimal> _sellPriceByMaterialId;

    public ProductionLineEditDialogViewModel(
        IProductionAttachmentStore attachments,
        int lineNumber,
        bool isEditable,
        IReadOnlyList<ProductionMaterialStockOption> goldStockOptions,
        ProductionLineEditorViewModel? source = null,
        IReadOnlyDictionary<long, decimal>? sellPriceByMaterialId = null)
    {
        _attachments = attachments;
        _isEditable = isEditable;
        LineNumber = lineNumber;
        _sellPriceByMaterialId = sellPriceByMaterialId ?? new Dictionary<long, decimal>();

        SampleImages = [];
        DesignFiles = [];
        GoldStockOptions = [];
        SampleImages.CollectionChanged += OnAttachmentCollectionChanged;
        DesignFiles.CollectionChanged += OnAttachmentCollectionChanged;

        foreach (var item in goldStockOptions
                     .Where(x => string.Equals(x.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            GoldStockOptions.Add(item);
        }

        if (source is not null)
        {
            ModelCode = source.ModelCode;
            ModelName = source.ModelName;
            GoldTypeCode = source.GoldTypeCode;
            GoldTypeName = source.GoldTypeName;
            PlannedQuantityText = source.PlannedQuantity.ToString(CultureInfo.CurrentCulture);
            LaborCostPerUnitText = FormatLaborCost(source.LaborCostPerUnit);
            EstimatedWeightChiText = FormatDecimal(source.EstimatedWeightChi, "0.###");
            GoldUnitPricePerChiText = FormatLaborCost(source.GoldUnitPricePerChi);
            StoneAmountPerUnitVndText = FormatLaborCost(source.StoneAmountPerUnitVnd);
            Description = source.Description;
            Notes = source.Notes;

            foreach (var attachment in source.Attachments)
            {
                var clone = attachment.CloneForEdit();
                if (clone.IsSampleImage)
                    SampleImages.Add(clone);
                else
                    DesignFiles.Add(clone);
            }
        }

        SelectGoldStockFromCurrentValues();
        if (source is null || source.GoldUnitPricePerChi <= 0)
            ApplyDefaultGoldPriceFromSelection();

        SaveCommand = new RelayCommand(Save, () => IsEditable);
        CancelCommand = new RelayCommand(Cancel);
        AddSampleImageCommand = new AsyncRelayCommand(
            () => AddAttachmentAsync(ProductionAttachmentKinds.CustomerRequirement),
            () => IsEditable);
        AddDesignFileCommand = new AsyncRelayCommand(
            () => AddAttachmentAsync(ProductionAttachmentKinds.Design),
            () => IsEditable);
        RemoveAttachmentCommand = new ParameterRelayCommand(
            p => RemoveAttachment(p as ProductionAttachmentEditorViewModel),
            p => IsEditable && p is ProductionAttachmentEditorViewModel);
        OpenAttachmentCommand = new ParameterRelayCommand(
            p => OpenAttachment(p as ProductionAttachmentEditorViewModel),
            p => p is ProductionAttachmentEditorViewModel);
    }

    public event Action<bool>? RequestClose;

    public int LineNumber { get; }
    public bool IsEditable => _isEditable;
    public string WindowTitle => IsEditable
        ? (string.IsNullOrWhiteSpace(ModelName) ? $"Sản phẩm #{LineNumber}" : $"Sản phẩm #{LineNumber}: {ModelName}")
        : $"Xem sản phẩm #{LineNumber}";

    public ObservableCollection<ProductionAttachmentEditorViewModel> SampleImages { get; }
    public ObservableCollection<ProductionAttachmentEditorViewModel> DesignFiles { get; }
    public ObservableCollection<ProductionMaterialStockOption> GoldStockOptions { get; }

    public bool HasSampleImages => SampleImages.Count > 0;
    public bool HasDesignFiles => DesignFiles.Count > 0;
    public bool HasGoldStockOptions => GoldStockOptions.Count > 0;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public IReadOnlyList<ProductionAttachmentEditorViewModel> RemovedAttachments => _removedAttachments;

    public string? ModelCode
    {
        get => _modelCode;
        set => SetProperty(ref _modelCode, value);
    }

    public string ModelName
    {
        get => _modelName;
        set
        {
            if (SetProperty(ref _modelName, value ?? string.Empty))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string GoldTypeCode
    {
        get => _goldTypeCode;
        private set => SetProperty(ref _goldTypeCode, value ?? string.Empty);
    }

    public string GoldTypeName
    {
        get => _goldTypeName;
        private set => SetProperty(ref _goldTypeName, value ?? string.Empty);
    }

    public ProductionMaterialStockOption? SelectedGoldStock
    {
        get => _selectedGoldStock;
        set
        {
            if (!SetProperty(ref _selectedGoldStock, value))
                return;

            if (value is null)
            {
                GoldTypeCode = string.Empty;
                GoldTypeName = string.Empty;
            }
            else
            {
                GoldTypeCode = value.Code;
                GoldTypeName = value.Name;
                ApplyDefaultGoldPriceFromSelection();
            }

            OnPropertyChanged(nameof(SelectedGoldStockHint));
            OnPropertyChanged(nameof(QuotePreviewText));
        }
    }

    public string SelectedGoldStockHint
    {
        get
        {
            if (SelectedGoldStock is null)
            {
                return HasGoldStockOptions
                    ? "Chọn loại vàng còn tồn tại kho xuất."
                    : "Không có vàng còn tồn tại kho xuất. Kiểm tra kho NVL / vị trí xuất.";
            }

            var purity = string.IsNullOrWhiteSpace(SelectedGoldStock.PurityCode)
                ? "—"
                : SelectedGoldStock.PurityCode;
            return $"Mã {SelectedGoldStock.Code} · Độ tuổi {purity} · Tồn {SelectedGoldStock.QuantityOnHand:0.###} {SelectedGoldStock.UnitCode}";
        }
    }

    public string PlannedQuantityText
    {
        get => _plannedQuantityText;
        set
        {
            if (SetProperty(ref _plannedQuantityText, value ?? string.Empty))
                OnPropertyChanged(nameof(QuotePreviewText));
        }
    }

    public string LaborCostPerUnitText
    {
        get => _laborCostPerUnitText;
        set
        {
            var raw = value ?? string.Empty;
            if (decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var labor)
                && labor >= 0)
            {
                // On set: keep thousand-separated display when value is a valid amount.
                if (SetProperty(ref _laborCostPerUnitText, FormatLaborCost(labor)))
                    OnPropertyChanged(nameof(QuotePreviewText));
            }
            else
            {
                SetProperty(ref _laborCostPerUnitText, raw);
            }
        }
    }

    public string EstimatedWeightChiText
    {
        get => _estimatedWeightChiText;
        set
        {
            if (SetProperty(ref _estimatedWeightChiText, value ?? string.Empty))
                OnPropertyChanged(nameof(QuotePreviewText));
        }
    }

    public string GoldUnitPricePerChiText
    {
        get => _goldUnitPricePerChiText;
        set
        {
            var raw = value ?? string.Empty;
            if (decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var price)
                && price >= 0)
            {
                if (SetProperty(ref _goldUnitPricePerChiText, FormatLaborCost(price)))
                    OnPropertyChanged(nameof(QuotePreviewText));
            }
            else
            {
                SetProperty(ref _goldUnitPricePerChiText, raw);
            }
        }
    }

    public string StoneAmountPerUnitVndText
    {
        get => _stoneAmountPerUnitVndText;
        set
        {
            var raw = value ?? string.Empty;
            if (decimal.TryParse(raw.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var stone)
                && stone >= 0)
            {
                if (SetProperty(ref _stoneAmountPerUnitVndText, FormatLaborCost(stone)))
                    OnPropertyChanged(nameof(QuotePreviewText));
            }
            else
            {
                SetProperty(ref _stoneAmountPerUnitVndText, raw);
            }
        }
    }

    public string QuotePreviewText
    {
        get
        {
            if (!TryParseQuoteInputs(out var qty, out var weight, out var goldPrice, out var labor, out var stone))
                return "Nhập TL vàng, giá vàng/chỉ, tiền công và đá để xem thành tiền.";
            var line = (weight * goldPrice + labor + stone) * qty;
            return $"Thành tiền dòng ≈ {line:N0} đ  (vàng {weight * goldPrice * qty:N0} + công {labor * qty:N0} + đá {stone * qty:N0}).";
        }
    }

    /// <summary>Reformats labor cost with thousand separators after LostFocus.</summary>
    public void FormatLaborCostPerUnitText()
    {
        if (decimal.TryParse(
                _laborCostPerUnitText.Trim(),
                NumberStyles.Number,
                CultureInfo.CurrentCulture,
                out var labor)
            && labor >= 0)
        {
            var formatted = FormatLaborCost(labor);
            if (!string.Equals(_laborCostPerUnitText, formatted, StringComparison.Ordinal))
            {
                _laborCostPerUnitText = formatted;
                OnPropertyChanged(nameof(LaborCostPerUnitText));
            }
        }
    }

    private static string FormatLaborCost(decimal value) =>
        value.ToString("N0", CultureInfo.CurrentCulture);

    private static string FormatDecimal(decimal value, string format) =>
        value.ToString(format, CultureInfo.CurrentCulture);

    private void ApplyDefaultGoldPriceFromSelection()
    {
        if (SelectedGoldStock is null)
            return;
        if (_sellPriceByMaterialId.TryGetValue(SelectedGoldStock.MaterialId, out var sell) && sell > 0)
            GoldUnitPricePerChiText = FormatLaborCost(sell);
    }

    public void FormatMoneyFieldsOnLostFocus()
    {
        if (decimal.TryParse(LaborCostPerUnitText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var labor)
            && labor >= 0)
            LaborCostPerUnitText = FormatLaborCost(labor);
        if (decimal.TryParse(GoldUnitPricePerChiText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var gold)
            && gold >= 0)
            GoldUnitPricePerChiText = FormatLaborCost(gold);
        if (decimal.TryParse(StoneAmountPerUnitVndText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var stone)
            && stone >= 0)
            StoneAmountPerUnitVndText = FormatLaborCost(stone);
    }

    private bool TryParseQuoteInputs(
        out int qty, out decimal weight, out decimal goldPrice, out decimal labor, out decimal stone)
    {
        qty = 0;
        weight = 0;
        goldPrice = 0;
        labor = 0;
        stone = 0;
        return int.TryParse(PlannedQuantityText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out qty)
            && qty >= 1
            && decimal.TryParse(EstimatedWeightChiText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out weight)
            && weight >= 0
            && decimal.TryParse(GoldUnitPricePerChiText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out goldPrice)
            && goldPrice >= 0
            && decimal.TryParse(LaborCostPerUnitText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out labor)
            && labor >= 0
            && decimal.TryParse(StoneAmountPerUnitVndText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out stone)
            && stone >= 0;
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

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand AddSampleImageCommand { get; }
    public ICommand AddDesignFileCommand { get; }
    public ICommand RemoveAttachmentCommand { get; }
    public ICommand OpenAttachmentCommand { get; }

    public void ApplyTo(ProductionLineEditorViewModel target)
    {
        target.ModelCode = string.IsNullOrWhiteSpace(ModelCode) ? null : ModelCode.Trim();
        target.ModelName = ModelName.Trim();
        target.GoldTypeCode = GoldTypeCode.Trim();
        target.GoldTypeName = GoldTypeName.Trim();
        target.GoldMaterialId = SelectedGoldStock?.MaterialId;
        target.PlannedQuantity = int.Parse(PlannedQuantityText.Trim(), CultureInfo.CurrentCulture);
        target.LaborCostPerUnit = decimal.Parse(
            LaborCostPerUnitText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture);
        target.EstimatedWeightChi = decimal.Parse(
            EstimatedWeightChiText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture);
        target.GoldUnitPricePerChi = decimal.Parse(
            GoldUnitPricePerChiText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture);
        target.StoneAmountPerUnitVnd = decimal.Parse(
            StoneAmountPerUnitVndText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture);
        target.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        target.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();

        target.Attachments.Clear();
        foreach (var attachment in SampleImages.Concat(DesignFiles))
        {
            attachment.LineNumber = target.LineNumber;
            target.Attachments.Add(attachment);
        }

        target.NotifyAttachmentSummaryChanged();
    }

    private void Save()
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(ModelName))
        {
            ErrorMessage = "Vui lòng nhập tên mẫu.";
            return;
        }

        if (SelectedGoldStock is null
            || string.IsNullOrWhiteSpace(GoldTypeCode)
            || string.IsNullOrWhiteSpace(GoldTypeName))
        {
            ErrorMessage = "Vui lòng chọn loại vàng từ kho NVL.";
            return;
        }

        if (SelectedGoldStock.MaterialId <= 0)
        {
            ErrorMessage = "Loại vàng đang chọn không còn tồn. Vui lòng chọn lại từ kho NVL.";
            return;
        }

        if (!int.TryParse(PlannedQuantityText.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var qty)
            || qty < 1)
        {
            ErrorMessage = "Số lượng cần làm phải là số nguyên ≥ 1.";
            return;
        }

        if (!TryParseQuoteInputs(out _, out _, out _, out _, out _))
        {
            ErrorMessage = "TL vàng, giá vàng/chỉ, tiền công và đá phải là số ≥ 0; số lượng ≥ 1.";
            return;
        }

        // Chuyển quyền sở hữu staging sang dòng mẫu; không xóa file.
        _sessionStagingPaths.Clear();
        RequestClose?.Invoke(true);
    }

    private void Cancel()
    {
        CleanupSessionStaging();
        RequestClose?.Invoke(false);
    }

    public void CleanupSessionStaging()
    {
        foreach (var staging in _sessionStagingPaths)
            _attachments.TryDeleteStagingFile(staging);
        _sessionStagingPaths.Clear();
    }

    private async Task AddAttachmentAsync(string kind)
    {
        var isDesign = kind == ProductionAttachmentKinds.Design;
        var dialog = new OpenFileDialog
        {
            Multiselect = true,
            Filter = isDesign
                ? "File thiết kế|*.jpg;*.jpeg;*.png;*.webp;*.pdf;*.ai;*.cdr;*.dxf;*.dwg;*.psd;*.svg;*.zip;*.rar;*.7z;*.doc;*.docx;*.xls;*.xlsx|Tất cả|*.*"
                : "Ảnh mẫu|*.jpg;*.jpeg;*.png;*.webp"
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            ErrorMessage = string.Empty;
            foreach (var file in dialog.FileNames)
            {
                var staging = await _attachments.ImportToStagingAsync(file, kind);
                _sessionStagingPaths.Add(staging);
                var item = new ProductionAttachmentEditorViewModel(
                    staging,
                    Path.GetFileName(file),
                    kind)
                {
                    LineNumber = LineNumber,
                    MimeType = GuessMimeType(file)
                };

                if (item.IsSampleImage)
                    SampleImages.Add(item);
                else
                    DesignFiles.Add(item);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    private void RemoveAttachment(ProductionAttachmentEditorViewModel? attachment)
    {
        if (attachment is null)
            return;

        SampleImages.Remove(attachment);
        DesignFiles.Remove(attachment);
        attachment.MarkDeleted = true;

        if (attachment.Id is > 0)
        {
            _removedAttachments.Add(attachment);
            return;
        }

        // Chỉ xóa staging mới tạo trong phiên dialog; file cũ giữ lại đến khi người dùng bấm Lưu mẫu.
        if (!string.IsNullOrWhiteSpace(attachment.StagingPath)
            && _sessionStagingPaths.Remove(attachment.StagingPath))
        {
            _attachments.TryDeleteStagingFile(attachment.StagingPath);
        }
    }

    private static void OpenAttachment(ProductionAttachmentEditorViewModel? attachment)
    {
        if (attachment is null)
            return;

        var path = !string.IsNullOrWhiteSpace(attachment.StagingPath)
            ? attachment.StagingPath
            : attachment.StoredPath;

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            MessageBox.Show("Không tìm thấy file đính kèm.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void SelectGoldStockFromCurrentValues()
    {
        if (string.IsNullOrWhiteSpace(GoldTypeCode) && string.IsNullOrWhiteSpace(GoldTypeName))
        {
            SelectedGoldStock = null;
            return;
        }

        var match = GoldStockOptions.FirstOrDefault(x =>
            string.Equals(x.Code, GoldTypeCode, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(x.PurityCode)
                && string.Equals(x.PurityCode, GoldTypeCode, StringComparison.OrdinalIgnoreCase))
            || string.Equals(x.Name, GoldTypeName, StringComparison.CurrentCultureIgnoreCase));

        if (match is not null)
        {
            SelectedGoldStock = match;
            return;
        }

        // Giữ giá trị cũ trên UI khi hết tồn / chưa khớp kho — bắt buộc chọn lại khi lưu.
        var legacy = new ProductionMaterialStockOption(
            0,
            string.IsNullOrWhiteSpace(GoldTypeCode) ? "?" : GoldTypeCode.Trim(),
            string.IsNullOrWhiteSpace(GoldTypeName) ? GoldTypeCode.Trim() : GoldTypeName.Trim(),
            MaterialTypes.Gold,
            string.Empty,
            GoldTypeCode,
            0m,
            0m,
            0);
        GoldStockOptions.Insert(0, legacy);
        SelectedGoldStock = legacy;
    }

    private void OnAttachmentCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasSampleImages));
        OnPropertyChanged(nameof(HasDesignFiles));
    }

    private static string? GuessMimeType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            ".svg" => "image/svg+xml",
            _ => null
        };
    }
}
