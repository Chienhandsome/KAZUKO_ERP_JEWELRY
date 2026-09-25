using System.Windows;
using System.Windows.Controls;

namespace Kazuko.Modules.Authentication;

public partial class LoginView : UserControl
{
    private bool _passwordVisible;
    private bool _syncingPassword;

    public LoginView() => InitializeComponent();

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPassword || _passwordVisible)
            return;

        if (DataContext is LoginViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.Password = passwordBox.Password;
    }

    private void VisiblePassword_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncingPassword || !_passwordVisible)
            return;

        if (DataContext is LoginViewModel viewModel && sender is TextBox textBox)
            viewModel.Password = textBox.Text;
    }

    private void PasswordVisibilityButton_OnClick(object sender, RoutedEventArgs e)
    {
        _passwordVisible = !_passwordVisible;
        _syncingPassword = true;
        try
        {
            if (_passwordVisible)
            {
                VisiblePasswordInput.Text = PasswordInput.Password;
                PasswordInput.Visibility = Visibility.Collapsed;
                VisiblePasswordInput.Visibility = Visibility.Visible;
                PasswordVisibilityButton.Content = "Ẩn";
                VisiblePasswordInput.Focus();
                VisiblePasswordInput.CaretIndex = VisiblePasswordInput.Text.Length;
            }
            else
            {
                PasswordInput.Password = VisiblePasswordInput.Text;
                VisiblePasswordInput.Visibility = Visibility.Collapsed;
                PasswordInput.Visibility = Visibility.Visible;
                PasswordVisibilityButton.Content = "Hiện";
                PasswordInput.Focus();
            }
        }
        finally
        {
            _syncingPassword = false;
        }
    }
}
