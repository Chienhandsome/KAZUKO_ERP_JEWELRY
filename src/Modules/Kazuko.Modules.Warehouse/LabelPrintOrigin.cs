using System.Drawing;
using System.Drawing.Printing;

namespace Kazuko.Modules.Warehouse;

/// <summary>Gốc in tem: bù hard margin của driver, rồi cộng lệch dọc do người dùng nhập.</summary>
internal static class LabelPrintOrigin
{
    /// <summary>Dương đẩy nội dung xuống, âm kéo lên. Đơn vị mm trên Graphics đã đặt PageUnit = Millimeter.</summary>
    public static float VerticalShiftMm(float hardMarginYHundredthsOfInch, double verticalOffsetMm) =>
        (float)(-hardMarginYHundredthsOfInch * 0.254d + verticalOffsetMm);

    public static void Apply(Graphics graphics, PageSettings page, double verticalOffsetMm)
    {
        graphics.PageUnit = GraphicsUnit.Millimeter;
        graphics.TranslateTransform(
            -page.HardMarginX * 0.254f,
            VerticalShiftMm(page.HardMarginY, verticalOffsetMm));
    }
}
