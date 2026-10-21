using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class LabelPrintOffsetTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("0", 0)]
    [InlineData("1,5", 1.5)]
    [InlineData("-0.5", -0.5)]
    [InlineData("20", 20)]
    [InlineData("-20", -20)]
    public void Parses_vertical_offset(string text, double expected)
    {
        Assert.True(LabelPrintDialogViewModel.TryParseVerticalOffset(text, out var millimeters));
        Assert.Equal(expected, millimeters);
    }

    [Theory]
    [InlineData("21")]
    [InlineData("-21")]
    [InlineData("abc")]
    public void Rejects_offset_outside_range_or_not_a_number(string text)
    {
        Assert.False(LabelPrintDialogViewModel.TryParseVerticalOffset(text, out _));
    }

    [Fact]
    public void Positive_offset_moves_content_down_after_hard_margin()
    {
        var shift = LabelPrintOrigin.VerticalShiftMm(hardMarginYHundredthsOfInch: 10, verticalOffsetMm: 1.5);
        Assert.Equal((float)(-10 * 0.254 + 1.5), shift);
    }
}
