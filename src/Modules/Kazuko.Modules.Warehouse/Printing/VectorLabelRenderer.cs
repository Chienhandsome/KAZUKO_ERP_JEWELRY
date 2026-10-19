using System;
using System.Drawing;
using System.Linq;
using FastReport;
using FastReport.Barcode;
using FastReport.Utils;

namespace Kazuko.Modules.Warehouse.Printing;

/// <summary>
/// Vẽ tem nhãn trực tiếp bằng vector GDI+ lên Graphics của máy in (thay pipeline export PNG).
///
/// Nguyên lý: FastReport vẫn làm nhiệm vụ khó (load template .frx, nạp DataTable,
/// resolve [DataSet.CỘT]/[ThamSố] + định dạng). Sau report.Prepare(), Text của mỗi
/// object đã là text hoàn chỉnh (BandBase.GetData() → TextObject.GetData() resolve
/// expression ngay trong lúc Prepare). Renderer này chỉ việc vẽ lại các object đó
/// bằng vector lên đúng độ phân giải của máy in → chữ mịn, không răng cưa.
///
/// Giới hạn đã biết:
/// - Mỗi report chỉ chứa 1 dòng dữ liệu (1 tem). Với nhiều dòng, Text sẽ là giá trị
///   của dòng cuối (đúng như cách FastReport tự render).
/// - AutoShrink chỉ là xấp xỉ (vòng lặp MeasureString giảm cỡ font), không
///   pixel-identical với FastReport.
/// - Chỉ hỗ trợ TextObject và BarcodeObject; object loại khác được bỏ qua.
/// </summary>
public static class VectorLabelRenderer
{
    // FastReport's Windows compatibility assembly omits IGraphics used by another overload.
    // Select the public GDI+ constructor by its exact signature.
    private static readonly System.Reflection.ConstructorInfo PaintConstructor = typeof(FRPaintEventArgs)
        .GetConstructor([typeof(Graphics), typeof(float), typeof(float), typeof(GraphicCache)])
        ?? throw new InvalidOperationException("Không tìm thấy bộ vẽ mã FastReport cho Windows.");

    internal static void DrawNativeBarcode(Graphics graphics, BarcodeObject barcode)
    {
        using var cache = new GraphicCache();
        var args = (FRPaintEventArgs)PaintConstructor.Invoke([graphics, 1f, 1f, cache]);
        barcode.Draw(args);
    }
    /// <summary>FastReport dùng pixel 96dpi cho tọa độ object.</summary>
    private const float MmPerPx = 25.4f / 96f;

    /// <summary>
    /// Vẽ trang đầu tiên của report (đã Prepare()) lên Graphics.
    /// Yêu cầu: Graphics đang ở PageUnit = Millimeter.
    /// </summary>
    public static void DrawLabel(Graphics g, Report report)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(report);

        var page = report.Pages.OfType<ReportPage>().FirstOrDefault();
        if (page is null)
            return;

