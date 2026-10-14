using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Production.Converters;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert)
            flag = !flag;
        return flag ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not true;
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string text && string.IsNullOrWhiteSpace(text));
        if (Invert)
            empty = !empty;
        return empty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StatusCodeToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var code = value as string;
        return code switch
        {
            ProductionOrderStatuses.InProgress => new SolidColorBrush(Color.FromRgb(0x2F, 0x80, 0xED)),
            ProductionOrderStatuses.Completed => new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x5B)),
            ProductionOrderStatuses.Cancelled => new SolidColorBrush(Color.FromRgb(0xD3, 0x3F, 0x3F)),
            ProductionOrderStatuses.Draft => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
            _ => new SolidColorBrush(Color.FromRgb(0x8A, 0x8A, 0x8A)),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
