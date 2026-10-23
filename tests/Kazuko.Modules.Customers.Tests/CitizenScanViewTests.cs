using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kazuko.Core.Customers;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CitizenScanViewTests
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
    public void Scan_review_and_actions_render_at_small_window_large_fonts_and_multiple_dpi(string theme, double scale)
    {
        CustomerTestContext.Sta(() =>
        {
            var repo = new CustomerRepositoryFake();
            var vm = new CitizenScanDialogViewModel(repo, CustomerTestContext.Session().Current!, null,
                new("Khách đang nhập", "999999999999", null, "", "", null), true);
            var dialog = new CitizenScanDialog { DataContext = vm };
            var view = (FrameworkElement)dialog.Content;
            foreach (System.Collections.DictionaryEntry resource in dialog.Resources) view.Resources[resource.Key] = resource.Value;
            foreach (var resource in new[] { theme, "Typography", "Controls" })
                view.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
            foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle", "FontSize.Heading", "FontSize.Display" })
                view.Resources[key] = (double)view.FindResource(key) * scale;
            dialog.Content = null;
            view.DataContext = vm;
            var surface = new ContentControl { Content = view,
                FontFamily = (FontFamily)view.FindResource("AppFontFamily"),
                FontSize = (double)view.FindResource("FontSize.Body"),
                Foreground = (Brush)view.FindResource("TextPrimaryBrush"),
                Background = (Brush)view.FindResource("AppBackgroundBrush"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch };
            var canvas = new Border { Background = (Brush)view.FindResource("AppBackgroundBrush"), Child = surface };
            // Render the content in a themed host without showing an interactive window.
            foreach (var state in new[] { "empty", "review", "matches", "error" })
            {
                if (state is "review" or "matches")
                {
                    repo.IdentityMatches = state == "matches" ? [new(9, "KH000009", "Nguyễn Văn A", "0901234567", "Địa chỉ thử", "012345678901", true, false)] : [];
                    vm.RawInput = "012345678901|123456789|NGUYỄN VĂN A|01011990|Nam|Địa chỉ có dấu để kiểm tra hiển thị và xuống dòng đầy đủ|01012022";
                    vm.ReadAsync().GetAwaiter().GetResult();
                }
                if (state == "error") { vm.RawInput = "invalid"; vm.ReadAsync().GetAwaiter().GetResult(); }
                foreach (var size in new[] { new Size(580, 420), new Size(740, 620) })
                {
                    canvas.Measure(size); canvas.Arrange(new Rect(size)); canvas.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                    view.UpdateLayout();
                    if (state == "review")
                    {
                        var nameInput = Assert.Single(Descendants(view).OfType<TextBox>(),
                            input => input.DataContext is CitizenFieldChange row && row.Field == "Họ tên");
                        Assert.False(nameInput.IsReadOnly);
                        nameInput.SetCurrentValue(TextBox.TextProperty, "Đặng Thị Mỹ");
                        nameInput.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                        Assert.Equal("Đặng Thị Mỹ", vm.Changes[1].Scanned);
                    }
                    var buttons = Descendants(view).OfType<Button>().Where(b => b.IsVisible || b.Visibility == Visibility.Visible).ToArray();
                    Assert.DoesNotContain(buttons, b => b.IsDefault);
                    foreach (var button in buttons.Where(b => b.Command == vm.ApplyCommand || b.Command == vm.CancelCommand ||
                        b.Command == vm.UseExistingCommand || b.Command == vm.EditExistingCommand))
                    {
                        Assert.True(button.ActualHeight >= 40);
                        var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                        Assert.InRange(bounds.Bottom, 0, size.Height + 1);
                        Assert.InRange(bounds.Right, 0, size.Width + 1);
                    }
                    Assert.Single(Descendants(view).OfType<ScrollViewer>(), s => s.Content is StackPanel);
                    foreach (var dpiScale in new[] { 1.0, 1.5, 2.0 })
                    {
                        var bitmap = new RenderTargetBitmap((int)(size.Width*dpiScale), (int)(size.Height*dpiScale),
                            96*dpiScale, 96*dpiScale, PixelFormats.Pbgra32);
                        bitmap.Render(canvas);
                        var folder = Environment.GetEnvironmentVariable("KAZUKO_CCCD_PREVIEW_DIR");
                        if (folder is null || dpiScale != 1) continue;
                        Directory.CreateDirectory(folder);
                        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(folder, $"scan-{theme}-{scale:0.00}-{size.Width}-{state}.png"));
                        png.Save(output);
                    }
                }
            }
            dialog.Close();
        });
    }

    [Fact]
    public void Scanner_enter_reads_preview_without_applying_or_saving_the_customer()
    {
        CustomerTestContext.Sta(() =>
        {
            var repo = new CustomerRepositoryFake();
            var vm = new CitizenScanDialogViewModel(repo, CustomerTestContext.Session().Current!, null,
                new("", "", null, "", "", null), false);
            var dialog = new CitizenScanDialog { DataContext = vm };
            vm.RawInput = "012345678901||NGUYỄN VĂN A|01011990|Nam|Hà Nội|01012022";
            using var source = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("CCCD keyboard test")
                { Width = 1, Height = 1, WindowStyle = 0 });
            var enter = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, source,
                Environment.TickCount, System.Windows.Input.Key.Enter) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            ((TextBox)dialog.FindName("ScanInput")).RaiseEvent(enter);
            Assert.True(enter.Handled);
            Assert.NotNull(vm.Data);
            Assert.Null(vm.Outcome);
            Assert.Empty(repo.Saved);
            dialog.Close();
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}

