using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class JewelryOnHandReportColumns
{
    public const string SettingsKey = "reports.warehouse.jewelry_on_hand.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.05f),
        new("Code", "Mã SP", 0.15f),
        new("ProductName", "Tên sản phẩm", 0.28f),
        new("GoldPurity", "Loại vàng", 0.08f),
        new("GoldWeight", "TL vàng", 0.09f),
        new("StockValue", "Giá trị", 0.12f),
        new("LocationName", "Vị trí", 0.13f),
        new("StatusDisplay", "Trạng thái", 0.10f),
        new("TotalWeight", "TL tổng", 0.09f, DefaultVisible: false),
        new("ScopeDisplay", "Kho", 0.12f, DefaultVisible: false)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[JewelryRows.Index]", HorzAlign.Center),
        "Code" => ("[JewelryRows.Code]", HorzAlign.Left),
        "ProductName" => ("[JewelryRows.ProductName]", HorzAlign.Left),
        "GoldPurity" => ("[JewelryRows.GoldPurity]", HorzAlign.Center),
        "GoldWeight" => ("[JewelryRows.GoldWeightText]", HorzAlign.Right),
        "StockValue" => ("[JewelryRows.StockValueText]", HorzAlign.Right),
        "LocationName" => ("[JewelryRows.LocationName]", HorzAlign.Left),
        "StatusDisplay" => ("[JewelryRows.StatusDisplay]", HorzAlign.Center),
        "TotalWeight" => ("[JewelryRows.TotalWeightText]", HorzAlign.Right),
        "ScopeDisplay" => ("[JewelryRows.ScopeDisplay]", HorzAlign.Left),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class JewelryOnHandPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<JewelryOnHandRowViewModel> rows,
        AuthenticatedSession session,
        string scopeFilter,
        string goldTypeFilter,
        string statusFilter,
        string searchText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new JewelryOnHandPrintRow
        {
            Index = r.Index,
            Code = r.Code,
            ProductName = r.ProductName,
            GoldPurity = r.GoldPurity,
            GoldWeightText = r.GoldWeightText,
            TotalWeightText = r.TotalWeightText,
            StockValueText = r.StockValueText,
            LocationName = r.LocationName,
            ScopeDisplay = r.ScopeDisplay,
            StatusDisplay = r.StatusDisplay,
            GoldWeight = r.Item.GoldWeight,
            StockValue = r.Item.StockValue
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : JewelryOnHandReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "JewelryRows");
        FastReportTemplateHelper.BindDataSource(report, "JewelryRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                string.IsNullOrWhiteSpace(searchText) ? null : $"Từ khóa: {searchText.Trim()}",
                FastReportTemplateHelper.FilterPart("Loại vàng", goldTypeFilter),
                FastReportTemplateHelper.FilterPart("Kho", scopeFilter),
                FastReportTemplateHelper.FilterPart("Trạng thái", statusFilter)
            ]));
        report.SetParameterValue("ItemCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalGoldWeight", printRows.Sum(x => x.GoldWeight).ToString("N3", Vi));
        report.SetParameterValue("TotalStockValue", printRows.Sum(x => x.StockValue).ToString("N0", Vi));

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
            "BÁO CÁO NỮ TRANG TỒN KHO", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = JewelryOnHandReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Tổng số món: [ItemCount]  ·  Tổng TL vàng: [TotalGoldWeight]  ·  Tổng giá trị: [TotalStockValue] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
