using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class SalesOldGoldPurchaseSummaryByTypeReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly ISalesOldGoldPurchaseSummaryByTypeReportService _reportService;
    private readonly SalesOldGoldPurchaseSummaryByTypePrintService _printService = new();

    private DateTime _fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _toDate = DateTime.Today;

    public SalesOldGoldPurchaseSummaryByTypeReportViewModel(
        ISalesOldGoldPurchaseSummaryByTypeReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "sales-old-gold-purchase-summary-by-type", SalesOldGoldPurchaseSummaryByTypeReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns => SalesOldGoldPurchaseSummaryByTypeReportColumns.All;

    public ObservableCollection<SalesOldGoldPurchaseSummaryByTypeRowViewModel> Rows { get; } = new();

    public DateTime FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
                ScheduleReload();
        }
    }

    public DateTime ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
                ScheduleReload();
        }
    }

    protected override bool HasPrintableData => Rows.Count > 0;

    protected override string EmptyResultMessage => "Không có dòng mua vàng cũ khớp bộ lọc.";

    protected override void ResetFiltersCore()
    {
        FromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDate = DateTime.Today;
    }

    protected override async Task LoadDataAsync(AuthenticatedSession session, CancellationToken token)
    {
        var filter = new SalesOldGoldPurchaseSummaryByTypeReportFilter(FromDate, ToDate);

        var result = await _reportService.GetAsync(filter, session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new SalesOldGoldPurchaseSummaryByTypeRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText =
            $"Tổng số lượt: {result.TotalLines.ToString("N0", Vi)}  ·  " +
            $"Tổng TL vàng: {result.TotalGoldWeight.ToString("N3", Vi)}  ·  " +
            $"Tổng thành tiền: {result.TotalAmount.ToString("N0", Vi)} VND";
    }

    protected override Report CreatePreparedReport(AuthenticatedSession session) =>
        _printService.CreatePreparedReport(
            Rows.ToArray(),
            session,
            FromDate.ToString("dd/MM/yyyy", Vi),
            ToDate.ToString("dd/MM/yyyy", Vi),
            ActiveColumns);

    protected override void OnLoadFailed() => Rows.Clear();

    protected override string BuildLoadedStatus() =>
        $"Đã tải {Rows.Count.ToString("N0", Vi)} dòng.";
}
