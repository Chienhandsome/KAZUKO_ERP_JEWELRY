using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Reports.Shared;

public partial class ReportColumnVisibilityDialog : Window
{
    private readonly IReadOnlyList<ReportColumnDefinition> _available;
    private readonly string _settingsKey;
    private readonly ObservableCollection<ReportColumnVisibilityItem> _items = [];

    public ReportColumnVisibilityDialog(
        IReadOnlyList<ReportColumnDefinition> available,
        IReadOnlyList<ReportColumnDefinition> activeVisible,
        string settingsKey)
    {
        _available = available ?? throw new ArgumentNullException(nameof(available));
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsKey);
        _settingsKey = settingsKey;

        InitializeComponent();

        foreach (var (column, isVisible) in ReportColumnLayout.BuildDialogItems(available, activeVisible))
            _items.Add(new ReportColumnVisibilityItem(column.Key, column.Header, isVisible));

        ColumnList.ItemsSource = _items;
        ColumnList.SelectionChanged += (_, _) => UpdateMoveButtons();
        ColumnList.AddHandler(
            UIElement.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(ColumnList_PreviewMouseLeftButtonDown),
            handledEventsToo: true);
        UpdateMoveButtons();
    }

    public IReadOnlyList<ReportColumnDefinition>? ResultColumns { get; private set; }

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
        var byKey = _available.ToDictionary(c => c.Key, StringComparer.OrdinalIgnoreCase);
        var visible = new List<ReportColumnDefinition>();
        foreach (var item in _items)
        {
            if (!item.IsVisible)
                continue;
            if (byKey.TryGetValue(item.Key, out var def))
                visible.Add(def);
        }

        if (visible.Count == 0)
        {
            MessageBox.Show(
                this,
                "Cần chọn ít nhất một cột để hiển thị trên báo cáo.",
                "Cột hiển thị",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ReportColumnLayout.Save(_settingsKey, _available, visible);
        ResultColumns = visible;
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

public sealed class ReportColumnVisibilityItem : ObservableObject
{
    private bool _isVisible;

    public ReportColumnVisibilityItem(string key, string header, bool isVisible)
    {
        Key = key;
        Header = header;
        _isVisible = isVisible;
    }

    public string Key { get; }
    public string Header { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }
}
