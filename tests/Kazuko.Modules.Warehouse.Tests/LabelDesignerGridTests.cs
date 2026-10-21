using Kazuko.Modules.Warehouse.LabelDesigner;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class LabelDesignerGridTests
{
    [Fact]
    public void Snap_rounds_to_nearest_millimeter()
    {
        var step = LabelDesignerGrid.ScreenStep(LabelDesignerViewModel.CanvasScale);
        Assert.Equal(0, LabelDesignerGrid.SnapScreen(step * 0.4, LabelDesignerViewModel.CanvasScale, true), 3);
        Assert.Equal(step, LabelDesignerGrid.SnapScreen(step * 0.6, LabelDesignerViewModel.CanvasScale, true), 3);
        Assert.Equal(step * 2, LabelDesignerGrid.SnapScreen(step * 2, LabelDesignerViewModel.CanvasScale, true), 3);
    }

    [Fact]
    public void Snap_keeps_position_when_disabled()
    {
        Assert.Equal(12.3, LabelDesignerGrid.SnapScreen(12.3, LabelDesignerViewModel.CanvasScale, false));
    }

    [Fact]
    public void Paper_brush_is_frozen()
    {
        const double scale = LabelDesignerViewModel.CanvasScale;
        var step = LabelDesignerGrid.ScreenStep(scale);
        var brush = LabelDesignerGrid.CreatePaperBrush(step * 86, step * 12, scale, 86, 12);
        Assert.True(brush.IsFrozen);
    }
}
