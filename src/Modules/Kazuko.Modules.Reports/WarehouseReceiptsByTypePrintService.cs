using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class WarehouseReceiptsByTypeReportColumns
{
    public const string SettingsKey = "reports.warehouse.receipts_by_type.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.06f),
        new("TypeName", "Loại vàng", 0.30f),
        new("DocumentCountText", "Số phiếu", 0.14f),
        new("LineCountText", "Số dòng", 0.14f),
        new("TotalQuantityText", "Tổng SL / TL", 0.17f),
        new("TotalAmountVndText", "Tổng giá trị", 0.19f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[ByTypeRows.Index]", HorzAlign.Center),
        "TypeName" => ("[ByTypeRows.TypeName]", HorzAlign.Left),
        "DocumentCountText" => ("[ByTypeRows.DocumentCountText]", HorzAlign.Right),
        "LineCountText" => ("[ByTypeRows.LineCountText]", HorzAlign.Right),
        "TotalQuantityText" => ("[ByTypeRows.TotalQuantityText]", HorzAlign.Right),
        "TotalAmountVndText" => ("[ByTypeRows.TotalAmountVndText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class WarehouseReceiptsByTypePrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<WarehouseReceiptsByTypeRowViewModel> rows,
        AuthenticatedSession session,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new WarehouseReceiptsByTypePrintRow
        {
            Index = r.Index,
            TypeName = r.TypeName,
            DocumentCountText = r.DocumentCountText,
            LineCountText = r.LineCountText,
            TotalQuantityText = r.TotalQuantityText,
            TotalAmountVndText = r.TotalAmountVndText,
            DocumentCount = r.Item.DocumentCount,
            LineCount = r.Item.LineCount,
            TotalQuantity = r.Item.TotalQuantity,
            TotalAmountVnd = r.Item.TotalAmountVnd
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : WarehouseReceiptsByTypeReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "ByTypeRows");
        FastReportTemplateHelper.BindDataSource(report, "ByTypeRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                $"Từ ngày: {fromDate:dd/MM/yyyy} — Đến ngày: {toDate:dd/MM/yyyy}"
            ]));
        report.SetParameterValue("TypeCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue(
            "TotalAmountVnd",
            printRows.Sum(x => x.TotalAmountVnd).ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "TypeCount", "Số loại");
        FastReportTemplateHelper.AddStringParameter(report, "TotalAmountVnd", "Tổng giá trị");

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
            "BÁO CÁO TỔNG HỢP NHẬP HÀNG THEO LOẠI", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = WarehouseReceiptsByTypeReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Tổng số loại: [TypeCount]  ·  Tổng giá trị: [TotalAmountVnd] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
