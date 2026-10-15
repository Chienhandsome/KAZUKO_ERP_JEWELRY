using Kazuko.Core.Finance;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Finance;

/// <summary>
/// Wave 4 — spec #10.5: màu sắc theo hướng công nợ.
/// Logic thuần, chạy được trên Linux.
///
/// Converter thật (DebtDirectionToBrushConverter trong
/// src/Modules/Kazuko.Modules.Debt/DebtConverters.cs) là WPF IValueConverter và gọi
/// Application.Current.TryFindResource(...) — cần WPF Application, KHÔNG khởi tạo được
/// trên testhost Linux. Test này mirror đúng bảng quyết định của Convert() ở mức
/// resource key (SuccessBrush / DangerBrush / TextSecondaryBrush, fallback Brushes.Gray),
/// là phần logic duy nhất có thể kiểm chứng ngoài WPF.
/// </summary>
public sealed class DebtDirectionColorMappingTests
{
    /// <summary>
    /// Mirror DebtDirectionToBrushConverter.Convert — trả về resource key thay vì Brush.
    /// Production:
    ///   if (amount > 0) → Direction == Pay ? "DangerBrush" : "SuccessBrush"
    ///   else → "TextSecondaryBrush"
    /// </summary>
    private static string ResolveBrushKey(string direction, decimal amount)
    {
        if (amount > 0)
        {
            var brushKey = string.Equals(
                direction, DebtLedgerDirections.Pay, StringComparison.OrdinalIgnoreCase)
                ? "DangerBrush"
                : "SuccessBrush";
            return brushKey;
        }

        return "TextSecondaryBrush";
    }

    [Theory]
    [InlineData("RECEIVE", 1_000_000, "SuccessBrush")]   // phải thu > 0 → xanh lá
    [InlineData("PAY", 1_000_000, "DangerBrush")]        // phải trả > 0 → đỏ
    [InlineData("RECEIVE", 0, "TextSecondaryBrush")]     // = 0 → trung tính
    [InlineData("PAY", 0, "TextSecondaryBrush")]         // = 0 → trung tính
    [InlineData("RECEIVE", -500_000, "TextSecondaryBrush")] // âm → trung tính (không tô theo dấu)
    [InlineData("PAY", -500_000, "TextSecondaryBrush")]
    public void DirectionColorMapping(string direction, decimal amount, string expectedKey)
    {
        Assert.Equal(expectedKey, ResolveBrushKey(direction, amount));
    }

    [Fact]
    public void PayDirection_IsCaseInsensitive()
    {
        Assert.Equal("DangerBrush", ResolveBrushKey("pay", 100));
        Assert.Equal("DangerBrush", ResolveBrushKey("Pay", 100));
    }

    [Fact]
    public void UnknownDirection_FallsBackToReceiveColor()
    {
        // Production: chỉ đúng Pay (case-insensitive) mới lấy DangerBrush,
        // mọi giá trị khác → SuccessBrush khi amount > 0.
        Assert.Equal("SuccessBrush", ResolveBrushKey("UNKNOWN", 100));
        Assert.Equal("SuccessBrush", ResolveBrushKey("", 100));
    }

    [Fact]
    public void ConverterDefaultDirection_IsReceive()
    {
        // Production: public string Direction { get; set; } = DebtLedgerDirections.Receive;
        Assert.Equal("RECEIVE", DebtLedgerDirections.Receive);
        Assert.Equal("SuccessBrush", ResolveBrushKey(DebtLedgerDirections.Receive, 1));
        Assert.Equal("DangerBrush", ResolveBrushKey(DebtLedgerDirections.Pay, 1));
    }

    [Fact]
    public void ColorDependsOnDirection_NotOnSign()
    {
        // Ý đồ thiết kế (ghi trong XML doc của converter): "Tô màu số tiền theo HƯỚNG
        // công nợ, không theo dấu âm/dương".
        Assert.Equal(ResolveBrushKey("RECEIVE", 5_000_000), ResolveBrushKey("RECEIVE", 1));
        Assert.Equal(ResolveBrushKey("PAY", 5_000_000), ResolveBrushKey("PAY", 1));
        Assert.NotEqual(
            ResolveBrushKey("RECEIVE", 5_000_000),
            ResolveBrushKey("PAY", 5_000_000));
    }
}
