using System.Windows;
using System.Windows.Controls;
using System.IO;
using System.Xml.Linq;
using Kazuko.Core.Settings;
using Kazuko.Shared.UI.DataGridLayout;
using Xunit;

namespace Kazuko.Shared.UI.Tests;

public sealed class DataGridColumnLayoutTests
{
    [Fact]
    public void Every_application_grid_has_a_unique_table_key_menu_and_stable_column_keys()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        XNamespace ui = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace layout = "clr-namespace:Kazuko.Shared.UI.DataGridLayout;assembly=Kazuko.Shared.UI";
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root.FullName, "src"), "*.xaml", SearchOption.AllDirectories)
                     .Where(file => !file.Contains("\\obj\\") && !file.Contains("\\bin\\")))
        foreach (var grid in XDocument.Load(file).Descendants(ui + "DataGrid"))
        {
            var key = (string?)grid.Attribute(layout + "DataGridColumnLayout.SettingsKey");
            Assert.False(string.IsNullOrWhiteSpace(key), file);
            Assert.True(tables.Add(key!), $"Duplicate table key: {key}");
            Assert.Equal("True", (string?)grid.Attribute(layout + "DataGridColumnLayout.EnableHeaderMenu"));
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in grid.Element(ui + "DataGrid.Columns")!.Elements())
            {
                var columnKey = (string?)column.Attribute(layout + "DataGridColumnLayout.Key");
                Assert.False(string.IsNullOrWhiteSpace(columnKey), $"{file}: {column}");
                Assert.True(columns.Add(columnKey!), $"{key}: duplicate column {columnKey}");
            }
        }
        Assert.True(tables.Count >= 78);
    }

    [Fact]
    public void Changed_layout_survives_unload_and_a_new_grid_instance_without_losing_widths()
    {
        RunSta(() =>
        {
            var store = new MemoryStore();
            DataGridColumnLayoutStore.Configure(store);
            var grid = CreateGrid();
            grid.Columns[0].Width = 214;
            grid.Columns[1].Visibility = Visibility.Collapsed;
            grid.Columns[1].DisplayIndex = 0;
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            Assert.Single(store.Values);

            var reopened = CreateGrid();
            Assert.Equal(214, reopened.Columns[0].Width.Value);
            Assert.Equal(Visibility.Collapsed, reopened.Columns[1].Visibility);
            Assert.Equal(0, reopened.Columns[1].DisplayIndex);
            var dialog = new DataGridColumnVisibilityDialog(reopened, "test.table")
            {
                Left = -32000, Top = -32000, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual
            };
            Exception? dialogFailure = null;
            dialog.Loaded += (_, _) =>
            {
                try
                {
                    var list = (ListBox)dialog.FindName("ColumnList");
                    foreach (var item in list.Items.Cast<DataGridColumnVisibilityItem>()) item.IsVisible = true;
                    FindButton(dialog, "Áp dụng")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { dialogFailure = ex; dialog.Close(); }
            };
            dialog.ShowDialog();
            if (dialogFailure is not null) throw dialogFailure;
            var again = CreateGrid();
            Assert.Equal(214, again.Columns[0].Width.Value);
            Assert.Equal(Visibility.Visible, again.Columns[1].Visibility);
            Assert.NotNull(again.ColumnHeaderStyle);
            reopened.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            again.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        });
    }

    private static Button? FindButton(DependencyObject parent, string content)
    {
        if (parent is Button button && Equals(button.Content, content)) return button;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
            if (FindButton(child, content) is { } match) return match;
        return null;
    }

    [Fact]
    public void Closing_flushes_pending_changes_to_the_store_bound_when_the_grid_was_opened()
    {
        RunSta(() =>
        {
            var firstUser = new MemoryStore();
            var secondUser = new MemoryStore();
            DataGridColumnLayoutStore.Configure(firstUser);
            var grid = CreateGrid();
            DataGridColumnLayoutStore.Configure(secondUser);
            grid.Columns[0].Width = 245;
            DataGridColumnLayout.FlushPendingLayouts(grid);
            Assert.Empty(secondUser.Values);
            Assert.Equal(245, firstUser.Values["test.table"].Columns[0].Width);
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
        });
    }

    private static DataGrid CreateGrid()
    {
        var grid = new DataGrid { AutoGenerateColumns = false };
        foreach (var key in new[] { "code", "name" })
        {
            var column = new DataGridTextColumn { Header = key, Width = 100 };
            DataGridColumnLayout.SetKey(column, key);
            grid.Columns.Add(column);
        }
        DataGridColumnLayout.SetSettingsKey(grid, "test.table");
        DataGridColumnLayout.SetEnableHeaderMenu(grid, true);
        grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        return grid;
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)));
        if (failure is not null) throw new Exception("Column layout regression failed.", failure);
    }

    private sealed class MemoryStore : ITableColumnLayoutStore
    {
        public Dictionary<string, DataGridColumnLayoutSettings> Values { get; } = new();
        public DataGridColumnLayoutSettings? Load(string settingsKey) => Values.GetValueOrDefault(settingsKey);
        public void Save(string settingsKey, DataGridColumnLayoutSettings settings) => Values[settingsKey] = settings;
    }
}
