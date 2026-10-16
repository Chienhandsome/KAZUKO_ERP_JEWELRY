using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class FundCashReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IFundCashReportService _reportService;
    private readonly FundCashPrintService _printService = new();

    private DateTime _fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _toDate = DateTime.Today;

    public FundCashReportViewModel(
        IFundCashReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "fund-cash", FundCashReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns => FundCashReportColumns.All;

    public ObservableCollection<FundCashRowViewModel> Rows { get; } = new();

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

    protected override string EmptyResultMessage => "Không có giao dịch thu/chi tiền mặt nào trong kỳ.";

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
        var filter = new FundCashReportFilter(FromDate, ToDate);

        var result = await _reportService.GetAsync(filter, session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new FundCashRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText =
            $"Tồn đầu kỳ: {result.OpeningBalance.ToString("N0", Vi)}  ·  " +
            $"Tổng thu: {result.TotalIn.ToString("N0", Vi)}  ·  " +
            $"Tổng chi: {result.TotalOut.ToString("N0", Vi)}  ·  " +
            $"Tồn cuối kỳ: {result.ClosingBalance.ToString("N0", Vi)} VND";
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
        $"Đã tải {Rows.Count.ToString("N0", Vi)} ngày giao dịch.";
}
