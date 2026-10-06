using System.Globalization;

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
}
