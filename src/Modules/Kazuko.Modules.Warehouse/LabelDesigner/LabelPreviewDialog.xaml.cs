using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Kazuko.Modules.Warehouse.LabelDesigner;

/// <summary>Dialog hiển thị ảnh xem trước mẫu tem (render PNG từ FastReport).</summary>
public partial class LabelPreviewDialog : Window
{
    public LabelPreviewDialog(byte[] pngBytes)
    {
        InitializeComponent();

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(pngBytes);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        PreviewImage.Source = bitmap;
    }
}
