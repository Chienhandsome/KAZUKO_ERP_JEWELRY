using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Kazuko.Modules.Warehouse.Converters;

/// <summary>Maps theme brush resource keys (e.g. ChartBrush.Raw) to brushes for chart legends.</summary>
public sealed class BrushKeyToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        if (string.IsNullOrWhiteSpace(key))
            return DependencyProperty.UnsetValue;

        if (Application.Current?.TryFindResource(key) is Brush brush)
            return brush;

        return Application.Current?.TryFindResource("PrimaryBrush") as Brush
               ?? Brushes.Orange;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

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

/// <summary>
/// Chuyển AlternationIndex (0-based) của DataGridRow thành STT (1-based) cho cột "Món".
/// </summary>
public sealed class AlternationIndexToSttConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index)
            return (index + 1).ToString(CultureInfo.CurrentCulture);
        return "—";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// So sánh 2 object cho cột checkbox phản ánh dòng đang chọn trong DataGrid.
/// </summary>
public sealed class ObjectEqualsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length >= 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
