using System.Windows;
using System.Windows.Input;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Reports.Shared;

/// <summary>
/// Base cho mọi báo cáo: tải dữ liệu, debounce lọc, PDF preview/in, trạng thái UI.
/// Báo cáo cụ thể chỉ implement filter + LoadData + CreatePreparedReport.
/// </summary>
public abstract class ReportWorkspaceViewModel : ObservableObject
{
    private readonly FastReportPdfExporter _pdfExporter = new();
    private CancellationTokenSource? _loadCts;
    private CancellationTokenSource? _filterDebounceCts;
    private bool _suppressFilterReload;

    private string _statusMessage = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isLoading;
    private bool _isEmpty = true;
    private string _summaryText = "Chưa tải báo cáo.";
    private string? _previewPdfPath;
    private string? _previousPreviewPdfPath;

    protected ReportWorkspaceViewModel(
        ICurrentSessionAccessor sessionAccessor,
        string pdfFilePrefix,
        string? columnSettingsKey = null)
    {
        SessionAccessor = sessionAccessor;
        PdfFilePrefix = pdfFilePrefix;
        ColumnSettingsKey = columnSettingsKey;

        ClearFiltersCommand = new RelayCommand(ClearFilters);
        RefreshCommand = new AsyncRelayCommand(ReloadAsync, () => !IsLoading);
        PreviewCommand = new RelayCommand(Preview, CanExportPdf);
        PrintCommand = new RelayCommand(Print, CanExportPdf);
        ConfigureColumnsCommand = new RelayCommand(ConfigureColumns, () => SupportsColumnLayout);

        if (SupportsColumnLayout)
            ActiveColumns = ReportColumnLayout.Resolve(AvailableColumns, ColumnSettingsKey!);
    }

    protected ICurrentSessionAccessor SessionAccessor { get; }
    protected string PdfFilePrefix { get; }
    protected string? ColumnSettingsKey { get; }

    public ICommand ClearFiltersCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand PrintCommand { get; }
    public ICommand ConfigureColumnsCommand { get; }

    /// <summary>Cột đang dùng để dựng PDF (đã ẩn/hiện + sắp xếp).</summary>
    public IReadOnlyList<ReportColumnDefinition> ActiveColumns { get; private set; } = [];

    public bool SupportsColumnLayout =>
        !string.IsNullOrWhiteSpace(ColumnSettingsKey) && AvailableColumns.Count > 0;

    /// <summary>Danh sách cột có thể cấu hình. Override ở báo cáo cụ thể.</summary>
    protected virtual IReadOnlyList<ReportColumnDefinition> AvailableColumns => [];

    public string StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        protected set
        {
            if (SetProperty(ref _errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
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
                NotifyExportCommands();
            }
        }
    }

    public bool IsEmpty
    {
        get => _isEmpty;
        protected set
        {
            if (SetProperty(ref _isEmpty, value))
                NotifyExportCommands();
        }
    }

    public string SummaryText
    {
        get => _summaryText;
        protected set => SetProperty(ref _summaryText, value);
    }

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

    /// <summary>Thông báo khi không có dòng dữ liệu.</summary>
    protected virtual string EmptyResultMessage => "Không có dữ liệu khớp bộ lọc.";

    /// <summary>True khi đã có dữ liệu in được (sau LoadDataAsync).</summary>
    protected abstract bool HasPrintableData { get; }

    protected abstract void ResetFiltersCore();

    protected abstract Task LoadDataAsync(AuthenticatedSession session, CancellationToken token);

    protected abstract Report CreatePreparedReport(AuthenticatedSession session);

