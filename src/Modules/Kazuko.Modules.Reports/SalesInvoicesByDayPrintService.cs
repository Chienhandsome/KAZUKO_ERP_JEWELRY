using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class SalesInvoicesByDayReportColumns
{
    public const string SettingsKey = "reports.sales.invoices_by_day.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.05f),
        new("DocumentDateText", "Ngày", 0.14f),
        new("InvoiceCountText", "Số HĐ", 0.10f),
        new("TotalRevenue", "Tổng doanh thu", 0.24f),
        new("TotalCost", "Tổng giá vốn", 0.24f),
        new("Profit", "Lãi", 0.23f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[InvoicesByDayRows.Index]", HorzAlign.Center),
        "DocumentDateText" => ("[InvoicesByDayRows.DocumentDateText]", HorzAlign.Center),
        "InvoiceCountText" => ("[InvoicesByDayRows.InvoiceCountText]", HorzAlign.Center),
        "TotalRevenue" => ("[InvoicesByDayRows.TotalRevenueText]", HorzAlign.Right),
        "TotalCost" => ("[InvoicesByDayRows.TotalCostText]", HorzAlign.Right),
        "Profit" => ("[InvoicesByDayRows.ProfitText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class SalesInvoicesByDayPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<SalesInvoicesByDayRowViewModel> rows,
        AuthenticatedSession session,
        string fromDateText,
        string toDateText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new SalesInvoicesByDayPrintRow
        {
            Index = r.Index,
            DocumentDateText = r.DocumentDateText,
            InvoiceCountText = r.InvoiceCountText,
            TotalRevenueText = r.TotalRevenueText,
            TotalCostText = r.TotalCostText,
            ProfitText = r.ProfitText,
            TotalRevenue = r.Item.TotalRevenue,
            TotalCost = r.Item.TotalCost,
            Profit = r.Item.Profit
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : SalesInvoicesByDayReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "InvoicesByDayRows");
        FastReportTemplateHelper.BindDataSource(report, "InvoicesByDayRows");

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
        report.SetParameterValue("TotalRevenue", printRows.Sum(x => x.TotalRevenue).ToString("N0", Vi));
        report.SetParameterValue("TotalCost", printRows.Sum(x => x.TotalCost).ToString("N0", Vi));
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
        FastReportTemplateHelper.AddStringParameter(report, "TotalRevenue", "Tổng doanh thu");
        FastReportTemplateHelper.AddStringParameter(report, "TotalCost", "Tổng giá vốn");
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
            "TỔNG HỢP HÓA ĐƠN BÁN HÀNG THEO NGÀY", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = SalesInvoicesByDayReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Tổng doanh thu: [TotalRevenue]  ·  Tổng giá vốn: [TotalCost]  ·  Tổng lãi: [TotalProfit] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
