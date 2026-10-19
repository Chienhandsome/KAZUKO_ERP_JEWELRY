using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class CraftOrderViewTests
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
    public void Craft_order_controls_load_with_app_styles(string theme, double scale)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var (vm, service) = CraftOrderWorkspaceTests.Create();
                service.Header = service.Header! with { StatusCode = "COMPLETED", DepositPaidVnd = 1405000000, HasFinishedGoodsIssue = true };
                vm.Items.Add(CraftOrderWorkspaceTests.SampleOrder with { StatusCode = "COMPLETED", PaidAmountVnd = 1405000000, HasFinishedGoodsIssue = true });
                vm.SelectedItem = vm.Items[0];
                var view = new CraftOrderTabView { DataContext = vm };
                foreach (var resource in new[] { theme, "Typography", "Controls" })
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
                foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle", "FontSize.Heading", "FontSize.Display" })
                    view.Resources[key] = (double)view.FindResource(key) * scale;
                view.FontFamily = (FontFamily)view.FindResource("AppFontFamily");
                view.FontSize = (double)view.FindResource("FontSize.Body");
                view.Foreground = (Brush)view.FindResource("TextPrimaryBrush");
                foreach (var size in new[] { new Size(1100, 720), new Size(1500, 850) })
                {
                    view.Measure(size);
                    view.Arrange(new Rect(size));
                    view.UpdateLayout();
                    var combo = Descendants(view).OfType<ComboBox>().Single();
                    Assert.Equal(view.FindResource("CraftOrderFilterComboStyle"), combo.Style);
                    Assert.NotNull(combo.Template.FindName("PART_Popup", combo));
                    Assert.Contains(Descendants(combo).OfType<TextBlock>(), text => text.Text == "Tất cả trạng thái");
                    var table = Descendants(view).OfType<DataGrid>().Single();
                    Assert.Equal(view.FindResource("SalesDataGridStyle"), table.Style);
                    Assert.Equal(6, table.Columns.Count);
                    Assert.Single(table.Items);
                    Assert.DoesNotContain(Descendants(view).OfType<Button>(), button => Equals(button.Content, "Tìm"));
                    var badges = Descendants(view).OfType<ContentControl>().Where(control => Equals(control.Tag, "COMPLETED")).ToList();
                    Assert.Equal(2, badges.Count);
                    foreach (var badge in badges)
                    {
                        Assert.Equal("Hoàn tất", badge.Content);
                        Assert.Equal(view.FindResource("SuccessBrush"), badge.Foreground);
                    }
                    var zeroBalance = Descendants(view).OfType<TextBlock>().Single(text => text.Text == "0 đ");
                    Assert.Equal(view.FindResource("SuccessBrush"), zeroBalance.Foreground);
                    var captureDirectory = Environment.GetEnvironmentVariable("KAZUKO_CRAFT_UI_CAPTURE_DIR");
                    if (!string.IsNullOrWhiteSpace(captureDirectory))
                    {
                        Directory.CreateDirectory(captureDirectory);
                        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                        bitmap.Render(view);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(captureDirectory, $"craft-{theme}-{scale:0.00}-{size.Width}.png"));
                        encoder.Save(output);
                    }
                }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "WPF render timed out.");
        if (failure is not null) throw new Exception("Craft-order WPF check failed.", failure);
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
