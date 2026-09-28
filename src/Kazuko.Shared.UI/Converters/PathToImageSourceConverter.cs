using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Kazuko.Shared.UI.Converters;

/// <summary>
/// Converts a string path/URI (or null) to ImageSource without throwing when value is null.
/// </summary>
public sealed class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
            return DependencyProperty.UnsetValue;

        if (value is BitmapSource bitmap)
            return bitmap;

        var path = value.ToString();
        if (string.IsNullOrWhiteSpace(path))
            return DependencyProperty.UnsetValue;

        try
        {
            // Pack / relative component URIs used by module icons.
            if (path.StartsWith("/", StringComparison.Ordinal) ||
                path.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
            {
                var uri = new Uri(path, UriKind.RelativeOrAbsolute);
                return new BitmapImage(uri);
            }

            if (!File.Exists(path))
                return DependencyProperty.UnsetValue;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
