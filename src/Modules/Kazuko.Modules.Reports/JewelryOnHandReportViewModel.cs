using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class JewelryOnHandReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IJewelryOnHandReportService _reportService;
    private readonly JewelryOnHandPrintService _printService = new();

    private string _searchText = string.Empty;
    private string _selectedGoldType = "Tất cả loại vàng";
    private string _selectedStatus = "Tất cả trạng thái";
    private string _selectedScope = "Tất cả kho";

    public JewelryOnHandReportViewModel(
        IJewelryOnHandReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "jewelry-on-hand")
    {
        _reportService = reportService;

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
    }

    public ObservableCollection<JewelryOnHandRowViewModel> Rows { get; } = new();
    public ObservableCollection<string> GoldTypeOptions { get; } = new();
    public ObservableCollection<string> StatusOptions { get; } = new();
    public ObservableCollection<string> ScopeOptions { get; } = new();

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

    protected override bool HasPrintableData => Rows.Count > 0;

    protected override string EmptyResultMessage => "Không có nữ trang tồn kho khớp bộ lọc.";

    protected override void ResetFiltersCore()
    {
        SearchText = string.Empty;
        SelectedGoldType = "Tất cả loại vàng";
        SelectedStatus = "Tất cả trạng thái";
        SelectedScope = "Tất cả kho";
    }

    protected override async Task LoadDataAsync(AuthenticatedSession session, CancellationToken token)
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
    }

    protected override Report CreatePreparedReport(AuthenticatedSession session) =>
        _printService.CreatePreparedReport(
            Rows.ToArray(),
            session,
            SelectedScope,
            SelectedGoldType,
            SelectedStatus,
            SearchText);

    protected override void OnLoadFailed() => Rows.Clear();

    protected override string BuildLoadedStatus() =>
        $"Đã tải {Rows.Count.ToString("N0", Vi)} dòng.";

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
