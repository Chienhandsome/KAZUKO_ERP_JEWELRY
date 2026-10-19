using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Kazuko.Shared.UI.Behaviors;
using Xunit;

namespace Kazuko.Shared.UI.Tests;

public sealed class DataGridRowNumberTests
{
    [Fact]
    public void Numbers_follow_actual_indexes_after_recycling_sorting_and_removal()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var items = new ObservableCollection<Item>(Enumerable.Range(1, 200).Select(x => new Item(x)));
                var grid = new DataGrid { ItemsSource = items, AutoGenerateColumns = false,
                    CanUserAddRows = false, EnableRowVirtualization = true, RowHeight = 30, RowHeaderWidth = 0 };
                VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
                grid.Columns.Add(new DataGridTextColumn { Header = "#", Binding = new Binding("Header")
                { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(DataGridRow), 1) } });
                grid.Columns.Add(new DataGridTextColumn { Header = "Item", Binding = new Binding("Id") });
                DataGridRowNumberBehavior.SetIsEnabled(grid, true);
                host = new Window { Content = grid, Width = 600, Height = 300,
                    Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None };
                host.Show();
                Layout();
                foreach (var index in new[] { 199, 0, 120, 0, 199, 0 })
                {
                    grid.ScrollIntoView(grid.Items[index]);
                    Layout();
                    CheckRows();
                }
                grid.Items.SortDescriptions.Add(new SortDescription("Id", ListSortDirection.Descending));
                grid.ScrollIntoView(grid.Items[0]);
                Layout();
                CheckRows();
                items.Remove(items[^1]);
                Layout();
                CheckRows();
                DataGridRowNumberBehavior.SetIsEnabled(grid, false);

                void Layout()
                {
                    grid.Measure(new Size(600, 300)); grid.Arrange(new Rect(0, 0, 600, 300)); grid.UpdateLayout();
                    var frame = new DispatcherFrame();
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
                        new Action(() => frame.Continue = false));
                    Dispatcher.PushFrame(frame);
                    grid.UpdateLayout();
                }
                void CheckRows()
                {
                    var realized = Enumerable.Range(0, grid.Items.Count)
                        .Select(x => grid.ItemContainerGenerator.ContainerFromIndex(x)).OfType<DataGridRow>().ToList();
                    Assert.NotEmpty(realized);
                    Assert.True(realized.Count < grid.Items.Count, "The test must exercise virtualized rows.");
                    foreach (var row in realized) Assert.Equal(row.GetIndex() + 1, Assert.IsType<int>(row.Header));
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { host?.Close(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "Row-number test timed out.");
        if (failure is not null) throw new Exception("Row-number regression failed.", failure);
    }
    private sealed record Item(int Id);
}
