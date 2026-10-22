using System.Collections.ObjectModel;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public sealed class MasterCustomersReportViewModel : ReportWorkspaceViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IMasterCustomersReportService _reportService;
    private readonly MasterCustomersPrintService _printService = new();

    private string _selectedKind = "Có hồ sơ";
    public string[] KindOptions { get; } = ["Có hồ sơ", "Vãng lai", "Tất cả"];
    public string SelectedKind
    {
        get => _selectedKind;
        set { if (SetProperty(ref _selectedKind, value)) ScheduleReload(350); }
    }

    private string _searchText = string.Empty;

    public MasterCustomersReportViewModel(
        IMasterCustomersReportService reportService,
        ICurrentSessionAccessor sessionAccessor)
        : base(sessionAccessor, "master-customers", MasterCustomersReportColumns.SettingsKey)
    {
        _reportService = reportService;
    }

    protected override IReadOnlyList<ReportColumnDefinition> AvailableColumns => MasterCustomersReportColumns.All;

    public ObservableCollection<MasterCustomersRowViewModel> Rows { get; } = new();

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

    protected override string EmptyResultMessage => "Không có khách hàng nào khớp bộ lọc.";

    protected override void ResetFiltersCore()
    {
        SearchText = string.Empty;
        SelectedKind = "Có hồ sơ";
    }

    protected override async Task LoadDataAsync(AuthenticatedSession session, CancellationToken token)
    {
        var filter = new MasterCustomersReportFilter(SearchText, SelectedKind switch { "Có hồ sơ" => false, "Vãng lai" => true, _ => (bool?)null });

        var result = await _reportService.GetAsync(filter, session, token);
        token.ThrowIfCancellationRequested();

        Rows.Clear();
        var index = 1;
        foreach (var row in result.Rows)
            Rows.Add(new MasterCustomersRowViewModel(row, index++));

        IsEmpty = Rows.Count == 0;
        SummaryText =
            $"Số khách hàng: {result.ItemCount.ToString("N0", Vi)}  ·  " +
            $"Tổng hạn mức công nợ: {result.TotalCreditLimit.ToString("N0", Vi)} VND";
    }

    protected override Report CreatePreparedReport(AuthenticatedSession session) =>
        _printService.CreatePreparedReport(
            Rows.ToArray(),
            session,
            SearchText,
            ActiveColumns, SelectedKind);

    protected override void OnLoadFailed() => Rows.Clear();

    protected override string BuildLoadedStatus() =>
        $"Đã tải {Rows.Count.ToString("N0", Vi)} dòng.";
}
