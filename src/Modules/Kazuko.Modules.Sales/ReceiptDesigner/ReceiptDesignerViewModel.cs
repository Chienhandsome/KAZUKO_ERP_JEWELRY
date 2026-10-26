using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Xml;
using System.Xml.Linq;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Sales.Services;

namespace Kazuko.Modules.Sales.ReceiptDesigner;

public sealed class ReceiptDesignerViewModel : ObservableObject
{
    public const string LayoutMarker = "KAZUKO_RECEIPT_DESIGNER_V1";
    private readonly string _code;
    private readonly AuthenticatedSession _session;
    private readonly IPrintTemplateStore _templates;
    private readonly ReceiptTemplatePreviewService _preview;
    private readonly IAuthorizationService _authorization;
    private XDocument? _document;
    private string _name = string.Empty;
    private ReceiptDesignerSection? _section;
    private ReceiptDesignerObject? _selectedObject;
    private string _statusMessage = string.Empty;
    private bool _isBusy;
    private bool _isDirty;
    private bool? _dialogResult;
    private byte[]? _previewImage;
    public ReceiptDesignerViewModel(string code, AuthenticatedSession session, IPrintTemplateStore templates,
        ReceiptTemplatePreviewService preview, IAuthorizationService authorization)
    {
        if (code is not (PrintTemplateCodes.SalesReceiptA5 or PrintTemplateCodes.SalesReceiptA5Portrait))
            throw new ArgumentException("Mẫu bill không hợp lệ.", nameof(code));
        _code = code; _session = session; _templates = templates; _preview = preview; _authorization = authorization;
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && _document is not null && IsDirty);
        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && _document is not null);
        AddTextCommand = new RelayCommand(AddText, () => !IsBusy && SelectedSection is not null);
        DeleteCommand = new RelayCommand(DeleteSelected, () => !IsBusy && SelectedObject is { CanMove: true });
        CancelCommand = new RelayCommand(() => { _dialogResult = false; OnPropertyChanged(nameof(DialogResult)); });
    }
    public string EditingTemplateName => _name;
    public ObservableCollection<ReceiptDesignerSection> Sections { get; } = [];
    public ObservableCollection<ReceiptDesignerObject> Objects { get; } = [];
    public IReadOnlyList<string> HorizontalAlignments { get; } = ["Left", "Center", "Right", "Justify"];
    public IReadOnlyList<string> VerticalAlignments { get; } = ["Top", "Center", "Bottom"];
    public ReceiptDesignerSection? SelectedSection
    {
        get => _section;
        set
        {
            if (!SetProperty(ref _section, value)) return;
            RebuildObjects();
            OnPropertyChanged(nameof(CanvasWidth)); OnPropertyChanged(nameof(CanvasHeight)); OnPropertyChanged(nameof(SectionHeightMm));
            NotifyCommands();
        }
    }
    public ReceiptDesignerObject? SelectedObject { get => _selectedObject; set { if (SetProperty(ref _selectedObject, value)) NotifyCommands(); } }
    public double CanvasWidth => SelectedSection?.Width ?? 700;
    public double CanvasHeight => SelectedSection?.Height ?? 100;
    public double SectionHeightMm
    {
        get => ReceiptDesignerObject.Read(SelectedSection?.Element ?? new XElement("Band"), "Height") * 25.4 / 96;
        set
        {
            if (SelectedSection is null || !double.IsFinite(value) || value is < 1 or > 300) return;
            SelectedSection.Element.SetAttributeValue("Height", XmlConvert.ToString(value * 96 / 25.4));
            IsDirty = true; OnPropertyChanged(); OnPropertyChanged(nameof(CanvasHeight));
        }
    }
    public bool SnapToGrid { get; set; }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanEdit)); NotifyCommands(); } } }
    public bool CanEdit => !IsBusy;
    public bool IsDirty { get => _isDirty; private set { if (SetProperty(ref _isDirty, value)) NotifyCommands(); } }
    public bool? DialogResult { get => _dialogResult; private set => SetProperty(ref _dialogResult, value); }
    public byte[]? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
    public ICommand SaveCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand AddTextCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand CancelCommand { get; }

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await DemandEditAsync();
            var dto = await _templates.GetByCodeAsync(_code, _session);
            using var report = new Report();
            using (var input = new MemoryStream(dto.Content)) report.Load(input);
            // Bake the legacy runtime layout once, so editing and printing use identical positions.
            SalesReceiptPrintService.NormalizeReceiptTemplate(report);
            report.ReportInfo.Description = LayoutMarker;
            using var output = new MemoryStream();
            report.Save(output); output.Position = 0;
            using var reader = XmlReader.Create(output, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            _document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            _name = dto.Name; OnPropertyChanged(nameof(EditingTemplateName));
            Sections.Clear();
            foreach (var page in _document.Root!.Elements("ReportPage"))
            {
                var width = (ReceiptDesignerObject.Read(page, "PaperWidth", 210)
                    - ReceiptDesignerObject.Read(page, "LeftMargin", 10) - ReceiptDesignerObject.Read(page, "RightMargin", 10)) * 96 / 25.4;
                foreach (var band in page.Descendants().Where(x => x.Name.LocalName.EndsWith("Band", StringComparison.Ordinal)))
                    Sections.Add(new(band, width, $"{(string?)page.Attribute("Name")} · {BandLabel(band.Name.LocalName)} · {(string?)band.Attribute("Name")}"));
            }
            SelectedSection = Sections.FirstOrDefault();
            IsDirty = false;
            StatusMessage = "Chọn vùng nội dung, rồi kéo-thả đối tượng. Xem trước dùng bản đang sửa, chưa cần lưu.";
        }
        catch (Exception ex) { StatusMessage = $"Không nạp được mẫu bill: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private static string BandLabel(string type) => type switch
    {
        "ReportTitleBand" => "Đầu phiếu", "DataBand" => "Dòng dữ liệu", "ReportSummaryBand" => "Tổng kết",
        "PageFooterBand" => "Chân trang", "PageHeaderBand" => "Đầu trang", "DataHeaderBand" => "Tiêu đề bảng", _ => type
    };
    private void RebuildObjects()
    {
        SelectedObject = null;
        Objects.Clear();
        if (SelectedSection is null) return;
        foreach (var node in SelectedSection.Element.Elements().Where(x => x.Name.LocalName.EndsWith("Object", StringComparison.Ordinal)))
        {
            AddWrapper(node);
            foreach (var cell in node.Descendants("TableCell")) AddWrapper(cell);
        }
    }
    private void AddWrapper(XElement node)
    {
        var item = new ReceiptDesignerObject(node);
        item.PropertyChanged += (_, _) => IsDirty = true;
        Objects.Add(item);
    }
    private void AddText()
    {
        if (SelectedSection is null || _document is null) return;
        var names = _document.Descendants().Attributes("Name").Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
        var n = 1; while (names.Contains($"BillText{n}")) n++;
        var node = new XElement("TextObject", new XAttribute("Name", $"BillText{n}"), new XAttribute("Left", "10"),
            new XAttribute("Top", "10"), new XAttribute("Width", "160"), new XAttribute("Height", "24"),
            new XAttribute("Text", "Nội dung mới"), new XAttribute("Font", "Arial, 9pt"));
        SelectedSection.Element.Add(node); AddWrapper(node); SelectedObject = Objects[^1]; IsDirty = true;
    }
    private void DeleteSelected()
    {
        if (SelectedObject is not { CanMove: true } selected) return;
        selected.Element.Remove(); RebuildObjects(); IsDirty = true;
    }
    public byte[] GetFrxBytes()
    {
        if (_document is null) throw new InvalidOperationException("Chưa nạp mẫu bill.");
        using var stream = new MemoryStream(); _document.Save(stream); return stream.ToArray();
    }
    private Task DemandEditAsync() => _authorization.DemandAsync([new("SYS_SETTINGS", PermissionAction.Edit)]);
    private async Task PreviewAsync()
    {
        IsBusy = true;
        try { PreviewImage = await _preview.CreateDraftPreviewAsync(_code, _session, GetFrxBytes()); StatusMessage = "Đã xem trước bản đang sửa với dữ liệu minh họa."; }
        catch (Exception ex) { StatusMessage = $"Xem trước thất bại: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await DemandEditAsync();
            var bytes = GetFrxBytes();
            // Prepare/render the draft before persistence: invalid expressions must not replace the usable template.
            await _preview.CreateDraftPreviewAsync(_code, _session, bytes);
            await _templates.SaveAsync(_code, _name, bytes, "Sửa bằng Bill Designer", _session);
            IsDirty = false; DialogResult = true;
        }
        catch (Exception ex) { StatusMessage = $"Lưu mẫu thất bại: {ex.Message}"; }
        finally { IsBusy = false; }
    }
    private void NotifyCommands()
    {
        ((AsyncRelayCommand)SaveCommand).NotifyCanExecuteChanged(); ((AsyncRelayCommand)PreviewCommand).NotifyCanExecuteChanged();
        ((RelayCommand)AddTextCommand).NotifyCanExecuteChanged(); ((RelayCommand)DeleteCommand).NotifyCanExecuteChanged();
    }
}
