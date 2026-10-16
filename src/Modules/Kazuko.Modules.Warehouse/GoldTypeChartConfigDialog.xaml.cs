using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace Kazuko.Modules.Warehouse;

/// <summary>Một loại vàng/NVL trong dialog cấu hình biểu đồ.</summary>
public sealed class GoldTypeChartTypeItem
{
    public GoldTypeChartTypeItem(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
    public bool IsChecked { get; set; }
}

/// <summary>
/// Dialog cấu hình loại vàng hiển thị trên biểu đồ cột tồn kho.
/// Danh sách loại được truyền từ ngoài (danh mục GOLD_TYPES + Bạc/Đá quý/Khác).
/// </summary>
public partial class GoldTypeChartConfigDialog : Window
{
    public GoldTypeChartConfigDialog(
        IReadOnlyList<GoldTypeChartTypeItem> allTypes,
        IEnumerable<string> visibleKeys)
    {
        InitializeComponent();
        var keys = new HashSet<string>(visibleKeys, System.StringComparer.OrdinalIgnoreCase);
        TypeItems = new ObservableCollection<GoldTypeChartTypeItem>(allTypes);
        foreach (var item in TypeItems)
            item.IsChecked = keys.Contains(item.Key);
    }

    public ObservableCollection<GoldTypeChartTypeItem> TypeItems { get; }

    /// <summary>Danh sách key được chọn, null nếu người dùng bấm Hủy.</summary>
    public List<string>? SelectedKeys { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = TypeItems.Where(x => x.IsChecked).Select(x => x.Key).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show("Vui lòng chọn ít nhất một loại để hiển thị.",
                "Cấu hình biểu đồ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedKeys = selected;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
