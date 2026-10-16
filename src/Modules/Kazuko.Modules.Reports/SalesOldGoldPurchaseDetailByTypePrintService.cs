using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Định nghĩa cột + dựng FastReport động theo layout cột người dùng.</summary>
public static class SalesOldGoldPurchaseDetailByTypeReportColumns
{
    public const string SettingsKey = "reports.sales.old_gold_purchase_detail_by_type.columns";

    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index", "STT", 0.05f),
        new("GoldType", "Loại vàng", 0.14f),
        new("PurchaseDateText", "Ngày", 0.10f),
        new("DocumentCode", "Số phiếu", 0.16f),
        new("GoldWeight", "TL vàng", 0.14f),
        new("UnitPrice", "Đơn giá", 0.20f),
        new("Amount", "Thành tiền", 0.21f)
    ];

    public static (string Expression, HorzAlign Align) Bind(string key) => key switch
    {
        "Index" => ("[OldGoldPurchaseDetailRows.Index]", HorzAlign.Center),
        "GoldType" => ("[OldGoldPurchaseDetailRows.GoldType]", HorzAlign.Left),
        "PurchaseDateText" => ("[OldGoldPurchaseDetailRows.PurchaseDateText]", HorzAlign.Center),
        "DocumentCode" => ("[OldGoldPurchaseDetailRows.DocumentCode]", HorzAlign.Left),
        "GoldWeight" => ("[OldGoldPurchaseDetailRows.GoldWeightText]", HorzAlign.Right),
        "UnitPrice" => ("[OldGoldPurchaseDetailRows.UnitPriceText]", HorzAlign.Right),
        "Amount" => ("[OldGoldPurchaseDetailRows.AmountText]", HorzAlign.Right),
        _ => ("", HorzAlign.Left)
    };
}

public sealed class SalesOldGoldPurchaseDetailByTypePrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public Report CreatePreparedReport(
        IReadOnlyList<SalesOldGoldPurchaseDetailByTypeRowViewModel> rows,
        AuthenticatedSession session,
        string fromDateText,
        string toDateText,
        IReadOnlyList<ReportColumnDefinition> activeColumns)
    {
        var printRows = rows.Select(r => new SalesOldGoldPurchaseDetailByTypePrintRow
        {
            Index = r.Index,
            GoldType = r.GoldType,
            PurchaseDateText = r.PurchaseDateText,
            DocumentCode = r.DocumentCode,
            GoldWeightText = r.GoldWeightText,
            UnitPriceText = r.UnitPriceText,
            AmountText = r.AmountText,
            GoldWeight = r.Item.GoldWeight,
            Amount = r.Item.Amount
        }).ToList();

        var columns = activeColumns.Count > 0 ? activeColumns : SalesOldGoldPurchaseDetailByTypeReportColumns.All
            .Where(c => c.DefaultVisible)
            .ToArray();

        var report = BuildReport(columns);
        report.RegisterData(printRows, "OldGoldPurchaseDetailRows");
        FastReportTemplateHelper.BindDataSource(report, "OldGoldPurchaseDetailRows");

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
        report.SetParameterValue("LineCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalGoldWeight", printRows.Sum(x => x.GoldWeight).ToString("N3", Vi));
        report.SetParameterValue("TotalAmount", printRows.Sum(x => x.Amount).ToString("N0", Vi));

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
        FastReportTemplateHelper.AddStringParameter(report, "TotalGoldWeight", "Tổng TL vàng");
        FastReportTemplateHelper.AddStringParameter(report, "TotalAmount", "Tổng thành tiền");

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
            "CHI TIẾT MUA VÀNG CŨ THEO LOẠI VÀNG", 14, FontStyle.Bold, HorzAlign.Center));
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
            var (expr, align) = SalesOldGoldPurchaseDetailByTypeReportColumns.Bind(c.Key);
            return (c.Header, expr, align, c.WidthWeight);
        }).ToArray();
        FastReportTemplateHelper.AddTableColumns(pageHeader, dataBand, contentWidth, rowHeight, tableColumns);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Số dòng: [LineCount]  ·  Tổng TL vàng: [TotalGoldWeight]  ·  Tổng thành tiền: [TotalAmount] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));

        FastReportTemplateHelper.AssignPageBands(page, titleBand, pageHeader, dataBand, footerBand);

        return report;
    }
}
