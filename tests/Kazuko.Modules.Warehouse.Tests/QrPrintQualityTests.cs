using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using FastReport;
using FastReport.Barcode;
using Kazuko.Core.Company;
using Kazuko.Modules.Warehouse.LabelDesigner;
using Kazuko.Modules.Warehouse.Printing;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class QrPrintQualityTests
{
    [Theory]
    [InlineData(203, "B600022")]
    [InlineData(203, "N600050")]
    [InlineData(300, "B600022")]
    public void Printed_modules_match_native_encoder_with_integer_dots_and_no_gray(int dpi, string text)
    {
        using var source = new BarcodeObject { Text = text, ShowText = false, AutoSize = false,
            Barcode = new BarcodeQR { ErrorCorrection = QRCodeErrorCorrection.L, QuietZone = false },
            Left = 10, Top = 4, Width = 32, Height = 32 };
        using var expected = new BarcodeObject { Text = text, ShowText = false, AutoSize = true,
            Barcode = new BarcodeQR { ErrorCorrection = QRCodeErrorCorrection.L, QuietZone = true } };
        expected.UpdateAutoSize();
        Assert.Equal(116f, expected.Width); // 21 symbol + 8 quiet modules, 4 native units/module.
        using var reference = new Bitmap(116, 116);
        using (var g = Graphics.FromImage(reference))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Pixel;
            VectorLabelRenderer.DrawNativeBarcode(g, expected);
        }
        using var bitmap = new Bitmap(160, 160);
        bitmap.SetResolution(dpi, dpi);
        int left, top, dots;
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.PageUnit = GraphicsUnit.Millimeter;
            g.TranslateTransform(.31f, .17f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var before = g.Transform;
            PointF[] bounds = [new(source.Left * 25.4f / 96, source.Top * 25.4f / 96),
                new(source.Right * 25.4f / 96, source.Bottom * 25.4f / 96)];
            g.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, bounds);
            left = (int)Math.Ceiling(bounds[0].X); top = (int)Math.Ceiling(bounds[0].Y);
            int width = (int)Math.Floor(bounds[1].X) - left;
            int height = (int)Math.Floor(bounds[1].Y) - top;
            dots = Math.Min(width, height) / 29;
            left += (width - 29 * dots) / 2;
            top += (height - 29 * dots) / 2;
            QrDotRenderer.Draw(g, source);
            Assert.Equal(SmoothingMode.AntiAlias, g.SmoothingMode);
            using var after = g.Transform;
            Assert.Equal(before.Elements, after.Elements);
        }
        Assert.False(((BarcodeQR)source.Barcode).QuietZone);
        Assert.True(dots >= 2);
        for (int row = 0; row < 29; row++)
        for (int col = 0; col < 29; col++)
        {
            var color = reference.GetPixel(col * 4 + 2, row * 4 + 2).ToArgb();
            Assert.True(color == Color.Black.ToArgb() || color == Color.White.ToArgb());
            for (int dy = 0; dy < dots; dy++)
            for (int dx = 0; dx < dots; dx++)
                Assert.Equal(color, bitmap.GetPixel(left + col * dots + dx, top + row * dots + dy).ToArgb());
        }
    }

    [Fact]
    public void Too_small_qr_is_rejected_instead_of_resampling_modules()
    {
        using var source = new BarcodeObject { Text = "B600022", Barcode = new BarcodeQR(),
            ShowText = false, Width = 20, Height = 20 };
        using var bitmap = new Bitmap(100, 100); bitmap.SetResolution(203, 203);
        using var graphics = Graphics.FromImage(bitmap); graphics.PageUnit = GraphicsUnit.Millimeter;
        Assert.Throws<InvalidOperationException>(() => QrDotRenderer.Draw(graphics, source));
    }

    [Fact]
    public void New_layout_keeps_font_heights_and_prints_only_black_white()
    {
        using var report = new Report();
        using (var source = typeof(FinishedGoodsLabelPrintService).Assembly.GetManifestResourceStream("Kazuko.Warehouse.LabelL.frx")) report.Load(source!);
        var sizes = report.AllObjects.OfType<TextObject>().ToDictionary(text => text.Name, text => text.Font.Size);
        using var data = LabelDesignerSampleData.BuildSampleTable(new CompanyProfileDto(1, "KAZUKO", null, null, null,
            "01:2026/KZJ", "CTY TNHH VBĐ THÀNH ĐẠT", "SN 778 Tổ DP Chiến Thắng - Điện Biên",
            "CTY KAZUKO GOLD", "925/15 Âu Cơ - Tân Sơn Nhì - TPHCM", "Vietnam"));
        FinishedGoodsLabelPrintService.RegisterTable(report, data, "DataSet");
        report.Prepare();
        using var page = report.PreparedPages.GetPage(0);
        foreach (var text in page.AllObjects.OfType<TextObject>()) Assert.Equal(sizes[text.Name], text.Font.Size);
        Assert.Contains(page.AllObjects.OfType<TextObject>(), text => text.FontWidthRatio < 1);
        using var bitmap = new Bitmap(688, 96); bitmap.SetResolution(203, 203);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White); g.PageUnit = GraphicsUnit.Millimeter;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            VectorLabelRenderer.DrawPreparedLabel(g, report);
            Assert.Equal(TextRenderingHint.ClearTypeGridFit, g.TextRenderingHint);
        }
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
        {
            int color = bitmap.GetPixel(x, y).ToArgb();
            Assert.True(color == Color.Black.ToArgb() || color == Color.White.ToArgb());
        }
    }
}
