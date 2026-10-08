using System.Data;
using System.Globalization;
using System.IO;
using FastReport;
using FastReport.Data;
using FastReport.Export.PdfSimple;
using Kazuko.Core.Authentication;
using Kazuko.Core.Printing;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Services;

/// <summary>
/// Builds sales receipt PDF from FastReport template (A5 ngang/dọc) loaded via <see cref="IPrintTemplateStore"/>.
/// </summary>
public sealed class SalesReceiptPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    private readonly IPrintTemplateStore _templates;

    public SalesReceiptPrintService(IPrintTemplateStore templates)
    {
        _templates = templates ?? throw new ArgumentNullException(nameof(templates));
    }

    public async Task<Report> CreatePreparedReportAsync(
        SalesReceiptDto receipt,
        AuthenticatedSession session,
        SalesReceiptOrientation orientation = SalesReceiptOrientation.Landscape,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(session);
        var template = await _templates.GetByCodeAsync(
            SalesReceiptOrientationCodes.ToTemplateCode(orientation),
            session,
            cancellationToken);

        var report = new Report();
        using (var stream = new MemoryStream(template.Content))
            report.Load(stream);

        // Open Source compiles DataBand.Filter as C# — "[DataSet.LADE]==…" fails.
        // Also the .frx Dictionary often omits LADE / *_CHUOI columns. Split tables + clear filters.
        NormalizeReceiptTemplate(report);

        var saleTable = BuildSaleLineTable(receipt);
        var componentTable = BuildComponentTable(receipt);
        RegisterTable(report, saleTable, "DataSet");
        RegisterTable(report, componentTable, "OldGold");

        BindSaleAndOldGoldBands(report);
        ApplyParameters(report, receipt);
        // Precompute nested expression that breaks Open Source: [[A]-[B]]
        SetParameter(report, "Cộng tiền", receipt.Checkout.Totals.SaleGrossVnd - receipt.Checkout.Totals.OldGoldTotalVnd);

        report.Prepare();
        return report;
    }

    public async Task<string> ExportToTempPdfAsync(
        SalesReceiptDto receipt,
        AuthenticatedSession session,
        SalesReceiptOrientation orientation = SalesReceiptOrientation.Landscape,
        CancellationToken cancellationToken = default)
    {
        using var report = await CreatePreparedReportAsync(receipt, session, orientation, cancellationToken);
        var suffix = orientation == SalesReceiptOrientation.Portrait ? "doc" : "ngang";
        var path = Path.Combine(
            Path.GetTempPath(),
            $"kazuko-sales-receipt-{suffix}-{receipt.Checkout.DocumentCode.Replace('/', '-')}-{Guid.NewGuid():N}.pdf");
        using var export = new PDFSimpleExport();
        report.Export(export, path);
        return path;
    }

    public void PrintPdf(string pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            throw new InvalidOperationException("Không tìm thấy file PDF để in.");

        // Mở PDF (không silent Verb=print) — người dùng chọn khổ A5 ngang / Fit để tránh cắt lề.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pdfPath)
        {
            UseShellExecute = true
        });
    }

    public static void TryDeleteTempPdf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // ignore cleanup failures
        }
    }

    /// <summary>Combined mapping (unit tests). Runtime uses split sale/old-gold tables.</summary>
    public static DataTable BuildLineTable(SalesReceiptDto receipt)
    {
        var table = CreateLineSchema("DataSet");
        foreach (DataRow row in BuildSaleLineTable(receipt).Rows)
            table.ImportRow(row);
        foreach (DataRow row in BuildComponentTable(receipt).Rows)
            table.ImportRow(row);
        return table;
    }

    public static DataTable BuildSaleLineTable(SalesReceiptDto receipt)
    {
        var table = CreateLineSchema("DataSet");
        foreach (var line in receipt.Checkout.SaleLines)
        {
            var gold = line.GoldWeightChi;
            var stone = line.StoneWeightChi;
            var total = line.TotalWeightChi > 0 ? line.TotalWeightChi : gold + stone;
            table.Rows.Add(
                line.ProductCode,
                line.ProductName,
                line.GoldPurity,
                total,
                gold,
                stone,
                gold,
                FormatQty(gold),
                FormatQty(gold),
                line.Amounts.LaborAmountVnd,
                line.AppliedPrices.AppliedGoldPricePerChi,
                line.Amounts.TotalAmountVnd,
                0m,
                0);
        }

        return table;
    }

    /// <summary>
    /// Bảng thành phần (Subreport trái): ưu tiên dòng vàng cũ;
    /// nếu không có thì lấy thành phần vàng từ dòng bán (tuổi / TL / đơn giá / tiền vàng).
    /// </summary>
    public static DataTable BuildComponentTable(SalesReceiptDto receipt)
    {
        var table = CreateLineSchema("OldGold");
        if (receipt.Checkout.OldGoldLines.Count > 0)
        {
            foreach (var line in receipt.Checkout.OldGoldLines)
            {
                var gold = line.Amounts.GoldWeightChi;
                var stone = line.Draft.StoneWeightChi;
                var loai = string.IsNullOrWhiteSpace(line.MaterialCode) ? line.MaterialName : line.MaterialCode;
                table.Rows.Add(
                    string.Empty,
                    line.MaterialName,
                    loai,
                    line.Draft.TotalWeightChi,
                    gold,
                    stone,
                    gold,
                    FormatQty(gold),
                    FormatQty(gold),
                    0m,
                    line.Draft.AppliedBuyPricePerChi,
                    line.Amounts.AmountVnd,
                    0m,
                    30);
            }

            return table;
        }

        foreach (var line in receipt.Checkout.SaleLines)
        {
            var gold = line.GoldWeightChi;
            var stone = line.StoneWeightChi;
            var total = line.TotalWeightChi > 0 ? line.TotalWeightChi : gold + stone;
            var loai = string.IsNullOrWhiteSpace(line.GoldPurity) ? line.ProductName : line.GoldPurity;
            table.Rows.Add(
                line.ProductCode,
                line.ProductName,
                loai,
                total,
                gold,
                stone,
                gold,
                FormatQty(gold),
                FormatQty(gold),
                0m,
                line.AppliedPrices.AppliedGoldPricePerChi,
                line.Amounts.GoldAmountVnd,
                0m,
                30);
        }

        return table;
    }

    [Obsolete("Use BuildComponentTable")]
    public static DataTable BuildOldGoldLineTable(SalesReceiptDto receipt) =>
        BuildComponentTable(receipt);

    private static DataTable CreateLineSchema(string tableName)
    {
        var table = new DataTable(tableName);
        table.Columns.Add("MAVACH", typeof(string));
        table.Columns.Add("MATHANG", typeof(string));
        table.Columns.Add("LOAIVANG", typeof(string));
        table.Columns.Add("TONGTRONGLUONG", typeof(decimal));
        table.Columns.Add("TRONGLUONGVANG", typeof(decimal));
        table.Columns.Add("TRONGLUONGHOT", typeof(decimal));
        table.Columns.Add("VANGTHUCTE", typeof(decimal));
        table.Columns.Add("VANGTHUCTE_CHUOI", typeof(string));
        table.Columns.Add("TRONGLUONGVANG_CHUOI", typeof(string));
        table.Columns.Add("CONGBAN", typeof(decimal));
        table.Columns.Add("DONGIA", typeof(decimal));
        table.Columns.Add("THANHTIEN", typeof(decimal));
        table.Columns.Add("CAT", typeof(decimal));
        table.Columns.Add("LADE", typeof(int));
        return table;
    }

    private static void NormalizeReceiptTemplate(Report report)
    {
        // Clear filters that Open Source cannot compile ([DataSet.LADE]==…).
        foreach (Base obj in report.AllObjects)
        {
            if (obj is DataBand band)
                band.Filter = string.Empty;
        }

        // Lề PDF ≥ 10mm — máy in vật lý thường cắt vùng < 5–8mm.
        foreach (PageBase pageBase in report.Pages)
        {
            if (pageBase is not ReportPage page)
                continue;
            // Page2/3/4 are Subreport hosts on A5 dọc — không in thành trang riêng.
            if (page.Name is "Page2" or "Page3" or "Page4")
            {
                page.Visible = false;
                continue;
            }

            if (page.LeftMargin < 10f)
                page.LeftMargin = 10f;
            if (page.RightMargin < 10f)
                page.RightMargin = 10f;
            if (page.TopMargin < 8f)
                page.TopMargin = 8f;
            if (page.BottomMargin < 8f)
                page.BottomMargin = 8f;
        }

        // Bảng thành phần nằm ở Subreport Page2 — in đè lên summary, không tách trang.
        foreach (Base obj in report.AllObjects)
        {
            if (obj is SubreportObject sub)
                sub.PrintOnParent = true;
        }

        // Replace nested [[A]-[B]] which also fails Open Source expression compile.
        // TableCell lives in a commercial assembly — touch Text via reflection.
        foreach (Base obj in EnumerateTree(report))
        {
            if (!TryGetText(obj, out var text)
                || !text.Contains("[[Tiền vàng mới]-[Tiền vàng cũ]]", StringComparison.Ordinal))
            {
                continue;
            }

            TrySetText(obj, "[Cộng tiền]");
        }
    }

    private static void RegisterTable(Report report, DataTable table, string name)
    {
        // Drop dictionary stub so RegisterData publishes full column set (incl. LADE / *_CHUOI).
        var existing = report.Dictionary.FindByName(name);
        if (existing is not null)
            existing.Dispose();

        report.RegisterData(table, name);
        var dataSource = report.GetDataSource(name);
        if (dataSource is not null)
            dataSource.Enabled = true;
    }

    private static void BindSaleAndOldGoldBands(Report report)
    {
        var saleSource = report.GetDataSource("DataSet");
        var oldSource = report.GetDataSource("OldGold");

        foreach (Base obj in report.AllObjects)
        {
            if (obj is not DataBand band)
                continue;

            // Landscape: Data2 = thành phần. Portrait: Data2/Data4 = thành phần; Data1/Data3 = dòng bán.
            if (IsComponentBand(band))
            {
                if (oldSource is not null)
                    band.DataSource = oldSource;
                RewriteDataSetReferences(band, "OldGold");
            }
            else
            {
                if (saleSource is not null)
                    band.DataSource = saleSource;
            }
        }
    }

    private static bool IsComponentBand(DataBand band) =>
        band.Name is "Data2" or "Data4";

    private static void RewriteDataSetReferences(Base root, string newSourceName)
    {
        foreach (Base obj in EnumerateTree(root))
        {
            if (!TryGetText(obj, out var text) || !text.Contains("[DataSet.", StringComparison.Ordinal))
                continue;

            TrySetText(obj, text.Replace("[DataSet.", $"[{newSourceName}.", StringComparison.Ordinal));
        }
    }

    private static IEnumerable<Base> EnumerateTree(Base root)
    {
        yield return root;
        foreach (Base child in root.ChildObjects)
        {
            foreach (var nested in EnumerateTree(child))
                yield return nested;
        }
    }

    private static bool TryGetText(Base obj, out string text)
    {
        text = string.Empty;
        if (obj is TextObject textObject)
        {
            text = textObject.Text ?? string.Empty;
            return !string.IsNullOrEmpty(text);
        }

        var prop = obj.GetType().GetProperty("Text");
        if (prop?.PropertyType != typeof(string) || !prop.CanRead)
            return false;

        text = prop.GetValue(obj) as string ?? string.Empty;
        return !string.IsNullOrEmpty(text);
    }

    private static void TrySetText(Base obj, string value)
    {
        if (obj is TextObject textObject)
        {
            textObject.Text = value;
            return;
        }

        var prop = obj.GetType().GetProperty("Text");
        if (prop?.PropertyType == typeof(string) && prop.CanWrite)
            prop.SetValue(obj, value);
    }

    private static void ApplyParameters(Report report, SalesReceiptDto receipt)
    {
        var checkout = receipt.Checkout;
        var totals = checkout.Totals;

        SetParameter(report, "Tên khách", checkout.CustomerName);
        SetParameter(report, "Số phiếu", checkout.DocumentCode);
        SetParameter(report, "Ngày", checkout.DocumentDate);
        SetParameter(report, "Date", DateTime.Now);
        SetParameter(report, "Tiền vàng mới", totals.SaleGrossVnd);
        SetParameter(report, "Tiền vàng cũ", totals.OldGoldTotalVnd);
        SetParameter(report, "Tiền bớt", totals.DiscountAmountVnd);
        SetParameter(report, "Tổng cộng", totals.NetAmountVnd);
        SetParameter(report, "Tổng cộng bằng chữ", VietnameseMoneyText.ToDong(totals.NetAmountVnd));
        SetParameter(report, "Công ty", receipt.CompanyName);
        SetParameter(report, "Chi nhánh", receipt.BranchName);
        SetParameter(report, "Điện thoại", receipt.BranchPhone ?? string.Empty);
        SetParameter(report, "Địa chỉ", receipt.BranchAddress ?? string.Empty);
        SetParameter(report, "Nhân viên", checkout.SalespersonName);
        SetParameter(report, "Người in", checkout.SalespersonName);
        SetParameter(report, "Thu ngân", checkout.CreatedByDisplayName);
    }

    private static void SetParameter(Report report, string name, object value)
    {
        var existing = report.Parameters.FindByName(name);
        if (existing is null)
        {
            report.Parameters.Add(new Parameter(name)
            {
                DataType = value.GetType(),
                Value = value
            });
        }
        else
        {
            report.SetParameterValue(name, value);
        }
    }

    private static string FormatQty(decimal value) =>
        value.ToString("0.####", Vi);
}
