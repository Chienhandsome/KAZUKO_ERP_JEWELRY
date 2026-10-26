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
        CancellationToken cancellationToken = default,
        byte[]? templateContent = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(session);
        var content = templateContent ?? (await _templates.GetByCodeAsync(
            SalesReceiptOrientationCodes.ToTemplateCode(orientation),
            session,
            cancellationToken)).Content;

        var report = new Report();
        using (var stream = new MemoryStream(content))
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

    public void PrintPdf(string pdfPath) =>
        PrintPdfFitted(pdfPath, SalesReceiptOrientation.Landscape);

    /// <summary>
    /// In giống setting WPS đẹp: A5 Portrait, Fit for printing margins, Auto center, Auto rotate.
    /// PDF (PdfSimple) là ảnh trang — vẽ fit+center lên PrintDocument, không qua WebView2.
    /// </summary>
    public void PrintPdfFitted(string pdfPath, SalesReceiptOrientation orientation)
    {
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            throw new InvalidOperationException("Không tìm thấy file PDF để in.");

        using var pageImage = ExtractPdfPageImage(pdfPath);
        using var document = new System.Drawing.Printing.PrintDocument();
        document.DocumentName = "Phiếu bán hàng KAZUKO";
        ApplyWpsLikePageSettings(document);

        document.PrintPage += (_, e) =>
        {
            var g = e.Graphics!;
            g.PageUnit = System.Drawing.GraphicsUnit.Display;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            // Vùng in an toàn = MarginBounds ∩ (PageBounds trừ hard margin máy in).
            var bounds = GetSafePrintBounds(e);

            // Auto rotate (WPS): nếu ảnh ngang trên giấy dọc (hoặc ngược lại) thì xoay 90°.
            var imageLandscape = pageImage.Width >= pageImage.Height;
            var paperLandscape = bounds.Width >= bounds.Height;
            var rotate = imageLandscape != paperLandscape;

            float contentW = rotate ? pageImage.Height : pageImage.Width;
            float contentH = rotate ? pageImage.Width : pageImage.Height;
            var scale = Math.Min(bounds.Width / contentW, bounds.Height / contentH);
            var drawW = contentW * scale;
            var drawH = contentH * scale;
            // Auto center; khổ dọc lệch trái nhẹ (~2mm) tránh máy in cắt mép phải.
            var biasX = orientation == SalesReceiptOrientation.Portrait ? -8f : 0f;
            var x = bounds.X + (bounds.Width - drawW) / 2f + biasX;
            if (x < bounds.X)
                x = bounds.X;
            var y = bounds.Y + (bounds.Height - drawH) / 2f;

            if (rotate)
            {
                // Xoay 90° CCW quanh góc trên-trái vùng vẽ (giống preview WPS).
                g.TranslateTransform(x, y + drawH);
                g.RotateTransform(-90f);
                g.DrawImage(pageImage, 0, 0, drawH, drawW);
                g.ResetTransform();
            }
            else
            {
                g.DrawImage(pageImage, x, y, drawW, drawH);
            }

            e.HasMorePages = false;
        };

        // In thẳng máy in mặc định — không hiện hộp thoại Windows.
        document.Print();
    }

    /// <summary>WPS: Page Size A5, Orientation Portrait, margins ~4mm.</summary>
    private static void ApplyWpsLikePageSettings(System.Drawing.Printing.PrintDocument document)
    {
        document.DefaultPageSettings.PaperSize = ResolveA5Paper(document.PrinterSettings);
        document.DefaultPageSettings.Landscape = false;
        // WPS margins ~4.06 / 3.22 mm → hundredths of inch (1 mm ≈ 3.94).
        document.DefaultPageSettings.Margins = new System.Drawing.Printing.Margins(16, 13, 16, 16);
    }

    private static System.Drawing.Rectangle GetSafePrintBounds(System.Drawing.Printing.PrintPageEventArgs e)
    {
        var page = e.PageBounds;
        var hardX = (int)Math.Ceiling(e.PageSettings.HardMarginX);
        var hardY = (int)Math.Ceiling(e.PageSettings.HardMarginY);
        var printable = System.Drawing.Rectangle.FromLTRB(
            page.Left + hardX,
            page.Top + hardY,
            page.Right - hardX,
            page.Bottom - hardY);
        var safe = System.Drawing.Rectangle.Intersect(e.MarginBounds, printable);
        return safe.Width > 0 && safe.Height > 0 ? safe : e.MarginBounds;
    }

    private static System.Drawing.Printing.PaperSize ResolveA5Paper(
        System.Drawing.Printing.PrinterSettings printer)
    {
        foreach (System.Drawing.Printing.PaperSize size in printer.PaperSizes)
        {
            if (size.Kind == System.Drawing.Printing.PaperKind.A5)
                return size;
            if (size.PaperName.Contains("A5", StringComparison.OrdinalIgnoreCase))
                return size;
        }

        // 148×210 mm = 5.83"×8.27" → hundredths of inch.
        return new System.Drawing.Printing.PaperSize("A5", 583, 827);
    }

    /// <summary>PdfSimple nhúng mỗi trang thành JPEG — tách SOI/EOI.</summary>
    private static System.Drawing.Image ExtractPdfPageImage(string pdfPath)
    {
        var bytes = File.ReadAllBytes(pdfPath);
        var start = -1;
        var end = -1;
        for (var i = 0; i < bytes.Length - 1; i++)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8)
            {
                start = i;
                break;
            }
        }

        for (var i = bytes.Length - 2; i >= 1; i--)
        {
            if (bytes[i] == 0xFF && bytes[i + 1] == 0xD9)
            {
                end = i + 1;
                break;
            }
        }

        if (start < 0 || end <= start)
            throw new InvalidOperationException(
                "Không đọc được ảnh trang từ PDF (PdfSimple). Hãy mở PDF và in bằng WPS với khổ A5.");

        var length = end - start + 1;
        using var stream = new MemoryStream(bytes, start, length, writable: false, publiclyVisible: true);
        // Clone so Image owns its buffer after MemoryStream is disposed.
        using var temp = System.Drawing.Image.FromStream(stream);
        return new System.Drawing.Bitmap(temp);
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

    internal static void NormalizeReceiptTemplate(Report report)
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

            // ISO A5 exact — giúp viewer/máy in nhận đúng khổ (tránh mặc định A4 bị cắt).
            if (page.Landscape)
            {
                page.PaperWidth = 210f;
                page.PaperHeight = 148f;
                // Laser thường cắt ~5mm mép trái — lề PDF ≥ 12mm.
                page.LeftMargin = Math.Max(page.LeftMargin, 12f);
                page.RightMargin = Math.Max(page.RightMargin, 12f);
                page.TopMargin = Math.Max(page.TopMargin, 10f);
                page.BottomMargin = Math.Max(page.BottomMargin, 10f);
            }
            else
            {
                page.PaperWidth = 148f;
                page.PaperHeight = 210f;
                // Laser cắt mép — lề trái 14mm, phải 16mm (đẩy nội dung sang trái một chút).
                page.LeftMargin = Math.Max(page.LeftMargin, 14f);
                page.RightMargin = Math.Max(page.RightMargin, 16f);
                page.TopMargin = Math.Max(page.TopMargin, 10f);
                page.BottomMargin = Math.Max(page.BottomMargin, 12f);
            }
        }

        // Bảng thành phần nằm ở Subreport — in đè lên parent, không tách trang.
        foreach (Base obj in report.AllObjects)
        {
            if (obj is SubreportObject sub)
                sub.PrintOnParent = true;
        }

        // A5 dọc: đưa Vàng mới/Vàng cũ từ PageFooter vào Summary (tránh cắt đáy).
        if (report.ReportInfo.Description != "KAZUKO_RECEIPT_DESIGNER_V1")
        {
            CompactPortraitOverflow(report);
            AlignLandscapeSummaryToMainTable(report);
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

    /// <summary>
    /// Mẫu A5 dọc cũ để Vàng mới/Vàng cũ trong PageFooter → máy in cắt đáy.
    /// Chuyển các khối đó vào ReportSummary và thu gọn title nếu còn khoảng trống lớn.
    /// </summary>
    private static void CompactPortraitOverflow(Report report)
    {
        foreach (PageBase pageBase in report.Pages)
        {
            if (pageBase is not ReportPage page || page.Landscape)
                continue;
            if (!string.Equals(page.Name, "Page1", StringComparison.OrdinalIgnoreCase))
                continue;

            ReportTitleBand? title = null;
            ReportSummaryBand? summary = null;
            PageFooterBand? footer = null;
            foreach (Base child in page.ChildObjects)
            {
                switch (child)
                {
                    case ReportTitleBand t:
                        title = t;
                        break;
                    case ReportSummaryBand s:
                        summary = s;
                        break;
                    case PageFooterBand f:
                        footer = f;
                        break;
                }
            }

            if (title is not null && title.Height > 150f)
            {
                ShiftChildrenUp(title, title.Height - 145f);
                title.Height = 145f;
            }

            if (summary is not null && footer is not null)
                RelocatePortraitCuongToSummary(summary, footer);

            if (footer is not null)
            {
                ClearBandChildren(footer);
                footer.Height = 0;
                footer.Visible = false;
            }

            if (summary is not null)
            {
                summary.CanGrow = true;
                summary.CanBreak = false;
                if (summary.Height < 260f)
                    summary.Height = 260f;
            }
        }
    }

    /// <summary>Chuyển nhãn + Subreport Vàng mới/Vàng cũ từ PageFooter sang Summary.</summary>
    private static void RelocatePortraitCuongToSummary(ReportSummaryBand summary, PageFooterBand footer)
    {
        // Đã có Subreport2 trong summary (mẫu mới) → không chuyển lại.
        foreach (Base child in summary.ChildObjects)
        {
            if (string.Equals(child.Name, "Subreport2", StringComparison.OrdinalIgnoreCase))
                return;
        }

        float baseTop = 118f;
        foreach (Base child in EnumerateTree(summary))
        {
            if (child is TextObject text
                && string.Equals(text.Name, "Text2", StringComparison.OrdinalIgnoreCase))
            {
                baseTop = text.Top + text.Height + 4f;
                break;
            }
        }

        var toMove = new List<ReportComponentBase>();
        foreach (Base child in footer.ChildObjects)
        {
            if (child is not ReportComponentBase component)
                continue;
            if (component.Name is "Text57" or "Subreport2" or "Text58" or "Subreport3"
                or "Barcode2" or "Text54" or "Text55" or "Text56" or "Text60"
                or "Text61" or "Text62")
            {
                toMove.Add(component);
            }
        }

        foreach (var component in toMove)
        {
            var name = component.Name;
            component.Parent = summary;
            component.Top = name switch
            {
                "Barcode2" or "Text57" or "Subreport2" => baseTop + Math.Max(0f, component.Top - 9.45f),
                "Text58" or "Subreport3" => baseTop + 60f + Math.Max(0f, component.Top - 113.4f),
                "Text54" or "Text55" => baseTop + 60f + Math.Max(0f, component.Top - 56.7f),
                "Text56" or "Text60" => baseTop + 60f + Math.Max(0f, component.Top - 56.7f),
                "Text61" or "Text62" => baseTop + 60f + Math.Max(0f, component.Top - 56.7f),
                _ => baseTop + component.Top
            };
        }
    }

    private static void ClearBandChildren(Base band)
    {
        var children = new List<Base>();
        foreach (Base child in band.ChildObjects)
            children.Add(child);
        foreach (var child in children)
            child.Dispose();
    }

    private static void ShiftChildrenUp(Base parent, float delta)
    {
        if (delta <= 0)
            return;
        foreach (Base child in parent.ChildObjects)
        {
            if (child is ReportComponentBase component && component.Top >= delta * 0.4f)
                component.Top = Math.Max(0f, component.Top - delta);
        }
    }

    /// <summary>
    /// Căn lề phải bảng Vàng mới/Vàng cũ/Tổng cộng với bảng hàng phía trên (A5 ngang).
    /// TableObject nằm assembly thương mại — dùng ReportComponentBase.
    /// </summary>
    private static void AlignLandscapeSummaryToMainTable(Report report)
    {
        ReportPage? landscapePage = null;
        foreach (PageBase pageBase in report.Pages)
        {
            if (pageBase is ReportPage page
                && page.Landscape
                && string.Equals(page.Name, "Page1", StringComparison.OrdinalIgnoreCase))
            {
                landscapePage = page;
                break;
            }
        }

        if (landscapePage is null)
            return;

        float? mainRight = null;
        ReportComponentBase? summary = null;
        foreach (Base obj in EnumerateTree(landscapePage))
        {
            if (obj is not ReportComponentBase component)
                continue;
            if (component.Name is "Table2" or "Table3")
                mainRight = Math.Max(mainRight ?? 0f, component.Left + component.Width);
            else if (string.Equals(component.Name, "Table1", StringComparison.OrdinalIgnoreCase))
                summary = component;
        }

        if (summary is null || mainRight is not float right || summary.Width <= 0)
            return;

        summary.Left = right - summary.Width;
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
