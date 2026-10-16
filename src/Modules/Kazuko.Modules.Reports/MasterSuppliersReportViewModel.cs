using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class MasterSuppliersReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IMasterSuppliersReportService _reportService;
    private readonly MasterSuppliersPrintService _printService = new();

    private string _searchText = string.Empty;

    public MasterSuppliersReportViewModel(
        IMasterSuppliersReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "master-suppliers", MasterSuppliersReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns => MasterSuppliersReportColumns.All;

    public ObservableCollection<MasterSuppliersRowViewModel> Rows { get; } = new();

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ScheduleReload(350);
        }
    }

    protected override bool HasPrintableData => Rows.Count > 0;

    protected override string EmptyResultMessage => "Không có nhà cung cấp nào khớp bộ lọc.";

    protected override void OnLoadFailed() => Rows.Clear();

    protected override void ResetFiltersCore()
    {
        SearchText = string.Empty;
    }

    protected override async Task LoadDataAsync(AuthenticatedSession session, CancellationToken token)
    {
        var filter = new MasterSuppliersReportFilter(SearchText);

        var result = await _reportService.GetAsync(filter, session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new MasterSuppliersRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText = $"Số nhà cung cấp: {result.ItemCount.ToString("N0", Vi)}";
    }

    protected override Report CreatePreparedReport(AuthenticatedSession session) =>
        _printService.CreatePreparedReport(
            Rows.ToArray(),
            session,
            SearchText,
            ActiveColumns);

    protected override string BuildLoadedStatus() =>
        $"Đã tải {Rows.Count.ToString("N0", Vi)} dòng.";
}