        DrawPage(g, page);
    }

    public static void DrawPreparedLabel(Graphics g, Report report)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(report);
        if (report.PreparedPages.Count == 0)
            throw new InvalidOperationException("Mẫu tem chưa có trang in.");
        using var page = report.PreparedPages.GetPage(0);
        DrawPage(g, page);
    }

    private static void DrawPage(Graphics g, ReportPage page)
    {
        // Duyệt mọi object trong các band (template tem chỉ có 1 DataBand).
        foreach (var band in page.Bands.OfType<BandBase>())
        {
            foreach (var obj in band.Objects)
            {
                switch (obj)
                {
                    case TextObject textObj:
                        DrawText(g, textObj);
                        break;
                    case BarcodeObject barcodeObj:
                        DrawBarcode(g, barcodeObj);
                        break;
                    default:
                        break; // object loại khác bỏ qua
                }
            }
        }
    }

    private static void DrawText(Graphics g, TextObject obj)
    {
        // Sau Prepare(), Text đã được FastReport resolve xong ([DataSet.X], [ThamSố], định dạng).
        string text = obj.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text))
            return;

        float x = obj.AbsLeft * MmPerPx;
        float y = obj.AbsTop * MmPerPx;
        float w = obj.Width * MmPerPx;
        float h = obj.Height * MmPerPx;
        if (w <= 0 || h <= 0)
            return;

        float fontSize = obj.Font.Size;
        if (obj.AutoShrink != AutoShrinkMode.None)
            fontSize = ShrinkFontToFit(g, text, obj, w, h);

        using var font = new Font(obj.Font.FontFamily, fontSize, obj.Font.Style, GraphicsUnit.Point);
        using var brush = new SolidBrush(obj.TextColor);
        using var format = new StringFormat(StringFormatFlags.NoWrap)
        {
            Alignment = ToAlignment(obj.HorzAlign),
            LineAlignment = ToLineAlignment(obj.VertAlign),
            Trimming = StringTrimming.None,
        };

        if (obj.Angle != 0)
        {
            // FastReport xoay chữ quanh TÂM rect bằng RotateTransform(+Angle) (chuẩn GDI+:
            // dương = cùng chiều kim đồng hồ) — đã đối chiếu trực tiếp từ source FastReport.
            // Nếu chữ dọc in ra ngược hướng so với bản PNG cũ, đây là dòng cần xem lại.
            var state = g.Save();
            try
            {
                g.TranslateTransform(x + w / 2f, y + h / 2f);
                g.RotateTransform(obj.Angle);
                float rw = Math.Abs(obj.Angle) % 180 == 90 ? h : w;
                float rh = Math.Abs(obj.Angle) % 180 == 90 ? w : h;
                g.DrawString(text, font, brush, new RectangleF(-rw / 2f, -rh / 2f, rw, rh), format);
            }
            finally
            {
                g.Restore(state);
            }
        }
        else
        {
            g.DrawString(text, font, brush, new RectangleF(x, y, w, h), format);
        }
    }

    /// <summary>
    /// Xấp xỉ AutoShrink: giảm dần cỡ font (bước 0.5pt) cho đến khi text vừa khung
    /// hoặc chạm ngưỡng dưới. Không pixel-identical với FastReport nhưng đủ dùng.
    /// </summary>
    private static float ShrinkFontToFit(Graphics g, string text, TextObject obj, float maxW, float maxH)
    {
        float minSize = Math.Max(obj.AutoShrinkMinSize, 1f);
        using var format = new StringFormat(StringFormatFlags.NoWrap);
        for (float size = obj.Font.Size; size >= minSize; size -= 0.5f)
        {
            using var font = new Font(obj.Font.FontFamily, size, obj.Font.Style, GraphicsUnit.Point);
            var measured = g.MeasureString(text, font, new SizeF(maxW, maxH), format);
            if (measured.Width <= maxW && measured.Height <= maxH)
                return size;
        }
        return minSize;
    }

    private static void DrawBarcode(Graphics g, BarcodeObject obj)
    {
        if (obj.Barcode is BarcodeQR)
        {
            var state = g.Save();
            try
            {
                g.ScaleTransform(MmPerPx, MmPerPx);
                DrawNativeBarcode(g, obj);
            }
            finally { g.Restore(state); }
            return;
        }
        // Sau Prepare(), Text đã được resolve từ DataColumn (BarcodeObject.GetData()).
        string data = obj.Text ?? string.Empty;
        if (string.IsNullOrEmpty(data))
            return;

        int[] widths = Code128Encoder.Encode(data); // chỉ số chẵn = vạch đen

        float x = obj.AbsLeft * MmPerPx;
        float y = obj.AbsTop * MmPerPx;
        float w = obj.Width * MmPerPx;
        float h = obj.Height * MmPerPx;
        if (w <= 0 || h <= 0)
            return;

        int totalModules = Code128Encoder.QuietZoneModules * 2;
        foreach (int wd in widths)
            totalModules += wd;
        float moduleW = w / totalModules;

        using var brush = new SolidBrush(Color.Black);
        float cx = x + Code128Encoder.QuietZoneModules * moduleW;
        for (int i = 0; i < widths.Length; i++)
        {
            float bw = widths[i] * moduleW;
            if ((i & 1) == 0) // vạch đen
                g.FillRectangle(brush, cx, y, bw, h);
            cx += bw;
        }
        // Template để ShowText=false nên không vẽ chữ dưới mã vạch (giống bản cũ).
    }

    private static StringAlignment ToAlignment(HorzAlign align) => align switch
    {
        HorzAlign.Center => StringAlignment.Center,
        HorzAlign.Right => StringAlignment.Far,
        _ => StringAlignment.Near, // Left, Justify → Near (GDI+ không justify trực tiếp)
    };

    private static StringAlignment ToLineAlignment(VertAlign align) => align switch
    {
        VertAlign.Center => StringAlignment.Center,
        VertAlign.Bottom => StringAlignment.Far,
        _ => StringAlignment.Near,
    };
}