    public async Task EnsureLoadedAsync()
    {
        if (HasPrintableData || IsLoading)
            return;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        var session = SessionAccessor.Current;
        if (session is null)
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var token = _loadCts.Token;

        IsLoading = true;
        ErrorMessage = string.Empty;
        StatusMessage = "Đang tải báo cáo…";

        try
        {
            await LoadDataAsync(session, token);
            token.ThrowIfCancellationRequested();

            if (IsEmpty || !HasPrintableData)
            {
                ClearPreviewPdf();
                StatusMessage = EmptyResultMessage;
            }
            else
            {
                StatusMessage = "Đang dựng bản in…";
                RebuildPreviewPdf(session);
                if (string.IsNullOrEmpty(ErrorMessage))
                    StatusMessage = BuildLoadedStatus();
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Đã hủy tải.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            StatusMessage = "Không tải được báo cáo.";
            IsEmpty = true;
            SummaryText = "Chưa tải báo cáo.";
            ClearPreviewPdf();
            OnLoadFailed();
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Gọi khi LoadDataAsync thất bại (ví dụ Rows.Clear).</summary>
    protected virtual void OnLoadFailed()
    {
    }

    protected virtual string BuildLoadedStatus() => "Đã tải báo cáo.";

    protected void ScheduleReload(int debounceMs = 0)
    {
        if (_suppressFilterReload)
            return;

        _filterDebounceCts?.Cancel();
        _filterDebounceCts = new CancellationTokenSource();
        var token = _filterDebounceCts.Token;
        _ = ReloadAfterDelayAsync(debounceMs, token);
    }

    protected void RunWithoutFilterReload(Action action)
    {
        _suppressFilterReload = true;
        try
        {
            action();
        }
        finally
        {
            _suppressFilterReload = false;
        }
    }

    protected void SetPreviewFromExport(string path)
    {
        var old = _previousPreviewPdfPath;
        _previousPreviewPdfPath = path;
        PreviewPdfPath = path;
        FastReportPdfExporter.TryDeleteFile(old);
    }

    protected void ClearPreviewPdf()
    {
        var old = PreviewPdfPath ?? _previousPreviewPdfPath;
        PreviewPdfPath = null;
        _previousPreviewPdfPath = null;
        FastReportPdfExporter.TryDeleteFile(old);
    }

    private async Task ReloadAfterDelayAsync(int debounceMs, CancellationToken token)
    {
        try
        {
            if (debounceMs > 0)
                await Task.Delay(debounceMs, token);
            token.ThrowIfCancellationRequested();
            await ReloadAsync();
        }
        catch (OperationCanceledException)
        {
            // Đổi lọc liên tục.
        }
    }

    private void ClearFilters()
    {
        RunWithoutFilterReload(ResetFiltersCore);
        _ = ReloadAsync();
    }

    private void ConfigureColumns()
    {
        if (!SupportsColumnLayout)
            return;

        var dialog = new ReportColumnVisibilityDialog(AvailableColumns, ActiveColumns, ColumnSettingsKey!);
        dialog.Owner = Application.Current?.MainWindow;
        if (dialog.ShowDialog() != true || dialog.ResultColumns is null)
            return;

        ActiveColumns = dialog.ResultColumns;
        OnPropertyChanged(nameof(ActiveColumns));
        RefreshPreviewAfterColumnChange();
    }

    private void RefreshPreviewAfterColumnChange()
    {
        var session = SessionAccessor.Current;
        if (session is null || !HasPrintableData)
            return;

        StatusMessage = "Đang dựng lại bản in theo cột…";
        RebuildPreviewPdf(session);
        if (string.IsNullOrEmpty(ErrorMessage))
            StatusMessage = BuildLoadedStatus();
    }

    private bool CanExportPdf() =>
        !IsLoading && !IsEmpty && HasPrintableData && SessionAccessor.Current is not null;

    private void Preview()
    {
        using var report = TryCreateExportReport("Xem trước báo cáo");
        if (report is null)
            return;

        try
        {
            _pdfExporter.OpenExternally(report, PdfFilePrefix);
        }
        catch (Exception ex)
        {
            ShowWarning("Xem trước báo cáo", ex.Message);
        }
    }

    private void Print()
    {
        using var report = TryCreateExportReport("In báo cáo");
        if (report is null)
            return;

        try
        {
            _pdfExporter.Print(report, PdfFilePrefix);
        }
        catch (Exception ex)
        {
            ShowWarning("In báo cáo", ex.Message);
        }
    }

    private Report? TryCreateExportReport(string dialogTitle)
    {
        var session = SessionAccessor.Current;
        if (session is null)
        {
            ShowInfo(dialogTitle, "Chưa đăng nhập.");
            return null;
        }

        if (!HasPrintableData)
        {
            ShowInfo(dialogTitle, "Không có dữ liệu để in. Hãy tải báo cáo trước.");
            return null;
        }

        try
        {
            return CreatePreparedReport(session);
        }
        catch (Exception ex)
        {
            ShowWarning(dialogTitle, ex.Message);
            return null;
        }
    }

    private void RebuildPreviewPdf(AuthenticatedSession session)
    {
        Report? report = null;
        try
        {
            report = CreatePreparedReport(session);
            var path = _pdfExporter.ExportToTempPdf(report, PdfFilePrefix, "view");
            SetPreviewFromExport(path);
        }
        catch (Exception ex)
        {
            ClearPreviewPdf();
            ErrorMessage = "Không dựng được bản xem báo cáo: " + ex.Message;
        }
        finally
        {
            report?.Dispose();
        }
    }

    private void NotifyExportCommands()
    {
        (PreviewCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PrintCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private static void ShowWarning(string title, string message) =>
        MessageBox.Show(
            Application.Current?.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

    private static void ShowInfo(string title, string message) =>
        MessageBox.Show(
            Application.Current?.MainWindow,
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
}
