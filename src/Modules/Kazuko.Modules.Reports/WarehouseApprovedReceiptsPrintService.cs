using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class WarehouseApprovedReceiptsReportColumns
{
    public const string SettingsKey = "reports.warehouse.approved_receipts.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.04f),
        new("DocumentDateText", "Ngày nhập", 0.09f),
        new("DocumentCode", "Số phiếu", 0.11f),
        new("MaterialCode", "Mã NVL", 0.10f),
        new("MaterialName", "Mặt hàng", 0.20f),
        new("GoldTypeName", "Loại vàng", 0.09f),
        new("QuantityText", "SL / TL", 0.10f),
        new("UnitPriceText", "Đơn giá", 0.10f),
        new("AmountVndText", "Thành tiền", 0.11f),
        new("SupplierName", "NCC", 0.06f, DefaultVisible: false)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[ApprovedRows.Index]", HorzAlign.Center),
        "DocumentDateText" => ("[ApprovedRows.DocumentDateText]", HorzAlign.Center),
        "DocumentCode" => ("[ApprovedRows.DocumentCode]", HorzAlign.Left),
        "MaterialCode" => ("[ApprovedRows.MaterialCode]", HorzAlign.Left),
        "MaterialName" => ("[ApprovedRows.MaterialName]", HorzAlign.Left),
        "GoldTypeName" => ("[ApprovedRows.GoldTypeName]", HorzAlign.Center),
        "QuantityText" => ("[ApprovedRows.QuantityText]", HorzAlign.Right),
        "UnitPriceText" => ("[ApprovedRows.UnitPriceText]", HorzAlign.Right),
        "AmountVndText" => ("[ApprovedRows.AmountVndText]", HorzAlign.Right),
        "SupplierName" => ("[ApprovedRows.SupplierName]", HorzAlign.Left),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class WarehouseApprovedReceiptsPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<WarehouseApprovedReceiptsRowViewModel> rows,
        AuthenticatedSession session,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new WarehouseApprovedReceiptsPrintRow
        {
            Index = r.Index,
            DocumentDateText = r.DocumentDateText,
            DocumentCode = r.DocumentCode,
            MaterialCode = r.MaterialCode,
            MaterialName = r.MaterialName,
            GoldTypeName = r.GoldTypeName,
            QuantityText = r.QuantityText,
            UnitPriceText = r.UnitPriceText,
            AmountVndText = r.AmountVndText,
            SupplierName = r.SupplierName,
            Quantity = r.Item.Quantity,
            AmountVnd = r.Item.AmountVnd
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : WarehouseApprovedReceiptsReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "ApprovedRows");
        FastReportTemplateHelper.BindDataSource(report, "ApprovedRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue(
            "FilterText",
            FastReportTemplateHelper.JoinFilterParts(
            [
                $"Từ ngày: {fromDate:dd/MM/yyyy} — Đến ngày: {toDate:dd/MM/yyyy}"
            ]));
        report.SetParameterValue("LineCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue(
            "TotalAmountVnd",
            printRows.Sum(x => x.AmountVnd).ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "LineCount", "Số dòng");
        FastReportTemplateHelper.AddStringParameter(report, "TotalAmountVnd", "Tổng thành tiền");

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
            "BÁO CÁO CHI TIẾT NHẬP HÀNG ĐÃ XÉT DUYỆT", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = WarehouseApprovedReceiptsReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Tổng số dòng: [LineCount]  ·  Tổng thành tiền: [TotalAmountVnd] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
