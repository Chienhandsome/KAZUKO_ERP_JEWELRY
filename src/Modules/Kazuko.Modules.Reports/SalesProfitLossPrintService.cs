using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class SalesProfitLossReportColumns
{
    public const string SettingsKey = "reports.sales.profit_loss.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.04f),
        new("OrderDateText", "Ngày", 0.09f),
        new("OrderCode", "Số phiếu", 0.14f),
        new("ProductName", "Mặt hàng", 0.26f),
        new("GoldType", "Loại vàng", 0.08f),
        new("CostAmount", "Giá nhập (vốn)", 0.13f),
        new("SaleAmount", "Giá bán", 0.13f),
        new("Profit", "Lãi", 0.13f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[ProfitLossRows.Index]", HorzAlign.Center),
        "OrderDateText" => ("[ProfitLossRows.OrderDateText]", HorzAlign.Center),
        "OrderCode" => ("[ProfitLossRows.OrderCode]", HorzAlign.Left),
        "ProductName" => ("[ProfitLossRows.ProductName]", HorzAlign.Left),
        "GoldType" => ("[ProfitLossRows.GoldType]", HorzAlign.Center),
        "CostAmount" => ("[ProfitLossRows.CostAmountText]", HorzAlign.Right),
        "SaleAmount" => ("[ProfitLossRows.SaleAmountText]", HorzAlign.Right),
        "Profit" => ("[ProfitLossRows.ProfitText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class SalesProfitLossPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<SalesProfitLossRowViewModel> rows,
        AuthenticatedSession session,
        string fromDateText,
        string toDateText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new SalesProfitLossPrintRow
        {
            Index = r.Index,
            OrderDateText = r.OrderDateText,
            OrderCode = r.OrderCode,
            ProductName = r.ProductName,
            GoldType = r.GoldType,
            CostAmountText = r.CostAmountText,
            SaleAmountText = r.SaleAmountText,
            ProfitText = r.ProfitText,
            CostAmount = r.Item.CostAmount,
            SaleAmount = r.Item.SaleAmount,
            Profit = r.Item.Profit
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : SalesProfitLossReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "ProfitLossRows");
        FastReportTemplateHelper.BindDataSource(report, "ProfitLossRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                $"Từ ngày: {fromDateText}",
                $"Đến ngày: {toDateText}"
            ]));
        report.SetParameterValue("ItemCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalCost", printRows.Sum(x => x.CostAmount).ToString("N0", Vi));
        report.SetParameterValue("TotalSale", printRows.Sum(x => x.SaleAmount).ToString("N0", Vi));
        report.SetParameterValue("TotalProfit", printRows.Sum(x => x.Profit).ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "ItemCount", "Số dòng");
        FastReportTemplateHelper.AddStringParameter(report, "TotalCost", "Tổng giá vốn");
        FastReportTemplateHelper.AddStringParameter(report, "TotalSale", "Tổng giá bán");
        FastReportTemplateHelper.AddStringParameter(report, "TotalProfit", "Tổng lãi");

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
            "TỔNG HỢP LÃI LỖ BÁN HÀNG", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = SalesProfitLossReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Số dòng: [ItemCount]  ·  Tổng giá vốn: [TotalCost]  ·  Tổng giá bán: [TotalSale]  ·  Tổng lãi: [TotalProfit] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
