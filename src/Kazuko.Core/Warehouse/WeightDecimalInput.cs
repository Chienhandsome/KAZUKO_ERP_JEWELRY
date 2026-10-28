using System.Globalization;

namespace Kazuko.Core.Warehouse;

/// <summary>Plain weight input: either decimal separator, never thousands grouping or machine culture.</summary>
public static class WeightDecimalInput
{
    public static bool TryParse(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var input = text.Trim();
        if (input.Count(c => c is '.' or ',') > 1) return false;
        var separator=input.IndexOfAny(['.',',']);
        if(separator>=0&&input.Length-separator-1>4)return false;
        return decimal.TryParse(input.Replace(',', '.'),
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    public static string Format(decimal value) => value.ToString("0.####", CultureInfo.GetCultureInfo("vi-VN"));
}
