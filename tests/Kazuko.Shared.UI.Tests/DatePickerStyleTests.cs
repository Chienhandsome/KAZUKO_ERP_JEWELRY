using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace Kazuko.Shared.UI.Tests;

public sealed class DatePickerStyleTests
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
    public void Default_and_derived_date_fields_keep_editing_calendar_null_dates_and_theme_support(string theme, double scale)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? host = null;
            try
            {
                var stack = new StackPanel { Margin = new Thickness(20) };
                host = new Window { Content = stack, Width = 450, Height = 320, Left = -32000, Top = -32000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
                foreach (var resource in new[] { theme, "Typography", "Controls" })
                    host.Resources.MergedDictionaries.Add(new ResourceDictionary
                    { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml", UriKind.Relative) });
                foreach (var key in new[] { "FontSize.Caption", "FontSize.Body", "FontSize.Subtitle", "FontSize.Heading", "FontSize.Display" })
                    host.Resources[key] = (double)host.FindResource(key) * scale;
                var model = new DateModel { Date = new DateTime(2026, 10, 19) };
                var field = new DatePicker { Width = 170, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) };
                field.SetBinding(DatePicker.SelectedDateProperty, new Binding(nameof(DateModel.Date)) { Source = model, Mode = BindingMode.TwoWay });
                stack.Children.Add(field);
                var empty = new DatePicker { Style = new Style(typeof(DatePicker), (Style)host.FindResource("RoundedDatePickerStyle")), Width = 170, HorizontalAlignment = HorizontalAlignment.Left };
                stack.Children.Add(empty);
                host.Show();
                Layout();
                Assert.Equal("vi-VN", field.Language.IetfLanguageTag, ignoreCase: true);
                Assert.True(field.ActualHeight >= 40);
                var editor = Assert.IsType<DatePickerTextBox>(field.Template.FindName("PART_TextBox", field));
                Assert.Equal("19/10/2026", editor.Text);
                Assert.True(editor.ExtentWidth <= editor.ViewportWidth + 1, "The full date must fit at the largest font scale.");
                var border = Assert.IsType<Border>(field.Template.FindName("DateBorder", field));
                Assert.Equal(new CornerRadius(8), border.CornerRadius);
                var open = Assert.IsType<Button>(field.Template.FindName("PART_Button", field));
                open.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Layout();
                Assert.True(field.IsDropDownOpen);
                var popup = Assert.IsType<Popup>(field.Template.FindName("PART_Popup", field));
                Assert.True(popup.IsOpen);
                var calendar = Assert.IsType<Calendar>(popup.Child);
                calendar.SelectedDate = new DateTime(2026, 11, 5);
                field.IsDropDownOpen = false;
                Layout();
                Assert.Equal(new DateTime(2026, 11, 5), model.Date);
                editor.Text = "06/12/2026";
                Enter(editor, host);
                Layout();
                Assert.Equal(new DateTime(2026, 12, 6), model.Date);
                editor.Text = string.Empty;
                Enter(editor, host);
                Layout();
                Assert.Null(model.Date);
                Assert.Null(empty.SelectedDate);
                field.SelectedDate = new DateTime(2026, 10, 19);
                var emptyEditor = Assert.IsType<DatePickerTextBox>(empty.Template.FindName("PART_TextBox", empty));
                Assert.Equal(Visibility.Visible, Assert.IsType<TextBlock>(emptyEditor.Template.FindName("DateHint", emptyEditor)).Visibility);
                empty.IsEnabled = false;
                Assert.False(Assert.IsType<Button>(empty.Template.FindName("PART_Button", empty)).IsEnabled);
                var nextTheme = theme == "Light" ? "Dark" : "Light";
                host.Resources.MergedDictionaries[0] = new ResourceDictionary
                { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{nextTheme}.xaml", UriKind.Relative) };
                Layout();
                Assert.Same(host.FindResource("SurfaceBrush"), field.Background);
                host.Resources.MergedDictionaries[0] = new ResourceDictionary
                { Source = new Uri($"/Kazuko.Shared.UI;component/Themes/{theme}.xaml", UriKind.Relative) };
                Layout();
                var capture = Environment.GetEnvironmentVariable("KAZUKO_DATE_UI_CAPTURE_DIR");
                if (!string.IsNullOrEmpty(capture))
                {
                    Directory.CreateDirectory(capture);
                    var bitmap = new RenderTargetBitmap(450, 320, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(capture, $"date-{theme}-{scale:0.00}.png")); encoder.Save(file);
                }
            }
            catch (Exception ex) { failure = ex; }
            finally { host?.Close(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(40)), "Date field verification timed out.");
        if (failure is not null) throw new Exception("Date field verification failed.", failure);
    }

    private static void Enter(DatePickerTextBox editor, Window host) => editor.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(host)!, 0, Key.Return) { RoutedEvent = Keyboard.KeyDownEvent });
    private static void Layout()
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
    }
    private sealed class DateModel { public DateTime? Date { get; set; } }
}
