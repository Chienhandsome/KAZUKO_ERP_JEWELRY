using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using FastReport;
using FastReport.Export.PdfSimple;
using FastReport.Utils;
using Kazuko.Core.Authentication;

namespace Kazuko.Modules.Reports;

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

        var report = new Report();
        var templatePath = GetTemplatePath();
        EnsureTemplate(templatePath);

        report.Load(templatePath);
        report.RegisterData(printRows, "JewelryRows");
        var dataSource = report.GetDataSource("JewelryRows");
        if (dataSource is not null)
        {
            dataSource.Enabled = true;
            foreach (Base obj in report.AllObjects)
            {
                if (obj is DataBand dataBand)
                    dataBand.DataSource = dataSource;
            }
        }

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

    public void ShowPreview(Report report)
    {
        var path = ExportToTempPdf(report, "preview");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void Print(Report report)
    {
        var path = ExportToTempPdf(report, "print");
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "print"
        });
    }

    public string ExportToTempPdf(Report report, string purpose = "view")
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"kazuko-jewelry-on-hand-{purpose}-{Guid.NewGuid():N}.pdf");
        using var export = new PDFSimpleExport();
        report.Export(export, path);
        return path;
    }

    private static string BuildFilterText(
        string scopeFilter,
        string goldTypeFilter,
        string statusFilter,
        string searchText)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(searchText))
            parts.Add($"Từ khóa: {searchText.Trim()}");
        if (!string.IsNullOrWhiteSpace(goldTypeFilter) && !goldTypeFilter.StartsWith("Tất cả", StringComparison.Ordinal))
            parts.Add($"Loại vàng: {goldTypeFilter}");
        if (!string.IsNullOrWhiteSpace(scopeFilter) && !scopeFilter.StartsWith("Tất cả", StringComparison.Ordinal))
            parts.Add($"Kho: {scopeFilter}");
        if (!string.IsNullOrWhiteSpace(statusFilter) && !statusFilter.StartsWith("Tất cả", StringComparison.Ordinal))
            parts.Add($"Trạng thái: {statusFilter}");
        return parts.Count == 0 ? "Tất cả" : string.Join(" · ", parts);
    }

    private static string GetTemplatePath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Reports", "Templates", TemplateFileName);
    }

    private static void EnsureTemplate(string templatePath)
    {
        if (File.Exists(templatePath))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(templatePath)!);
        using var report = new Report();
        report.RegisterData(new List<JewelryOnHandPrintRow>(), "JewelryRows");
        var ds = report.GetDataSource("JewelryRows");
        if (ds is not null)
            ds.Enabled = true;
        BuildDefaultTemplate(report);
        foreach (Base obj in report.AllObjects)
        {
            if (obj is DataBand dataBand)
                dataBand.DataSource = ds;
        }
        report.Save(templatePath);
    }

    private static void BuildDefaultTemplate(Report report)
    {
        report.Pages.Clear();
        report.Parameters.Clear();

        AddParameter(report, "BranchName", "Chi nhánh");
        AddParameter(report, "PrintedAt", "Ngày in");
        AddParameter(report, "PrintedBy", "Người in");
        AddParameter(report, "FilterText", "Bộ lọc");
        AddParameter(report, "ItemCount", "Số món");
        AddParameter(report, "TotalGoldWeight", "Tổng TL vàng");
        AddParameter(report, "TotalStockValue", "Tổng giá trị");

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

        var contentWidth = Mm(190); // 210 - 10 - 10
        var titleHeight = Mm(28);
        var rowHeight = Mm(7);

        var titleBand = new ReportTitleBand { Name = "ReportTitle1", Height = titleHeight };
        page.Bands.Add(titleBand);
        titleBand.Objects.Add(CreateText(0, Mm(0), contentWidth, Mm(8), "BÁO CÁO NỮ TRANG TỒN KHO", 14, FontStyle.Bold, HorzAlign.Center));
        titleBand.Objects.Add(CreateText(0, Mm(9), contentWidth, Mm(5), "[BranchName]", 10, FontStyle.Regular, HorzAlign.Center));
        titleBand.Objects.Add(CreateText(0, Mm(14), contentWidth, Mm(5), "Ngày in: [PrintedAt]  ·  Người in: [PrintedBy]", 9, FontStyle.Regular, HorzAlign.Center));
        titleBand.Objects.Add(CreateText(0, Mm(20), contentWidth, Mm(5), "Bộ lọc: [FilterText]", 9, FontStyle.Regular, HorzAlign.Left));

        var pageHeader = new PageHeaderBand { Name = "PageHeader1", Height = rowHeight };
        page.Bands.Add(pageHeader);
        AddHeaderCells(pageHeader, contentWidth, rowHeight);

        var dataBand = new DataBand { Name = "Data1", Height = rowHeight };
        page.Bands.Add(dataBand);
        AddDataCells(dataBand, contentWidth, rowHeight);

        var footerBand = new ReportSummaryBand { Name = "ReportSummary1", Height = Mm(20) };
        page.Bands.Add(footerBand);
        footerBand.Objects.Add(CreateText(
            0, Mm(2), contentWidth, Mm(6),
            "Tổng số món: [ItemCount]  ·  Tổng TL vàng: [TotalGoldWeight]  ·  Tổng giá trị: [TotalStockValue] VND",
            10, FontStyle.Bold, HorzAlign.Left));
        footerBand.Objects.Add(CreateText(
            0, Mm(12), contentWidth, Mm(6),
            "Người lập: .....................          Thủ kho: .....................          Quản lý: .....................",
            9, FontStyle.Regular, HorzAlign.Left));
    }

    private static void AddParameter(Report report, string name, string description)
    {
        var p = new FastReport.Data.Parameter(name)
        {
            DataType = typeof(string),
            Description = description,
            Value = string.Empty
        };
        report.Parameters.Add(p);
    }

    private static void AddHeaderCells(BandBase band, float contentWidth, float rowHeight)
    {
        // Tỷ lệ cột (tổng = 1), trải hết bề ngang nội dung A4.
        float[] ratios = [0.05f, 0.15f, 0.28f, 0.08f, 0.09f, 0.12f, 0.13f, 0.10f];
        string[] titles = ["STT", "Mã SP", "Tên sản phẩm", "Loại vàng", "TL vàng", "Giá trị", "Vị trí", "Trạng thái"];
        float x = 0;
        for (var i = 0; i < titles.Length; i++)
        {
            var w = contentWidth * ratios[i];
            var cell = CreateText(x, 0, w, rowHeight, titles[i], 8, FontStyle.Bold, HorzAlign.Center);
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
            var cell = CreateText(x, 0, w, rowHeight, exprs[i], 8, FontStyle.Regular, aligns[i]);
            cell.Border.Lines = BorderLines.All;
            cell.CanGrow = false;
            cell.WordWrap = true;
            band.Objects.Add(cell);
            x += w;
        }
    }

    private static TextObject CreateText(
        float left,
        float top,
        float width,
        float height,
        string text,
        float fontSize,
        FontStyle style,
        HorzAlign align)
    {
        return new TextObject
        {
            Bounds = new RectangleF(left, top, width, height),
            Text = text,
            Font = new Font("Arial", fontSize, style),
            HorzAlign = align,
            VertAlign = VertAlign.Center,
            CanGrow = false,
            WordWrap = true,
            Padding = new System.Windows.Forms.Padding(2, 1, 2, 1)
        };
    }

    // PaperWidth/Margins dùng mm; Bounds object dùng đơn vị màn hình (Units.Millimeters).
    private static float Mm(float millimeters) => millimeters * Units.Millimeters;
}
