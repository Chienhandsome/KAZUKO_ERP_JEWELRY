using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class FundBalanceReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IFundBalanceReportService _reportService;
    private readonly FundBalancePrintService _printService = new();

    private DateTime _fromDate = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _toDate = DateTime.Today;

    public FundBalanceReportViewModel(
        IFundBalanceReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "fund-balance", FundBalanceReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns => FundBalanceReportColumns.All;

    public ObservableCollection<FundBalanceRowViewModel> Rows { get; } = new();

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

    protected override string EmptyResultMessage => "Không có quỹ tiền mặt / tài khoản ngân hàng nào.";

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
        var filter = new FundBalanceReportFilter(FromDate, ToDate);

        var result = await _reportService.GetAsync(filter, session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new FundBalanceRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText =
            $"Số quỹ/TK: {Rows.Count.ToString("N0", Vi)}  ·  " +
            $"Tồn đầu kỳ: {result.TotalOpening.ToString("N0", Vi)}  ·  " +
            $"Thu trong kỳ: {result.TotalIn.ToString("N0", Vi)}  ·  " +
            $"Chi trong kỳ: {result.TotalOut.ToString("N0", Vi)}  ·  " +
            $"Tồn cuối kỳ: {result.TotalClosing.ToString("N0", Vi)} VND";
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
        $"Đã tải {Rows.Count.ToString("N0", Vi)} quỹ / tài khoản.";
}
