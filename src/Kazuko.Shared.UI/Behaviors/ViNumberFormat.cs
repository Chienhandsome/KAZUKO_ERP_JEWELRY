using System.Globalization;
using System.Linq;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>
/// Định dạng và đọc số theo vi-VN: dấu chấm ngăn nghìn, dấu phẩy thập phân.
/// </summary>
public static class ViNumberFormat
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");

    public static string FormatInteger(decimal value) =>
        decimal.Truncate(value).ToString("N0", Culture);

    public static string FormatQuantity(decimal value)
    {
        if (value == decimal.Truncate(value))
            return value.ToString("N0", Culture);

        return value.ToString("#,0.####", Culture);
    }

    public static bool TryParseInteger(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0)
            return false;

        return decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseQuantity(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim().Replace(" ", string.Empty).TrimEnd(',', '.');
        if (trimmed.Length == 0)
            return false;

        return decimal.TryParse(trimmed, NumberStyles.Number, Culture, out value)
            || decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            || TryParseInteger(trimmed, out value);
    }

    public static bool TryFormatInput(string text, bool allowDecimal, int caret, out string formatted, out int newCaret)
    {
        formatted = text;
        newCaret = caret;
        if (string.IsNullOrEmpty(text))
            return false;

        return allowDecimal
            ? TryFormatDecimalInput(text, caret, out formatted, out newCaret)
            : TryFormatIntegerInput(text, caret, out formatted, out newCaret);
    }

    private static bool TryFormatIntegerInput(string text, int caret, out string formatted, out int newCaret)
    {
        formatted = text;
        newCaret = caret;
        var digitsBeforeCaret = text.Take(Math.Clamp(caret, 0, text.Length)).Count(char.IsDigit);
        var digitsOnly = new string(text.Where(char.IsDigit).ToArray());
        if (digitsOnly.Length == 0)
            return false;

        if (!decimal.TryParse(digitsOnly, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return false;

        formatted = FormatInteger(value);
        if (formatted == text)
            return false;

        newCaret = CaretAfterDigits(formatted, digitsBeforeCaret, inFraction: false);
        return true;
    }

    private static bool TryFormatDecimalInput(string text, int caret, out string formatted, out int newCaret)
    {
        formatted = text;
        newCaret = caret;
        var sliceLength = Math.Clamp(caret, 0, text.Length);
        if (!TrySplit(text, out var intDigits, out var fracDigits, out var hadDecimal))
            return false;

        if (intDigits.Length == 0 && fracDigits.Length == 0)
            return false;

        if (!decimal.TryParse(intDigits.Length == 0 ? "0" : intDigits, NumberStyles.None, CultureInfo.InvariantCulture, out var intValue))
            return false;

        var intFormatted = FormatInteger(intValue);
        formatted = hadDecimal ? intFormatted + "," + fracDigits : intFormatted;
        if (formatted == text)
            return false;

        TrySplit(text[..sliceLength], out var intBefore, out var fracBefore, out var caretInFraction);
        newCaret = CaretAfterDigits(formatted, caretInFraction ? fracBefore.Length : intBefore.Length, caretInFraction);
        return true;
    }

    private static bool TrySplit(string text, out string intDigits, out string fracDigits, out bool hadDecimal)
    {
        intDigits = string.Empty;
        fracDigits = string.Empty;
        hadDecimal = false;
        if (text.Contains(','))
        {
            hadDecimal = true;
            var comma = text.IndexOf(',');
            intDigits = Digits(text[..comma]);
            fracDigits = Digits(text[(comma + 1)..]);
            return true;
        }

        var dots = text.Count(ch => ch == '.');
        if (dots == 0)
        {
            intDigits = Digits(text);
            return true;
        }

        if (dots == 1)
        {
            var dot = text.IndexOf('.');
            var after = text[(dot + 1)..];
            var afterDigits = Digits(after);
            // Một dấu chấm và đúng 3 số phía sau là ngăn nghìn (1.250). 1 hoặc 2 số là thập phân (1.5, 1.25).
            if (afterDigits.Length == 3 && after.Trim().Length == 3)
            {
                intDigits = Digits(text);
                return true;
            }

            hadDecimal = true;
            intDigits = Digits(text[..dot]);
            fracDigits = afterDigits;
            return true;
        }

        intDigits = Digits(text);
        return true;
    }

    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());

    private static int CaretAfterDigits(string formatted, int digitsBefore, bool inFraction)
    {
        if (digitsBefore == 0 && !inFraction)
            return 0;

        var seenInt = 0;
        var seenFrac = 0;
        var passedSeparator = false;
        for (var i = 0; i < formatted.Length; i++)
        {
            var ch = formatted[i];
            if (ch == ',')
            {
                passedSeparator = true;
                if (inFraction && digitsBefore == 0)
                    return i + 1;
                continue;
            }

            if (!char.IsDigit(ch))
                continue;

            if (!passedSeparator)
            {
                seenInt++;
                if (!inFraction && seenInt == digitsBefore)
                    return i + 1;
            }
            else
            {
                seenFrac++;
                if (inFraction && seenFrac == digitsBefore)
                    return i + 1;
            }
        }

        return formatted.Length;
    }
}
