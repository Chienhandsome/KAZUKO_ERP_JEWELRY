using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kazuko.Shared.UI.DataGridLayout;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CustomerTabsViewTests
{
    [Theory]
    [InlineData("Light", 0.9)]
    [InlineData("Light", 1.0)]
    [InlineData("Light", 1.25)]
    [InlineData("Light", 1.5)]
    [InlineData("Dark", 0.9)]
    [InlineData("Dark", 1.0)]
    [InlineData("Dark", 1.25)]
    [InlineData("Dark", 1.5)]
    public void Tabs_render_with_shared_theme_fonts_and_consistent_table_and_button_styles(string theme, double scale)
    {
        CustomerTestContext.Sta(() =>
        {
            var repo = new CustomerRepositoryFake
            {
                Customers = [CustomerRepositoryFake.Sample(), CustomerRepositoryFake.Sample(2) with { IsWalkIn = true, Name = "Khách vãng lai" }]
            };
            var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
            var view = new CustomerView { DataContext = vm };
            foreach (var resource in new[] { theme, "Typography", "Controls" })
                view.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
            foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle", "FontSize.Heading", "FontSize.Display" })
                view.Resources[key] = (double)view.FindResource(key) * scale;
            view.FontFamily = (FontFamily)view.FindResource("AppFontFamily");
            view.FontSize = (double)view.FindResource("FontSize.Body");
            view.Foreground = (Brush)view.FindResource("TextPrimaryBrush");
            foreach (var size in new[] { new Size(900, 520), new Size(1500, 900) })
            foreach (var tab in new[] { 0, 1, 2, 3 })
            {
                if (tab == 0) { vm.SelectedKind = "Tất cả"; vm.SearchCommand.Execute(null); }
                vm.SelectedTabIndex = tab;
                if (tab == 1) vm.HistoryTab.SelectedCustomer = vm.HistoryTab.CustomerOptions[0];
                if (tab == 2 && vm.DebtTab.PaymentHistory.Count == 0)
                    vm.DebtTab.PaymentHistory.Add(new(new(DateTime.Today, "MB26/00001", "PAYMENT / CASH", 25470000, 0, 0)));
                view.Measure(size);
                view.Arrange(new Rect(size));
                view.UpdateLayout();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                view.UpdateLayout();
                var grids = Descendants(view).OfType<DataGrid>().ToList();
                Assert.Equal(tab == 3 ? 5 : tab == 0 ? 2 : 1, grids.Count);
                foreach (var grid in grids)
                {
                    if (tab == 0) continue;
                    Assert.Same(view.FindResource("WorkspaceGridStyle"), grid.Style);
                    Assert.Same(view.FindResource("WorkspaceHeaderStyle"), grid.ColumnHeaderStyle);
                    Assert.Equal(28, grid.RowHeight);
                    Assert.Equal(32, grid.ColumnHeaderHeight);
                    Assert.False(grid.CanUserAddRows);
                    Assert.Equal(grid.Columns.Count, grid.Columns.Select(DataGridColumnLayout.GetColumnKey).Where(x => !string.IsNullOrEmpty(x)).Distinct().Count());
                }
                foreach (var button in Descendants(view).OfType<Button>().Where(x => tab != 0 && Equals(x.Content, "Xuất Excel")))
                {
                    Assert.Same(view.FindResource("WorkspaceExportStyle"), button.Style);
                    Assert.True(button.ActualHeight >= 40);
                }
                foreach (var date in Descendants(view).OfType<DatePicker>())
                {
                    Assert.Same(view.FindResource("ReportDateStyle"), date.Style);
                    Assert.IsType<DatePickerTextBox>(date.Template.FindName("PART_TextBox", date));
                    Assert.IsType<Popup>(date.Template.FindName("PART_Popup", date));
                    Assert.NotNull(date.Template.FindName("PART_Button", date));
                }
                Capture(view, theme, scale, size, tab);
            }
            repo.Top = [];
            repo.Customers = [];
            vm.SelectedTabIndex = 2;
            vm.SelectedTabIndex = 3;
            view.UpdateLayout();
            Assert.False(vm.ExportTopCustomersCommand.CanExecute(null));
            Assert.False(vm.ExportInactiveCustomersCommand.CanExecute(null));
            Assert.False(vm.ExportDistributionCommand.CanExecute(null));
            Capture(view, theme, scale, new Size(1500, 900), 4);
        });
    }

    private static void Capture(CustomerView view, string theme, double scale, Size size, int tab)
    {
        var directory = Environment.GetEnvironmentVariable("KAZUKO_CUSTOMER_UI_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, $"customers-{tab}-{theme}-{scale:0.00}-{size.Width}.png"));
        encoder.Save(output);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
