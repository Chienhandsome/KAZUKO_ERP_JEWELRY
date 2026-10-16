using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class FundCashReportColumns
{
    public const string SettingsKey = "reports.fund.cash.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.07f),
        new("DayText", "Ngày", 0.18f),
        new("TotalInText", "Thu", 0.22f),
        new("TotalOutText", "Chi", 0.22f),
        new("ClosingBalanceText", "Tồn cuối ngày", 0.26f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[CashRows.Index]", HorzAlign.Center),
        "DayText" => ("[CashRows.DayText]", HorzAlign.Center),
        "TotalInText" => ("[CashRows.TotalInText]", HorzAlign.Right),
        "TotalOutText" => ("[CashRows.TotalOutText]", HorzAlign.Right),
        "ClosingBalanceText" => ("[CashRows.ClosingBalanceText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class FundCashPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<FundCashRowViewModel> rows,
        AuthenticatedSession session,
        string fromDateText,
        string toDateText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new FundCashPrintRow
        {
            Index = r.Index,
            DayText = r.DayText,
            TotalInText = r.TotalInText,
            TotalOutText = r.TotalOutText,
            ClosingBalanceText = r.ClosingBalanceText,
            TotalIn = r.Item.TotalIn,
            TotalOut = r.Item.TotalOut,
            ClosingBalance = r.Item.ClosingBalance
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : FundCashReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "CashRows");
        FastReportTemplateHelper.BindDataSource(report, "CashRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue("FilterText", $"Từ ngày {fromDateText} đến {toDateText}");
        report.SetParameterValue("ItemCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalIn", printRows.Sum(x => x.TotalIn).ToString("N0", Vi));
        report.SetParameterValue("TotalOut", printRows.Sum(x => x.TotalOut).ToString("N0", Vi));
        report.SetParameterValue(
            "TotalClosing",
            (printRows.Count > 0 ? printRows[^1].ClosingBalance : 0m).ToString("N0", Vi));

        report.Prepare();
        return report;
    }

    private static Report BuildReport(IReadOnlyList<ReportColumnDefinition> columns)
    {
        var report = new Report();
        FastReportTemplateHelper.AddStringParameter(report, "BranchName", "Chi nhánh");
        FastReportTemplateHelper.AddStringParameter(report, "PrintedAt", "Ngày in");
        FastReportTemplateHelper.AddStringParameter(report, "PrintedBy", "Người in");
        FastReportTemplateHelper.AddStringParameter(report, "FilterText", "Bộ lọc");
        FastReportTemplateHelper.AddStringParameter(report, "ItemCount", "Số ngày");
        FastReportTemplateHelper.AddStringParameter(report, "TotalIn", "Tổng thu");
        FastReportTemplateHelper.AddStringParameter(report, "TotalOut", "Tổng chi");
        FastReportTemplateHelper.AddStringParameter(report, "TotalClosing", "Tồn cuối kỳ");

        var page = new ReportPage
        {
            Name = "Page1",
            PaperWidth = 210,
            PaperHeight = 297,
            LeftMargin = 10,
            TopMargin = 10,
            RightMargin = 10,
            BottomMargin = 10
        };
        report.Pages.Add(page);

        var contentWidth = FastReportTemplateHelper.Mm(190);
        var rowHeight = FastReportTemplateHelper.Mm(7);

        var titleBand = new ReportTitleBand { Name = "ReportTitle1", Height = FastReportTemplateHelper.Mm(28) };
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(0), contentWidth, FastReportTemplateHelper.Mm(8),
            "BÁO CÁO QUỸ TIỀN MẶT", 14, FontStyle.Bold, HorzAlign.Center));
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(9), contentWidth, FastReportTemplateHelper.Mm(5),
            "[BranchName]", 10, FontStyle.Regular, HorzAlign.Center));
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(14), contentWidth, FastReportTemplateHelper.Mm(5),
            "Ngày in: [PrintedAt]  ·  Người in: [PrintedBy]", 9, FontStyle.Regular, HorzAlign.Center));
        titleBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(20), contentWidth, FastReportTemplateHelper.Mm(5),
            "Bộ lọc: [FilterText]", 9, FontStyle.Regular, HorzAlign.Left));

        var pageHeader = new PageHeaderBand { Name = "PageHeader1", Height = rowHeight };
        var dataBand = new DataBand { Name = "Data1", Height = rowHeight };

        var tableColumns = columns.Select(c =>
        {
            var (expr, align) = FundCashReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Số ngày có giao dịch: [ItemCount]  ·  Tổng thu: [TotalIn]  ·  Tổng chi: [TotalOut]  ·  Tồn cuối kỳ: [TotalClosing] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ quỹ: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
