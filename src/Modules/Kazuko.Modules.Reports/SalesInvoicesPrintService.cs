using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class SalesInvoicesReportColumns
{
    public const string SettingsKey = "reports.sales.invoices.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.05f),
        new("Code", "Số HĐ", 0.16f),
        new("DocumentDateText", "Ngày", 0.10f),
        new("CustomerName", "Khách hàng", 0.27f),
        new("TotalAmount", "Tổng tiền", 0.14f),
        new("PaidAmount", "Đã thu", 0.14f),
        new("RemainingAmount", "Còn lại", 0.14f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[InvoicesRows.Index]", HorzAlign.Center),
        "Code" => ("[InvoicesRows.Code]", HorzAlign.Left),
        "DocumentDateText" => ("[InvoicesRows.DocumentDateText]", HorzAlign.Center),
        "CustomerName" => ("[InvoicesRows.CustomerName]", HorzAlign.Left),
        "TotalAmount" => ("[InvoicesRows.TotalAmountText]", HorzAlign.Right),
        "PaidAmount" => ("[InvoicesRows.PaidAmountText]", HorzAlign.Right),
        "RemainingAmount" => ("[InvoicesRows.RemainingAmountText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class SalesInvoicesPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<SalesInvoicesRowViewModel> rows,
        AuthenticatedSession session,
        string fromDateText,
        string toDateText,
        string customerText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new SalesInvoicesPrintRow
        {
            Index = r.Index,
            Code = r.Code,
            DocumentDateText = r.DocumentDateText,
            CustomerName = r.CustomerName,
            TotalAmountText = r.TotalAmountText,
            PaidAmountText = r.PaidAmountText,
            RemainingAmountText = r.RemainingAmountText,
            TotalAmount = r.Item.TotalAmount,
            PaidAmount = r.Item.PaidAmount,
            RemainingAmount = r.Item.RemainingAmount
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : SalesInvoicesReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "InvoicesRows");
        FastReportTemplateHelper.BindDataSource(report, "InvoicesRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                $"Từ ngày: {fromDateText}",
                $"Đến ngày: {toDateText}",
                FastReportTemplateHelper.FilterPart("Khách hàng", customerText)
            ]));
        report.SetParameterValue("InvoiceCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalAmount", printRows.Sum(x => x.TotalAmount).ToString("N0", Vi));
        report.SetParameterValue("TotalPaid", printRows.Sum(x => x.PaidAmount).ToString("N0", Vi));
        report.SetParameterValue("TotalRemaining", printRows.Sum(x => x.RemainingAmount).ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "InvoiceCount", "Số hóa đơn");
        FastReportTemplateHelper.AddStringParameter(report, "TotalAmount", "Tổng tiền");
        FastReportTemplateHelper.AddStringParameter(report, "TotalPaid", "Tổng đã thu");
        FastReportTemplateHelper.AddStringParameter(report, "TotalRemaining", "Tổng còn lại");

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
            "DANH SÁCH HÓA ĐƠN BÁN HÀNG", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = SalesInvoicesReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Số hóa đơn: [InvoiceCount]  ·  Tổng tiền: [TotalAmount]  ·  Đã thu: [TotalPaid]  ·  Còn lại: [TotalRemaining] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
