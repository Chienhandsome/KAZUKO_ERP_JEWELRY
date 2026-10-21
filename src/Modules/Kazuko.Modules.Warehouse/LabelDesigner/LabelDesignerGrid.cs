using System.Windows;
using System.Windows.Media;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>
/// Lưới căn trên mặt tem (không đưa vào file in). Bước 1 mm, nét đậm mỗi 5 mm.
/// </summary>
internal static class LabelDesignerGrid
{
    public const double StepMm = 1;
    public const int MajorEveryMm = 5;
    private const double PxPerMm = 96.0 / 25.4;

    public static double ScreenStep(double scale) => PxPerMm * scale * StepMm;

    /// <summary>Bám tọa độ màn hình vào đường lưới. Tắt thì giữ nguyên.</summary>
    public static double SnapScreen(double screenPx, double scale, bool enabled)
    {
        if (!enabled)
            return screenPx;
        var step = ScreenStep(scale);
        if (step <= 0)
            return screenPx;
        return Math.Round(screenPx / step, MidpointRounding.AwayFromZero) * step;
    }

    /// <summary>Nền giấy trắng kèm lưới. Tọa độ tuyệt đối theo px màn hình của canvas.</summary>
    public static Brush CreatePaperBrush(double width, double height, double scale, int widthMm, int heightMm)
    {
        var step = ScreenStep(scale);
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(
            Brushes.White,
            null,
            new RectangleGeometry(new Rect(0, 0, width, height))));

        var minor = new GeometryGroup();
        var major = new GeometryGroup();
        for (var mm = 1; mm < widthMm; mm++)
        {
            var line = new LineGeometry(new Point(mm * step, 0), new Point(mm * step, height));
            (mm % MajorEveryMm == 0 ? major : minor).Children.Add(line);
        }

        for (var mm = 1; mm < heightMm; mm++)
        {
            var line = new LineGeometry(new Point(0, mm * step), new Point(width, mm * step));
            (mm % MajorEveryMm == 0 ? major : minor).Children.Add(line);
        }

        // Nét lưới nằm trên giấy in, không lấy màu theme: giấy luôn trắng.
        group.Children.Add(new GeometryDrawing(null, Pen(0xD4, 0xD4, 0xD4), minor));
        group.Children.Add(new GeometryDrawing(null, Pen(0xA8, 0xA8, 0xA8), major));
        group.Children.Add(new GeometryDrawing(
            null,
            Pen(0x6E, 0x6E, 0x6E),
            new RectangleGeometry(new Rect(0.5, 0.5, Math.Max(0, width - 1), Math.Max(0, height - 1)))));

        var brush = new DrawingBrush(group)
        {
            Stretch = Stretch.None,
            AlignmentX = AlignmentX.Left,
            AlignmentY = AlignmentY.Top,
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, width, height),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, width, height),
        };
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        var pen = new Pen(brush, 1);
        pen.Freeze();
        return pen;
    }
}
