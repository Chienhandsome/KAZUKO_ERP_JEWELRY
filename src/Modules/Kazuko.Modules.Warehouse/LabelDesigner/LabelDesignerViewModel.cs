using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FastReport;
using FastReport.Barcode;
using FastReport.Export.Image;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Printing;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>
/// ViewModel cho designer sửa mẫu tem nữ trang.
/// Backing store là FastReport Report object model: load từ DB, sửa trực tiếp
/// các TextObject/BarcodeObject, lưu lại bằng Report.Save + IPrintTemplateStore.
/// </summary>
public sealed class LabelDesignerViewModel : ObservableObject
{
    /// <summary>Tỷ lệ hiển thị canvas so với kích thước thật (px 96dpi).</summary>
    public const double CanvasScale = 3.0;

    private const float PaperWidthMm = 86f;
    private const float PaperHeightMm = 12f;
    private const float PxPerMm = 96f / 25.4f;

    private readonly IPrintTemplateStore _templates;
    private readonly AuthenticatedSession _session;

    private Report? _report;
    private LabelDesignerObject? _selectedObject;
    private bool? _dialogResult;
    private string _statusMessage = string.Empty;
    private bool _isBusy;

    public LabelDesignerViewModel(IPrintTemplateStore templates, AuthenticatedSession session)
    {
        _templates = templates;
        _session = session;

        PreviewCommand = new AsyncRelayCommand(PreviewAsync, () => !IsBusy && _report is not null);
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
                            Objects.Add(new LabelDesignerObject(obj));
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

    private Task PreviewAsync()
    {
        if (_report is null)
            return Task.CompletedTask;

        IsBusy = true;
        try
        {
            // Dùng bản sao của template hiện tại (kể cả chỉnh sửa chưa lưu).
            var previewReport = new Report();
            using (var stream = new MemoryStream(GetFrxBytes()))
                previewReport.Load(stream);

            var table = LabelDesignerSampleData.BuildSampleTable();
            previewReport.RegisterData(table, "DataSet");
            previewReport.SetParameterValue("Tên tiệm", "VÀNG BẠC ĐÁ QUÝ MẪU");
            previewReport.SetParameterValue("DiaChiDoanhNghiep", "123 Đường Mẫu - Quận 1 - TP.HCM");
            previewReport.Prepare();

            using var imgStream = new MemoryStream();
            var export = new ImageExport
            {
                ImageFormat = ImageExportFormat.Png,
                Resolution = 200,
            };
            export.Export(previewReport, imgStream);

            var dialog = new LabelPreviewDialog(imgStream.ToArray())
            {
                Owner = System.Windows.Application.Current?.MainWindow,
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
        return Task.CompletedTask;
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
