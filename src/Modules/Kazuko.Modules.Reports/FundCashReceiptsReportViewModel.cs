using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class FundCashReceiptsReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IFundCashReceiptsReportService _reportService;
    private readonly FundCashReceiptsPrintService _printService = new();

    private DateTime _fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _toDate = DateTime.Today;

    public FundCashReceiptsReportViewModel(
        IFundCashReceiptsReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "fund-cash-receipts", FundCashReceiptsReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns => FundCashReceiptsReportColumns.All;

    public ObservableCollection<FundCashReceiptsRowViewModel> Rows { get; } = new();

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

    protected override string EmptyResultMessage => "Không có phiếu thu tiền mặt nào trong kỳ.";

    protected override void ResetFiltersCore()
    {
        RunWithoutFilterReload(() =>
        {
            _fromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            _toDate = DateTime.Today;
            OnPropertyChanged(nameof(FromDate));
            OnPropertyChanged(nameof(ToDate));
        });
    }

    protected override async Task LoadDataAsync(AuthenticatedSession session, CancellationToken token)
    {
        var filter = new FundCashReceiptsReportFilter(FromDate, ToDate);

        var result = await _reportService.GetAsync(filter, session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new FundCashReceiptsRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText =
            $"Số phiếu: {result.ItemCount.ToString("N0", Vi)}  ·  " +
            $"Tổng tiền thu: {result.TotalAmount.ToString("N0", Vi)} VND";
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
        $"Đã tải {Rows.Count.ToString("N0", Vi)} phiếu thu.";
}
