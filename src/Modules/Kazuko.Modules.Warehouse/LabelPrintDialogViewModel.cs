using System.Collections.ObjectModel;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;

namespace Kazuko.Modules.Warehouse;

/// <summary>Dialog chọn máy in tem + số bản in.</summary>
public sealed class LabelPrintDialogViewModel : ObservableObject
{
    private const double MaxOffsetMm = 20;
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private string? _selectedPrinter;
    private string _copiesText = "1";
    private string _verticalOffsetText = "0";
    private string _errorMessage = string.Empty;
    private bool? _dialogResult;
    private bool _compactWeightUnits = LabelPrinterPreferences.LoadCompactWeightUnits();
    private readonly Dictionary<string, string> _offsetDrafts = new(StringComparer.OrdinalIgnoreCase);
    private JewelryLabelTemplateOption _selectedTemplate;

    public LabelPrintDialogViewModel(int itemCount, string? summaryText = null, string? templateCode = null)
    {
        _selectedTemplate = JewelryLabelTemplates.Resolve(templateCode ?? LabelPrinterPreferences.LoadTemplateCode());
        Printers = new ObservableCollection<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
            Printers.Add(printer);

        var lastPrinter = LabelPrinterPreferences.LoadPrinterName();
        _selectedPrinter = !string.IsNullOrWhiteSpace(lastPrinter) && Printers.Contains(lastPrinter)
            ? lastPrinter
            : Printers.FirstOrDefault();
        _verticalOffsetText = FormatOffset(LabelPrinterPreferences.LoadVerticalOffsetMm(_selectedPrinter));

        SummaryText = summaryText ?? $"In tem cho {itemCount} sản phẩm đã chọn.";

        PrintCommand = new RelayCommand(Print, () => !string.IsNullOrWhiteSpace(SelectedPrinter));
        CancelCommand = new RelayCommand(() => DialogResult = false);
    }

    public ObservableCollection<string> Printers { get; }

    public string SummaryText { get; }
    public IReadOnlyList<JewelryLabelTemplateOption> Templates => JewelryLabelTemplates.All;
    public JewelryLabelTemplateOption SelectedTemplate
    {
        get => _selectedTemplate;
        set { if (value is not null && Templates.Contains(value)) SetProperty(ref _selectedTemplate, value); }
    }
    public bool CompactWeightUnits
    {
        get => _compactWeightUnits;
        set => SetProperty(ref _compactWeightUnits, value);
    }

    public string? SelectedPrinter
    {
        get => _selectedPrinter;
        set
        {
            if (string.Equals(_selectedPrinter, value, StringComparison.OrdinalIgnoreCase))
                return;
            if (!string.IsNullOrWhiteSpace(_selectedPrinter))
                _offsetDrafts[_selectedPrinter] = _verticalOffsetText;
            if (!SetProperty(ref _selectedPrinter, value))
                return;
            VerticalOffsetText = value is not null && _offsetDrafts.TryGetValue(value, out var draft)
                ? draft
                : FormatOffset(LabelPrinterPreferences.LoadVerticalOffsetMm(value));
            ((RelayCommand)PrintCommand).NotifyCanExecuteChanged();
        }
    }

    public string VerticalOffsetText
    {
        get => _verticalOffsetText;
        set => SetProperty(ref _verticalOffsetText, value);
    }

    public double VerticalOffsetMm =>
        TryParseVerticalOffset(VerticalOffsetText, out var offset) ? offset : 0;

    public string CopiesText
    {
        get => _copiesText;
        set => SetProperty(ref _copiesText, value);
    }

    public int Copies
    {
        get
        {
            if (int.TryParse(CopiesText, out var copies) && copies > 0 && copies <= 999)
                return copies;
            return 1;
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public ICommand PrintCommand { get; }
    public ICommand CancelCommand { get; }

    private void Print()
    {
        if (string.IsNullOrWhiteSpace(SelectedPrinter))
        {
            ErrorMessage = "Vui lòng chọn máy in.";
            return;
        }

        if (!int.TryParse(CopiesText, out var copies) || copies <= 0 || copies > 999)
        {
            ErrorMessage = "Số bản in phải từ 1 đến 999.";
            return;
        }

        if (!TryParseVerticalOffset(VerticalOffsetText, out var offset))
        {
            ErrorMessage = "Lệch dọc phải là số từ -20 đến 20 mm. Số dương đẩy nội dung xuống.";
            return;
        }

        LabelPrinterPreferences.Save(SelectedPrinter, offset, CompactWeightUnits, SelectedTemplate.Code);
        DialogResult = true;
    }

    internal static bool TryParseVerticalOffset(string? text, out double millimeters)
    {
        millimeters = 0;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var trimmed = text.Trim();
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowLeadingSign;
        var ok = trimmed.Contains(',')
            ? double.TryParse(trimmed, styles, Vi, out millimeters)
            : double.TryParse(trimmed, styles, CultureInfo.InvariantCulture, out millimeters);
        return ok && millimeters is >= -MaxOffsetMm and <= MaxOffsetMm;
    }

    private static string FormatOffset(double millimeters) =>
        millimeters.ToString("0.##", Vi);
}
