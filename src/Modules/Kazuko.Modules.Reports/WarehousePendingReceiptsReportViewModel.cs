using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Báo cáo "Chi tiết nhập hàng chưa xét duyệt" (reports.warehouse.pending_receipts).</summary>
public sealed class WarehousePendingReceiptsReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IWarehousePendingReceiptsReportService _reportService;
    private readonly WarehousePendingReceiptsPrintService _printService = new();

    private DateTime _fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _toDate = DateTime.Today;

    public WarehousePendingReceiptsReportViewModel(
        IWarehousePendingReceiptsReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "warehouse-pending-receipts", WarehousePendingReceiptsReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns =>
        WarehousePendingReceiptsReportColumns.All;

    public ObservableCollection<WarehousePendingReceiptsRowViewModel> Rows { get; } = new();

    public DateTime FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value.Date))
                ScheduleReload();
        }
    }

    public DateTime ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value.Date))
                ScheduleReload();
        }
    }

    protected override bool HasPrintableData => Rows.Count > 0;

    protected override string EmptyResultMessage => "Không có dòng nhập hàng chưa xét duyệt trong khoảng ngày.";

    protected override void ResetFiltersCore()
    {
        FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        ToDate = DateTime.Today;
    }

    protected override async Task LoadDataAsync(AuthenticatedSession session, CancellationToken token)
    {
        var result = await _reportService.GetAsync(
            new WarehousePendingReceiptsReportFilter(FromDate, ToDate), session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new WarehousePendingReceiptsRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText =
            $"Số phiếu: {result.DocumentCount.ToString("N0", Vi)}  ·  " +
            $"Số dòng: {result.LineCount.ToString("N0", Vi)}  ·  " +
            $"Tổng SL/TL: {result.TotalQuantity.ToString("N3", Vi)}  ·  " +
            $"Tổng thành tiền: {result.TotalAmountVnd.ToString("N0", Vi)} VND";
    }

    protected override Report CreatePreparedReport(AuthenticatedSession session) =>
        _printService.CreatePreparedReport(
            Rows.ToArray(),
            session,
            FromDate,
            ToDate,
            ActiveColumns);

    protected override void OnLoadFailed() => Rows.Clear();

    protected override string BuildLoadedStatus() =>
        $"Đã tải {Rows.Count.ToString("N0", Vi)} dòng.";
}
