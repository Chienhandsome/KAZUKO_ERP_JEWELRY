using System.Globalization;
using System.Text;

namespace Kazuko.Core.Printing;

/// <summary>Đọc số tiền VND thành chữ (đủ dùng cho hóa đơn).</summary>
public static class VietnameseMoneyText
{
    private static readonly string[] Digits =
    [
        "không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"
    ];

    private static readonly string[] Units = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ"];

    public static string ToDong(decimal amount)
    {
        var rounded = Math.Round(Math.Abs(amount), 0, MidpointRounding.AwayFromZero);
        if (rounded == 0)
            return "Không đồng";

        var words = ReadInteger(rounded);
        if (amount < 0)
            words = "Âm " + words;
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words) + " đồng";
    }

    private static string ReadInteger(decimal value)
    {
        var n = (long)value;
        if (n == 0)
            return Digits[0];

        var parts = new List<string>();
        var unitIndex = 0;
        while (n > 0 && unitIndex < Units.Length)
        {
            var block = (int)(n % 1000);
            n /= 1000;
            if (block > 0)
            {
                var blockText = ReadBlock(block, unitIndex > 0);
                if (!string.IsNullOrEmpty(Units[unitIndex]))
                    blockText = blockText + " " + Units[unitIndex];
                parts.Insert(0, blockText.Trim());
            }

            unitIndex++;
        }

        return string.Join(" ", parts).Replace("  ", " ").Trim();
    }

    private static string ReadBlock(int block, bool fullHundred)
    {
        var hundreds = block / 100;
        var tens = (block % 100) / 10;
        var ones = block % 10;
        var sb = new StringBuilder();

        if (hundreds > 0)
        {
            sb.Append(Digits[hundreds]).Append(" trăm");
        }
        else if (fullHundred && (tens > 0 || ones > 0))
        {
            sb.Append("không trăm");
        }

        if (tens > 1)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(Digits[tens]).Append(" mươi");
            if (ones == 1)
                sb.Append(" mốt");
            else if (ones == 5)
                sb.Append(" lăm");
            else if (ones > 0)
                sb.Append(' ').Append(Digits[ones]);
        }
        else if (tens == 1)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append("mười");
            if (ones == 5)
                sb.Append(" lăm");
            else if (ones > 0)
                sb.Append(' ').Append(Digits[ones]);
        }
        else if (ones > 0)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
                if (ones == 1)
                    sb.Append("mốt");
                else if (ones == 5 && hundreds > 0)
                    sb.Append("lăm");
                else
                    sb.Append(Digits[ones]);
            }
            else
            {
                sb.Append(Digits[ones]);
            }
        }

        return sb.ToString();
    }
}
