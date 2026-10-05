using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class JewelryOnHandReportViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IJewelryOnHandReportService _reportService;
    private readonly ICurrentSessionAccessor _sessionAccessor;
    private readonly JewelryOnHandPrintService _printService = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterDebounceCts;
    private bool _suppressFilterReload;

    private string _searchText = string.Empty;
    private string _selectedGoldType = "Tất cả loại vàng";
    private string _selectedStatus = "Tất cả trạng thái";
    private string _selectedScope = "Tất cả kho";
    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isEmpty = true;
    private string _summaryText = "Chưa tải báo cáo.";
    private string? _previewPdfPath;
    private string? _previousPreviewPdfPath;

    public JewelryOnHandReportViewModel(
        IJewelryOnHandReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
    {
        _reportService = reportService;
        _sessionAccessor = sessionAccessor;

        GoldTypeOptions.Add("Tất cả loại vàng");
        GoldTypeOptions.Add("24K");
        GoldTypeOptions.Add("18K");
        GoldTypeOptions.Add("14K");
        GoldTypeOptions.Add("10K");

        StatusOptions.Add("Tất cả trạng thái");
        StatusOptions.Add("Trong kho");
        StatusOptions.Add("Đang giữ");
        StatusOptions.Add("Trưng bày");

        ScopeOptions.Add("Tất cả kho");
        ScopeOptions.Add("Kho thành phẩm");
        ScopeOptions.Add("Kho bán hàng");

        ClearFiltersCommand = new RelayCommand(ClearFilters);
        RefreshCommand = new AsyncRelayCommand(SearchAsync, () => !IsLoading);
        PreviewCommand = new RelayCommand(Preview, CanPrint);
        PrintCommand = new RelayCommand(Print, CanPrint);
    }

    public ObservableCollection<JewelryOnHandRowViewModel> Rows { get; } = new();
    public ObservableCollection<string> GoldTypeOptions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();
    public ObservableCollection<string> ScopeOptions { get; } = new();

    public ICommand ClearFiltersCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand PrintCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ScheduleReload(350);
        }
    }

    public string SelectedGoldType
    {
        get => _selectedGoldType;
        set
        {
            if (SetProperty(ref _selectedGoldType, value))
                ScheduleReload();
        }
    }

    public string SelectedStatus
    {
        get => _selectedStatus;
        set
        {
            if (SetProperty(ref _selectedStatus, value))
                ScheduleReload();
        }
    }

    public string SelectedScope
    {
        get => _selectedScope;
        set
        {
            if (SetProperty(ref _selectedScope, value))
                ScheduleReload();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                (RefreshCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();
                NotifyPrintCommands();
            }
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        private set
        {
            if (SetProperty(ref _isEmpty, value))
                NotifyPrintCommands();
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    /// <summary>Đường dẫn PDF FastReport để hiển thị trong panel (WebView2).</summary>
    public string? PreviewPdfPath
    {
        get => _previewPdfPath;
        private set
        {
            if (!SetProperty(ref _previewPdfPath, value))
                return;
            OnPropertyChanged(nameof(HasPreviewPdf));
        }
    }

    public bool HasPreviewPdf => !string.IsNullOrWhiteSpace(PreviewPdfPath);

    public async Task EnsureLoadedAsync()
    {
        if (Rows.Count > 0 || IsLoading)
            return;
        await SearchAsync();
    }

    public async Task SearchAsync()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            OnPropertyChanged(nameof(HasError));
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        OnPropertyChanged(nameof(HasError));
        StatusMessage = "Đang tải báo cáo…";

        try
        {
            var filter = new JewelryOnHandReportFilter(
                SearchText,
                NormalizeGoldType(SelectedGoldType),
                NormalizeStatus(SelectedStatus),
                NormalizeScope(SelectedScope));

            var result = await _reportService.GetAsync(filter, session, token);
            token.ThrowIfCancellationRequested();

            Rows.Clear();
            var index = 1;
            foreach (var row in result.Rows)
                Rows.Add(new JewelryOnHandRowViewModel(row, index++));

            IsEmpty = Rows.Count == 0;
            SummaryText =
                $"Số món: {result.ItemCount.ToString("N0", Vi)}  ·  " +
                $"Tổng TL vàng: {result.TotalGoldWeight.ToString("N3", Vi)}  ·  " +
                $"Tổng giá trị: {result.TotalStockValue.ToString("N0", Vi)} VND";

            if (IsEmpty)
            {
                ClearPreviewPdf();
                StatusMessage = "Không có nữ trang tồn kho khớp bộ lọc.";
            }
            else
            {
                StatusMessage = $"Đã tải {result.ItemCount.ToString("N0", Vi)} dòng. Đang dựng bản in…";
                RebuildPreviewPdf(session);
                StatusMessage = $"Đã tải {result.ItemCount.ToString("N0", Vi)} dòng.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã hủy tải.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            OnPropertyChanged(nameof(HasError));
            StatusMessage = "Không tải được báo cáo.";
            Rows.Clear();
            IsEmpty = true;
            SummaryText = "Chưa tải báo cáo.";
            ClearPreviewPdf();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RebuildPreviewPdf(AuthenticatedSession session)
    {
        Report? report = null;
        try
        {
            report = _printService.CreatePreparedReport(
                Rows.ToArray(),
                session,
                SelectedScope,
                SelectedGoldType,
                SelectedStatus,
                SearchText);
            var path = _printService.ExportToTempPdf(report, "view");
            var old = _previousPreviewPdfPath;
            _previousPreviewPdfPath = path;
            PreviewPdfPath = path;
            TryDeleteFile(old);
        }
        catch (Exception ex)
        {
            ClearPreviewPdf();
            ErrorMessage = "Không dựng được bản xem báo cáo: " + ex.Message;
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            report?.Dispose();
        }
    }

    private void ClearPreviewPdf()
    {
        var old = PreviewPdfPath ?? _previousPreviewPdfPath;
        PreviewPdfPath = null;
        _previousPreviewPdfPath = null;
        TryDeleteFile(old);
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore temp cleanup failures
        }
    }

    private void ClearFilters()
    {
        _suppressFilterReload = true;
        try
        {
            SearchText = string.Empty;
            SelectedGoldType = "Tất cả loại vàng";
            SelectedStatus = "Tất cả trạng thái";
            SelectedScope = "Tất cả kho";
        }
        finally
        {
            _suppressFilterReload = false;
        }

        _ = SearchAsync();
    }

    private void ScheduleReload(int debounceMs = 0)
    {
        if (_suppressFilterReload)
            return;

        _filterDebounceCts?.Cancel();
        _filterDebounceCts = new CancellationTokenSource();
        var token = _filterDebounceCts.Token;
        _ = ReloadAfterDelayAsync(debounceMs, token);
    }

    private async Task ReloadAfterDelayAsync(int debounceMs, CancellationToken token)
    {
        try
        {
            if (debounceMs > 0)
                await Task.Delay(debounceMs, token);
            token.ThrowIfCancellationRequested();
            await SearchAsync();
        }
        catch (OperationCanceledException)
        {
            // Bỏ qua khi người dùng đổi lọc liên tục.
        }
    }

    private bool CanPrint() => !IsLoading && !IsEmpty && _sessionAccessor.Current is not null;

    private void Preview()
    {
        Report? report = null;
        try
        {
            report = CreatePrintReport();
            if (report is null)
                return;
            _printService.ShowPreview(report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "Xem trước báo cáo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            report?.Dispose();
        }
    }

    private void Print()
    {
        Report? report = null;
        try
        {
            report = CreatePrintReport();
            if (report is null)
                return;
            _printService.Print(report);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                ex.Message,
                "In báo cáo",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            report?.Dispose();
        }
    }

    private Report? CreatePrintReport()
    {
        var session = _sessionAccessor.Current;
        if (session is null)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                "Chưa đăng nhập.",
                "In báo cáo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        if (Rows.Count == 0)
        {
            MessageBox.Show(
                Application.Current?.MainWindow,
                "Không có dữ liệu để in. Hãy tải báo cáo trước.",
                "In báo cáo",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        return _printService.CreatePreparedReport(
            Rows.ToArray(),
            session,
            SelectedScope,
            SelectedGoldType,
            SelectedStatus,
            SearchText);
    }

    private void NotifyPrintCommands()
    {
        (PreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PrintCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private static string NormalizeGoldType(string value) =>
        string.IsNullOrWhiteSpace(value) || value.StartsWith("Tất cả", StringComparison.Ordinal)
            ? string.Empty
            : value.Trim();

    private static string NormalizeStatus(string value) => value switch
    {
        "Trong kho" => "IN_STOCK",
        "Đang giữ" => "RESERVED",
        "Trưng bày" => "DISPLAYED",
        _ => string.Empty
    };

    private static string NormalizeScope(string value) => value switch
    {
        "Kho thành phẩm" => "FINISHED_GOODS",
        "Kho bán hàng" => "SALES_FLOOR",
        _ => string.Empty
    };
}
