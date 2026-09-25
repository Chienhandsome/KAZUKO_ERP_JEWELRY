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
        var width = Math.Round(workArea.Width * 0.50);
        var height = Math.Round(workArea.Height * 0.60);

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
