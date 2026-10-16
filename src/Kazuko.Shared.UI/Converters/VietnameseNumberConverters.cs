using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Kazuko.Shared.UI.Behaviors;

namespace Kazuko.Shared.UI.Converters;

/// <summary>
/// Tiền VND: hiển thị và đọc lại "1.000.000". Dùng thay StringFormat=N0 trên ô nhập,
/// vì binding WPF mặc định theo en-US nên hiểu dấu chấm là dấu thập phân.
/// </summary>
public sealed class VietnameseMoneyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (!TryRead(value, out var number))
            return string.Empty;

        return ViNumberFormat.FormatInteger(number);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string;
        if (string.IsNullOrWhiteSpace(text))
            return Zero(targetType);

        return ViNumberFormat.TryParseInteger(text, out var number)
            ? Cast(number, targetType)
            : Binding.DoNothing;
    }

    private static bool TryRead(object? value, out decimal number)
    {
        switch (value)
        {
            case decimal d:
                number = d;
                return true;
            case double d:
                number = (decimal)d;
                return true;
            case float f:
                number = (decimal)f;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            default:
                number = 0m;
                return false;
        }
    }

    private static object Zero(Type targetType) => Cast(0m, targetType);

    private static object Cast(decimal number, Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(double))
            return (double)number;
        if (type == typeof(float))
            return (float)number;
        if (type == typeof(int))
            return (int)number;
        if (type == typeof(long))
            return (long)number;
        return number;
    }
}

/// <summary>
/// Số lượng có thể lẻ: "1.250" hoặc "1.250,5". Dấu phẩy/chấm đang gõ dở không ghi đè nguồn.
/// </summary>
public sealed class VietnameseQuantityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not decimal and not double and not float and not int and not long)
            return string.Empty;

        decimal number = value switch
        {
            decimal d => d,
            double d => (decimal)d,
            float f => (decimal)f,
            int i => i,
            long l => l,
            _ => 0m
        };
        return ViNumberFormat.FormatQuantity(number);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = (value as string ?? string.Empty).Trim();
        if (text.Length == 0)
            return Zero(targetType);

        if (text.EndsWith(',') || text.EndsWith('.'))
            return Binding.DoNothing;

        if (!ViNumberFormat.TryParseQuantity(text, out var number))
            return Binding.DoNothing;

        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(double))
            return (double)number;
        if (type == typeof(float))
            return (float)number;
        if (type == typeof(int))
            return (int)number;
        if (type == typeof(long))
            return (long)number;
        return number;
    }

    private static object Zero(Type targetType)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (type == typeof(double))
            return 0d;
        if (type == typeof(float))
            return 0f;
        if (type == typeof(int))
            return 0;
        if (type == typeof(long))
            return 0L;
        return 0m;
    }
}
