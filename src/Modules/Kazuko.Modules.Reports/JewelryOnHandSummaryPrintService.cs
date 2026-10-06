using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public static class JewelryOnHandSummaryReportColumns
{
    public const string SettingsKey = "reports.warehouse.jewelry_on_hand_summary.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.06f),
        new("ScopeDisplay", "Kho", 0.22f),
        new("GoldPurity", "Loại vàng", 0.14f),
        new("ItemCount", "Số món", 0.12f),
        new("GoldWeight", "TL vàng", 0.15f),
        new("TotalWeight", "TL tổng", 0.15f),
        new("StockValue", "Giá trị", 0.16f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[SummaryRows.Index]", HorzAlign.Center),
        "ScopeDisplay" => ("[SummaryRows.ScopeDisplay]", HorzAlign.Left),
        "GoldPurity" => ("[SummaryRows.GoldPurity]", HorzAlign.Center),
        "ItemCount" => ("[SummaryRows.ItemCountText]", HorzAlign.Right),
        "GoldWeight" => ("[SummaryRows.GoldWeightText]", HorzAlign.Right),
        "TotalWeight" => ("[SummaryRows.TotalWeightText]", HorzAlign.Right),
        "StockValue" => ("[SummaryRows.StockValueText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class JewelryOnHandSummaryPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<JewelryOnHandSummaryRowViewModel> rows,
        AuthenticatedSession session,
        string scopeFilter,
        string goldTypeFilter,
        string statusFilter,
        int itemCount,
        decimal totalGoldWeight,
        decimal totalStockValue,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new JewelryOnHandSummaryPrintRow
        {
            Index = r.Index,
            ScopeDisplay = r.ScopeDisplay,
            GoldPurity = r.GoldPurity,
            ItemCountText = r.ItemCountText,
            GoldWeightText = r.GoldWeightText,
            TotalWeightText = r.TotalWeightText,
            StockValueText = r.StockValueText,
            ItemCount = r.Item.ItemCount,
            GoldWeight = r.Item.TotalGoldWeight,
            StockValue = r.Item.TotalStockValue
        }).ToList();

        var columns = activeColumns.Count > 0
            ? activeColumns
            : JewelryOnHandSummaryReportColumns.All.Where(c => c.DefaultVisible).ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "SummaryRows");
        FastReportTemplateHelper.BindDataSource(report, "SummaryRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                FastReportTemplateHelper.FilterPart("Loại vàng", goldTypeFilter),
                FastReportTemplateHelper.FilterPart("Kho", scopeFilter),
                FastReportTemplateHelper.FilterPart("Trạng thái", statusFilter)
            ]));
        report.SetParameterValue("GroupCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("ItemCount", itemCount.ToString("N0", Vi));
        report.SetParameterValue("TotalGoldWeight", totalGoldWeight.ToString("N3", Vi));
        report.SetParameterValue("TotalStockValue", totalStockValue.ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "GroupCount", "Số nhóm");
        FastReportTemplateHelper.AddStringParameter(report, "ItemCount", "Số món");
        FastReportTemplateHelper.AddStringParameter(report, "TotalGoldWeight", "Tổng TL vàng");
        FastReportTemplateHelper.AddStringParameter(report, "TotalStockValue", "Tổng giá trị");

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
            "TỔNG HỢP NỮ TRANG TỒN KHO", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = JewelryOnHandSummaryReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Số nhóm: [GroupCount]  ·  Tổng số món: [ItemCount]  ·  Tổng TL vàng: [TotalGoldWeight]  ·  Tổng giá trị: [TotalStockValue] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
