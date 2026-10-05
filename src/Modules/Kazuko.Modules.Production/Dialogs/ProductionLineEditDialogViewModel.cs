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
    private string? _description;
    private string? _notes;
    private string _errorMessage = string.Empty;

    public ProductionLineEditDialogViewModel(
        IProductionAttachmentStore attachments,
        int lineNumber,
        bool isEditable,
        IReadOnlyList<ProductionMaterialStockOption> goldStockOptions,
        ProductionLineEditorViewModel? source = null)
    {
        _attachments = attachments;
        _isEditable = isEditable;
        LineNumber = lineNumber;

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
            LaborCostPerUnitText = source.LaborCostPerUnit.ToString("0.##", CultureInfo.CurrentCulture);
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
        ? (string.IsNullOrWhiteSpace(ModelName) ? $"Mẫu #{LineNumber}" : $"Mẫu #{LineNumber}: {ModelName}")
        : $"Xem mẫu #{LineNumber}";

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
            }

            OnPropertyChanged(nameof(SelectedGoldStockHint));
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
        set => SetProperty(ref _plannedQuantityText, value ?? string.Empty);
    }

    public string LaborCostPerUnitText
    {
        get => _laborCostPerUnitText;
        set => SetProperty(ref _laborCostPerUnitText, value ?? string.Empty);
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
        target.PlannedQuantity = int.Parse(PlannedQuantityText.Trim(), CultureInfo.CurrentCulture);
        target.LaborCostPerUnit = decimal.Parse(LaborCostPerUnitText.Trim(), CultureInfo.CurrentCulture);
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

        if (!decimal.TryParse(LaborCostPerUnitText.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var labor)
            || labor < 0)
        {
            ErrorMessage = "Tiền công mỗi món phải ≥ 0.";
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
