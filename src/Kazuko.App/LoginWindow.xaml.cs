using System.Windows;

namespace Kazuko.App;

public partial class LoginWindow : Window
{
    public LoginWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var workArea = SystemParameters.WorkArea;
        var contentSize = new Size(0, 0);
        if (Content is FrameworkElement content)
        {
            content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            contentSize = content.DesiredSize;
        }

        var frameWidth = SystemParameters.FixedFrameVerticalBorderWidth * 2;
        var frameHeight = SystemParameters.FixedFrameHorizontalBorderHeight * 2
            + SystemParameters.WindowCaptionHeight;
        var width = Math.Min(workArea.Width - 24,
            Math.Max(Math.Max(1000, workArea.Width * 0.58), contentSize.Width + frameWidth));
        var height = Math.Min(workArea.Height - 24,
            Math.Max(Math.Max(700, workArea.Height * 0.72), contentSize.Height + frameHeight));

        Width = width;
        Height = height;
        MinWidth = width;
        MaxWidth = width;
        MinHeight = height;
        MaxHeight = height;
        Left = workArea.Left + ((workArea.Width - width) / 2);
        Top = workArea.Top + ((workArea.Height - height) / 2);
    }
}
