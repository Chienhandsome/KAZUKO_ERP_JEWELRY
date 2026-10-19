using System.Globalization;
using System.Windows.Data;
using FastReport;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>Chuyển HorzAlign sang tiếng Việt cho ComboBox.</summary>
public sealed class HorzAlignConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is HorzAlign align ? align switch
        {
            HorzAlign.Left => "Trái",
            HorzAlign.Center => "Giữa",
            HorzAlign.Right => "Phải",
            HorzAlign.Justify => "Căn đều",
            _ => value.ToString() ?? string.Empty,
        } : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Chuyển VertAlign sang tiếng Việt cho ComboBox.</summary>
public sealed class VertAlignConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is VertAlign align ? align switch
        {
            VertAlign.Top => "Trên",
            VertAlign.Center => "Giữa",
            VertAlign.Bottom => "Dưới",
            _ => value.ToString() ?? string.Empty,
        } : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
