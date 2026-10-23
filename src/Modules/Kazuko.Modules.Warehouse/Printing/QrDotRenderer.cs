using System.Drawing;
using System.Drawing.Drawing2D;
using FastReport.Barcode;

namespace Kazuko.Modules.Warehouse.Printing;

/// <summary>Dùng bộ mã hóa FastReport hiện có, vẽ QR bằng module nguyên chấm của thiết bị.</summary>
internal static class QrDotRenderer
{
    internal static void Draw(Graphics graphics, BarcodeObject source)
    {
        if (string.IsNullOrEmpty(source.Text)) return;
        using var native = new BarcodeObject();
        native.Assign(source);
        native.Left = native.Top = 0;
        native.Angle = 0;
        native.AutoSize = true;
        native.ShowText = false;
        native.Zoom = 1;
        var qr = (BarcodeQR)native.Barcode;
        // Hiệu kích thước có/không quiet zone 4 module mỗi phía cho biết đơn vị
        // tự nhiên của một ô. Chỉ dùng API công khai, không đọc matrix nội bộ.
        qr.QuietZone = false;
        native.UpdateAutoSize();
        float symbolWidth = native.Width;
        qr.QuietZone = true;
        native.UpdateAutoSize();
        float unit = (native.Width - symbolWidth) / 8f;
        if (unit <= 0) throw new InvalidOperationException("Không xác định được lưới QR.");
        int modules = (int)Math.Round(native.Width / unit);

        const float mmPerPx = 25.4f / 96f;
        PointF[] bounds = [new(source.AbsLeft * mmPerPx, source.AbsTop * mmPerPx),
            new((source.AbsLeft + source.Width) * mmPerPx, (source.AbsTop + source.Height) * mmPerPx)];
        graphics.TransformPoints(CoordinateSpace.Device, CoordinateSpace.World, bounds);
        int left = (int)Math.Ceiling(bounds[0].X), top = (int)Math.Ceiling(bounds[0].Y);
        int width = (int)Math.Floor(bounds[1].X) - left;
        int height = (int)Math.Floor(bounds[1].Y) - top;
        int dots = Math.Min(width, height) / modules;
        if (dots < 2)
            throw new InvalidOperationException(
                $"QR quá nhỏ cho mã hàng này ở {graphics.DpiX:0} DPI. Hãy tăng khung QR lên ít nhất {modules * 2 * 25.4f / Math.Min(graphics.DpiX, graphics.DpiY):0.##} mm (gồm vùng trắng).");
        int side = modules * dots;
        left += (width - side) / 2;
        top += (height - side) / 2;
        PointF[] corners = [new(left, top), new(left + side, top + side)];
        graphics.TransformPoints(CoordinateSpace.World, CoordinateSpace.Device, corners);
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.TranslateTransform(corners[0].X, corners[0].Y);
            graphics.ScaleTransform((corners[1].X - corners[0].X) / native.Width,
                (corners[1].Y - corners[0].Y) / native.Height);
            graphics.FillRectangle(Brushes.White, 0, 0, native.Width, native.Height);
            VectorLabelRenderer.DrawNativeBarcode(graphics, native);
        }
        finally { graphics.Restore(state); }
    }
}
