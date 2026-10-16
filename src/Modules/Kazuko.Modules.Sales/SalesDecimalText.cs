using System.Globalization;
using System.Linq;

namespace Kazuko.Modules.Sales;

internal static class SalesDecimalText
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var normalized = text.Trim().Replace(',', '.');
        // Cho phép gõ dở: "1." / "1,"
        if (normalized is "." or "-" or "-.")
            return false;

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);
    }

    public static string Format(decimal value, string format = "0.####") =>
        value == 0m
            ? string.Empty
            : value.ToString(format, CultureInfo.InvariantCulture);

    /// <summary>
    /// Tiền / số món: bỏ dấu ngăn nghìn (1.000.000) rồi đọc số nguyên.
    /// Không dùng cho trọng lượng, vì dấu chấm ở đó là thập phân.
    /// </summary>
    public static bool TryParseGrouped(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return false;

        return decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static string FormatGrouped(decimal value) =>
        value == 0m
            ? string.Empty
            : value.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
}
