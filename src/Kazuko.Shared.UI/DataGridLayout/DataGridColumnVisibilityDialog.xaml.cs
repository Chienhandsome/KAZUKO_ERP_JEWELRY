using Kazuko.Core.Settings;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kazuko.Core.Mvvm;

namespace Kazuko.Shared.UI.DataGridLayout;

public partial class DataGridColumnVisibilityDialog : Window
{
    private readonly DataGrid _dataGrid;
    private readonly ObservableCollection<DataGridColumnVisibilityItem> _items = [];

    public DataGridColumnVisibilityDialog(DataGrid dataGrid, string settingsKey)
    {
        _dataGrid = dataGrid ?? throw new ArgumentNullException(nameof(dataGrid));
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsKey);

        InitializeComponent();
        LoadColumns();
        ColumnList.ItemsSource = _items;
        ColumnList.SelectionChanged += (_, _) => UpdateMoveButtons();
        ColumnList.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(ColumnList_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        UpdateMoveButtons();
    }

    private void ColumnList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var item = FindAncestor<ListBoxItem>(source);
        if (item is not null)
            item.IsSelected = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void LoadColumns()
    {
        _items.Clear();
        foreach (var column in _dataGrid.Columns.OrderBy(static c => c.DisplayIndex))
        {
            var key = DataGridColumnLayout.GetColumnKey(column);
            var header = DataGridColumnLayout.ResolveHeader(column);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (string.IsNullOrWhiteSpace(header))
                header = key is "Select" ? "Chọn dòng" : "Thao tác";

            _items.Add(new DataGridColumnVisibilityItem(
                column,
                key,
                header,
                column.Visibility == Visibility.Visible));
        }
    }

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int delta)
    {
        var index = ColumnList.SelectedIndex;
        if (index < 0)
            return;

        var target = index + delta;
        if (target < 0 || target >= _items.Count)
            return;

        _items.Move(index, target);
        ColumnList.SelectedIndex = target;
        ColumnList.ScrollIntoView(ColumnList.SelectedItem);
        UpdateMoveButtons();
    }

    private void UpdateMoveButtons()
    {
        var index = ColumnList.SelectedIndex;
        MoveUpButton.IsEnabled = index > 0;
        MoveDownButton.IsEnabled = index >= 0 && index < _items.Count - 1;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_items.Any(item => item.IsVisible))
        {
            MessageBox.Show(this, "Cần giữ ít nhất một cột hiển thị.", "Cột hiển thị",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var settings = new DataGridColumnLayoutSettings(
            _items.Select((item, index) =>
                    new DataGridColumnSetting(item.Key, item.IsVisible, index))
                .ToArray());

        DataGridColumnLayout.Apply(_dataGrid, settings);
        DataGridColumnLayout.Persist(_dataGrid);

        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            DialogResult = false;
            Close();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Enter)
        {
            ApplyButton_Click(sender, e);
            e.Handled = true;
        }
    }
}

public sealed class DataGridColumnVisibilityItem : ObservableObject
{
    private bool _isVisible;

    public DataGridColumnVisibilityItem(DataGridColumn column, string key, string header, bool isVisible)
    {
        Column = column;
        Key = key;
        Header = header;
        _isVisible = isVisible;
    }

    public DataGridColumn Column { get; }
    public string Key { get; }
    public string Header { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
