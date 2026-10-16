using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>
/// Tự động bôi đen toàn bộ nội dung khi TextBox nhận focus (click chuột hoặc Tab).
/// Bỏ qua TextBox nhiều dòng (AcceptsReturn) và TextBox chỉ đọc (IsReadOnly).
/// <see cref="Register"/> gắn class handler nên style riêng (không BasedOn style TextBox mặc định)
/// vẫn được bôi đen. Attached property giữ cho trường hợp cần gắn tường minh.
/// </summary>
public static class TextBoxSelectAllBehavior
{
    private static bool _registered;

    /// <summary>
    /// Gắn một lần lúc khởi động. Style có key riêng không kế thừa setter trên style TextBox mặc định.
    /// </summary>
    public static void Register()
    {
        if (_registered)
            return;

        _registered = true;
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.GotFocusEvent,
            new RoutedEventHandler(OnTextBoxGotFocus));
        EventManager.RegisterClassHandler(
            typeof(TextBox),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnTextBoxPreviewMouseLeftButtonDown));
        EventManager.RegisterClassHandler(
            typeof(PasswordBox),
            UIElement.GotFocusEvent,
            new RoutedEventHandler(OnPasswordBoxGotFocus));
        EventManager.RegisterClassHandler(
            typeof(PasswordBox),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnPasswordBoxPreviewMouseLeftButtonDown));
    }

    public static readonly DependencyProperty SelectAllOnFocusProperty =
        DependencyProperty.RegisterAttached(
            "SelectAllOnFocus",
            typeof(bool),
            typeof(TextBoxSelectAllBehavior),
            new PropertyMetadata(false, OnSelectAllOnFocusChanged));

    public static bool GetSelectAllOnFocus(DependencyObject obj) =>
        (bool)obj.GetValue(SelectAllOnFocusProperty);

    public static void SetSelectAllOnFocus(DependencyObject obj, bool value) =>
        obj.SetValue(SelectAllOnFocusProperty, value);

    private static void OnSelectAllOnFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBox textBox)
        {
            if ((bool)e.NewValue)
            {
                textBox.GotFocus += OnTextBoxGotFocus;
                textBox.PreviewMouseLeftButtonDown += OnTextBoxPreviewMouseLeftButtonDown;
            }
            else
            {
                textBox.GotFocus -= OnTextBoxGotFocus;
                textBox.PreviewMouseLeftButtonDown -= OnTextBoxPreviewMouseLeftButtonDown;
            }
        }
        else if (d is PasswordBox passwordBox)
        {
            if ((bool)e.NewValue)
            {
                passwordBox.GotFocus += OnPasswordBoxGotFocus;
                passwordBox.PreviewMouseLeftButtonDown += OnPasswordBoxPreviewMouseLeftButtonDown;
            }
            else
            {
                passwordBox.GotFocus -= OnPasswordBoxGotFocus;
                passwordBox.PreviewMouseLeftButtonDown -= OnPasswordBoxPreviewMouseLeftButtonDown;
            }
        }
    }

    private static bool ShouldSelectAll(TextBox textBox) =>
        !textBox.IsReadOnly && !textBox.AcceptsReturn;

    private static void OnTextBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox && ShouldSelectAll(textBox))
            textBox.SelectAll();
    }

    private static void OnTextBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox textBox && ShouldSelectAll(textBox) && !textBox.IsFocused)
        {
            e.Handled = true;
            textBox.Focus();
        }
    }

    private static void OnPasswordBoxGotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox passwordBox)
            passwordBox.SelectAll();
    }

    private static void OnPasswordBoxPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is PasswordBox passwordBox && !passwordBox.IsFocused)
        {
            e.Handled = true;
            passwordBox.Focus();
        }
    }
}
