using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

/// <summary>Mẫu FastReport riêng cho báo cáo nữ trang tồn kho.</summary>
public sealed class JewelryOnHandPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private const string TemplateFileName = "jewelry_on_hand_v3.frx";

    public Report CreatePreparedReport(
        IReadOnlyList<JewelryOnHandRowViewModel> rows,
        AuthenticatedSession session,
        string scopeFilter,
        string goldTypeFilter,
        string statusFilter,
        string searchText)
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

        var templatePath = FastReportTemplateHelper.GetTemplatePath(TemplateFileName);
        FastReportTemplateHelper.EnsureTemplate(templatePath, BuildDefaultTemplate);

        var report = new Report();
        report.Load(templatePath);
        report.RegisterData(printRows, "JewelryRows");
        FastReportTemplateHelper.BindDataSource(report, "JewelryRows");

        report.SetParameterValue("BranchName", $"{session.BranchName} ({session.BranchCode})");
        report.SetParameterValue("PrintedAt", DateTime.Now.ToString("dd/MM/yyyy HH:mm", Vi));
        report.SetParameterValue("PrintedBy", session.DisplayName);
        report.SetParameterValue("FilterText", BuildFilterText(scopeFilter, goldTypeFilter, statusFilter, searchText));
        report.SetParameterValue("ItemCount", printRows.Count.ToString("N0", Vi));
        report.SetParameterValue("TotalGoldWeight", printRows.Sum(x => x.GoldWeight).ToString("N3", Vi));
        report.SetParameterValue("TotalStockValue", printRows.Sum(x => x.StockValue).ToString("N0", Vi));

        report.Prepare();
        return report;
    }

    private static string BuildFilterText(
        string scopeFilter,
        string goldTypeFilter,
        string statusFilter,
        string searchText) =>
        FastReportTemplateHelper.JoinFilterParts(
        [
            string.IsNullOrWhiteSpace(searchText) ? null : $"Từ khóa: {searchText.Trim()}",
            FastReportTemplateHelper.FilterPart("Loại vàng", goldTypeFilter),
            FastReportTemplateHelper.FilterPart("Kho", scopeFilter),
            FastReportTemplateHelper.FilterPart("Trạng thái", statusFilter)
        ]);

    private static void BuildDefaultTemplate(Report report)
    {
        report.Pages.Clear();
        report.Parameters.Clear();

        report.RegisterData(new List<JewelryOnHandPrintRow>(), "JewelryRows");
        var ds = report.GetDataSource("JewelryRows");
        if (ds is not null)
            ds.Enabled = true;

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
        var titleHeight = FastReportTemplateHelper.Mm(28);
        var rowHeight = FastReportTemplateHelper.Mm(7);

        var titleBand = new ReportTitleBand { Name = "ReportTitle1", Height = titleHeight };
        page.Bands.Add(titleBand);
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
        page.Bands.Add(pageHeader);
        AddHeaderCells(pageHeader, contentWidth, rowHeight);

        var dataBand = new DataBand { Name = "Data1", Height = rowHeight };
        page.Bands.Add(dataBand);
        AddDataCells(dataBand, contentWidth, rowHeight);
        FastReportTemplateHelper.BindDataSource(report, "JewelryRows");

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = FastReportTemplateHelper.Mm(20) };
        page.Bands.Add(footerBand);
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(2), contentWidth, FastReportTemplateHelper.Mm(6),
            "Tổng số món: [ItemCount]  ·  Tổng TL vàng: [TotalGoldWeight]  ·  Tổng giá trị: [TotalStockValue] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(FastReportTemplateHelper.CreateText(
            0, FastReportTemplateHelper.Mm(12), contentWidth, FastReportTemplateHelper.Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));
    }

    private static void AddHeaderCells(BandBase band, float contentWidth, float rowHeight)
    {
        float[] ratios = [0.05f, 0.15f, 0.28f, 0.08f, 0.09f, 0.12f, 0.13f, 0.10f];
        string[] titles = ["STT", "Mã SP", "Tên sản phẩm", "Loại vàng", "TL vàng", "Giá trị", "Vị trí", "Trạng thái"];
        float x = 0;
        for (var i = 0; i < titles.Length; i++)
        {
            var w = contentWidth * ratios[i];
            var cell = FastReportTemplateHelper.CreateText(x, 0, w, rowHeight, titles[i], 8, FontStyle.Bold, HorzAlign.Center);
            cell.Border.Lines = BorderLines.All;
            cell.Fill = new SolidFill(Color.WhiteSmoke);
            cell.CanGrow = false;
            band.Objects.Add(cell);
            x += w;
        }
    }

    private static void AddDataCells(DataBand band, float contentWidth, float rowHeight)
    {
        float[] ratios = [0.05f, 0.15f, 0.28f, 0.08f, 0.09f, 0.12f, 0.13f, 0.10f];
        string[] exprs =
        [
            "[JewelryRows.Index]",
            "[JewelryRows.Code]",
            "[JewelryRows.ProductName]",
            "[JewelryRows.GoldPurity]",
            "[JewelryRows.GoldWeightText]",
            "[JewelryRows.StockValueText]",
            "[JewelryRows.LocationName]",
            "[JewelryRows.StatusDisplay]"
        ];
        HorzAlign[] aligns =
        [
            HorzAlign.Center, HorzAlign.Left, HorzAlign.Left, HorzAlign.Center,
            HorzAlign.Right, HorzAlign.Right, HorzAlign.Left, HorzAlign.Center
        ];

        float x = 0;
        for (var i = 0; i < exprs.Length; i++)
        {
            var w = contentWidth * ratios[i];
            var cell = FastReportTemplateHelper.CreateText(x, 0, w, rowHeight, exprs[i], 8, FontStyle.Regular, aligns[i]);
            cell.Border.Lines = BorderLines.All;
            cell.CanGrow = false;
            cell.WordWrap = true;
            band.Objects.Add(cell);
            x += w;
        }
    }
}
