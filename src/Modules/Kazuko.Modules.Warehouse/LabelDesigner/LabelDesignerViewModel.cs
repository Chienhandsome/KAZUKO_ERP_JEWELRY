using System.Collections.ObjectModel;
using System.Data;
using System.Drawing.Printing;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FastReport;
using FastReport.Barcode;
using FastReport.Export.Image;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;
using Kazuko.Modules.Warehouse.Printing;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

public sealed record LabelBarcodeKind(string Code, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// ViewModel cho designer sửa mẫu tem nữ trang.
/// Backing store là FastReport Report object model: load từ DB, sửa trực tiếp
/// các TextObject/BarcodeObject, lưu lại bằng Report.Save + IPrintTemplateStore.
/// </summary>
public sealed class LabelDesignerViewModel : ObservableObject
{
    public IReadOnlyList<HorzAlign> HorizontalAlignments { get; } = Enum.GetValues<HorzAlign>();
    public IReadOnlyList<VertAlign> VerticalAlignments { get; } = Enum.GetValues<VertAlign>();
    public IReadOnlyList<LabelBarcodeKind> BarcodeKinds { get; } =
        [new("Code128", "Barcode (Code 128)"), new("QR", "QR")];
    /// <summary>Tỷ lệ hiển thị canvas so với kích thước thật (px 96dpi).</summary>
    public const double CanvasScale = 3.0;

    private const float PaperWidthMm = 86f;
    private const float PaperHeightMm = 12f;
    private const float PxPerMm = 96f / 25.4f;

    private readonly IPrintTemplateStore _templates;
    private readonly ICompanyProfileService _companyProfile;
    private readonly AuthenticatedSession _session;

    private Report? _report;
    private LabelDesignerObject? _selectedObject;
    private bool? _dialogResult;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public LabelDesignerViewModel(
        IPrintTemplateStore templates,
        ICompanyProfileService companyProfile,
        AuthenticatedSession session)
    {
        _templates = templates;
        _companyProfile = companyProfile;
        _session = session;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && _report is not null);
        PrintTestCommand = new AsyncRelayCommand(PrintTestAsync, () => !IsBusy && _report is not null);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => !IsBusy && _report is not null);
        CancelCommand = new RelayCommand(() => DialogResult = false);
    }

    /// <summary>Chiều rộng canvas hiển thị (px màn hình).</summary>
    public double CanvasWidth => PaperWidthMm * PxPerMm * CanvasScale;

    /// <summary>Chiều cao canvas hiển thị (px màn hình).</summary>
    public double CanvasHeight => PaperHeightMm * PxPerMm * CanvasScale;

    public ObservableCollection<LabelDesignerObject> Objects { get; } = new();

    public LabelDesignerObject? SelectedObject
    {
        get => _selectedObject;
        set => SetProperty(ref _selectedObject, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                (PreviewCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (PrintTestCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                (SaveCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
            }
        }
    }

    public bool? DialogResult
    {
        get => _dialogResult;
        private set => SetProperty(ref _dialogResult, value);
    }

    public ICommand PreviewCommand { get; }
    public ICommand PrintTestCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Report FastReport gốc (code-behind dùng để vẽ lại khi cần).</summary>
    public Report? Report => _report;

    /// <summary>
    /// Nạp mẫu tem từ DB. Gọi một lần sau khi tạo ViewModel.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        IsBusy = true;
        try
        {
            var dto = await _templates.GetByCodeAsync(
                PrintTemplateCodes.JewelryLabel28x12, _session, cancellationToken);

            _report = new Report();
            using (var stream = new MemoryStream(dto.Content))
                _report.Load(stream);

            Objects.Clear();
            var page = _report.Pages.OfType<ReportPage>().FirstOrDefault();
            if (page is not null)
            {
                foreach (var band in page.Bands.OfType<BandBase>())
                {
                    foreach (var obj in band.Objects.OfType<ReportComponentBase>())
                    {
                        if (obj is TextObject || obj is BarcodeObject)
                        {
                            if (obj is BarcodeObject barcode)
                            {
                                barcode.DataColumn = "DataSet.MAVACH";
                                barcode.Expression = string.Empty;
                            }
                            Objects.Add(new LabelDesignerObject(obj));
                        }
                    }
                }
            }

            SelectedObject = null;
            StatusMessage = $"Đã nạp {Objects.Count} đối tượng từ mẫu tem.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Không nạp được mẫu tem: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Lấy bytes .frx hiện tại (kể cả các chỉnh sửa chưa lưu).</summary>
    public byte[] GetFrxBytes()
    {
        if (_report is null)
            return Array.Empty<byte>();
        using var stream = new MemoryStream();
        _report.Save(stream);
        return stream.ToArray();
    }

    private async Task PreviewAsync()
    {
        if (_report is null)
            return;

        IsBusy = true;
        try
        {
            var profile = await _companyProfile.GetAsync(_session);
            using var previewReport = CreateSampleReport(profile);

            using var imgStream = new MemoryStream();
            using var export = new ImageExport
            {
                ImageFormat = ImageExportFormat.Png,
                Resolution = 200,
            };
            export.Export(previewReport, imgStream);

            var dialog = new LabelPreviewDialog(imgStream.ToArray())
            {
                Owner = DesignerWindow,
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Xem trước thất bại: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Window? DesignerWindow => System.Windows.Application.Current?.Windows.OfType<LabelDesignerDialog>()
        .FirstOrDefault(window => ReferenceEquals(window.DataContext, this))
        ?? System.Windows.Application.Current?.MainWindow;

    private Report CreateSampleReport(CompanyProfileDto profile)
    {
        var report = new Report();
        try
        {
            using (var stream = new MemoryStream(GetFrxBytes()))
                report.Load(stream);
            foreach (var page in report.Pages.OfType<ReportPage>())
            {
                page.Landscape = false;
                page.PaperWidth = PaperWidthMm;
                page.PaperHeight = PaperHeightMm;
                page.LeftMargin = page.TopMargin = page.RightMargin = page.BottomMargin = 0;
            }
            var address = profile.Address ?? string.Empty;
            using var table = LabelDesignerSampleData.BuildSampleTable(profile.Name, address, profile.FacilityStandard);
            FinishedGoodsLabelPrintService.RegisterTable(report, table, "DataSet");
            report.SetParameterValue("Tên tiệm", profile.Name);
            report.SetParameterValue("DiaChiDoanhNghiep", address);
            report.Prepare();
            return report;
        }
        catch
        {
            report.Dispose();
            throw;
        }
    }

    private async Task PrintTestAsync()
    {
        if (_report is null) return;
        IsBusy = true;
        try
        {
            var profile = await _companyProfile.GetAsync(_session);
            var selection = new LabelPrintDialogViewModel(1,
                "In thử bằng dữ liệu hàng mẫu, khổ 86 × 12 mm. Tên doanh nghiệp, địa chỉ và TCCS lấy từ Thông tin chung. Dùng các chỉnh sửa hiện tại, kể cả khi chưa lưu mẫu.");
            var dialog = new LabelPrintDialog(selection) { Owner = DesignerWindow };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(selection.SelectedPrinter))
                return;

            using var report = CreateSampleReport(profile);
            using var document = new PrintDocument { DocumentName = "In thử tem nữ trang KAZUKO" };
            document.PrinterSettings.PrinterName = selection.SelectedPrinter;
            document.PrinterSettings.Copies = (short)selection.Copies;
            document.DefaultPageSettings.Landscape = false;
            document.DefaultPageSettings.PaperSize = new PaperSize("Tem 86 x 12 mm",
                (int)Math.Round(PaperWidthMm * 100 / 25.4), (int)Math.Round(PaperHeightMm * 100 / 25.4));
            document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            document.OriginAtMargins = false;
            document.PrintPage += (_, e) =>
            {
                var graphics = e.Graphics!;
                LabelPrintOrigin.Apply(graphics, e.PageSettings, selection.VerticalOffsetMm);
                VectorLabelRenderer.DrawPreparedLabel(graphics, report);
                e.HasMorePages = false;
            };
            document.Print();
            StatusMessage = $"Đã gửi {selection.Copies} tem in thử đến máy in {selection.SelectedPrinter}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"In thử thất bại: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_report is null)
            return;

        IsBusy = true;
        try
        {
            var frxBytes = GetFrxBytes();
            await _templates.SaveAsync(
                PrintTemplateCodes.JewelryLabel28x12,
                "Tem nữ trang 28x12",
                frxBytes,
                "Sửa bằng Label Designer",
                _session);
            StatusMessage = "Đã lưu mẫu tem.";
            DialogResult = true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Lưu thất bại: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
