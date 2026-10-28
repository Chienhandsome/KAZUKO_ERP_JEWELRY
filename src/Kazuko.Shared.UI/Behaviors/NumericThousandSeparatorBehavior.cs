using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace Kazuko.Shared.UI.Behaviors;

/// <summary>
/// Ngắt hàng nghìn trực tiếp trong khi đang nhập số tiền hoặc số lượng.
/// "1000000" → "1.000.000" ngay khi gõ, giữ đúng vị trí con trỏ.
/// Số lượng lẻ: set thêm <c>AllowDecimal="True"</c> ("1.250,5").
/// Không tự gắn vào style chung — từng ô opt-in.
/// Ô binding thẳng vào decimal phải dùng <c>ViMoneyConverter</c> / <c>ViQuantityConverter</c>,
/// không dùng StringFormat=N0 (binding WPF đọc dấu chấm theo en-US).
/// </summary>
public static class NumericThousandSeparatorBehavior
{
    public static readonly DependencyProperty EnableLiveThousandSeparatorProperty =
        DependencyProperty.RegisterAttached(
            "EnableLiveThousandSeparator",
            typeof(bool),
            typeof(NumericThousandSeparatorBehavior),
            new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnableLiveThousandSeparator(DependencyObject obj) =>
        (bool)obj.GetValue(EnableLiveThousandSeparatorProperty);

    public static void SetEnableLiveThousandSeparator(DependencyObject obj, bool value) =>
        obj.SetValue(EnableLiveThousandSeparatorProperty, value);

    public static readonly DependencyProperty AllowDecimalProperty =
        DependencyProperty.RegisterAttached(
            "AllowDecimal",
            typeof(bool),
            typeof(NumericThousandSeparatorBehavior),
            new PropertyMetadata(false));

    public static bool GetAllowDecimal(DependencyObject obj) =>
        (bool)obj.GetValue(AllowDecimalProperty);

    public static void SetAllowDecimal(DependencyObject obj, bool value) =>
        obj.SetValue(AllowDecimalProperty, value);

    // Cờ chống đệ quy: set Text trong TextChanged sẽ bắn TextChanged lần nữa.
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(NumericThousandSeparatorBehavior),
            new PropertyMetadata(false));

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            textBox.TextChanged += OnTextChanged;
            textBox.Loaded += OnLoaded;
            textBox.LostFocus += OnLostFocus;
            DataObject.AddPastingHandler(textBox, OnPaste);
        }
        else
        {
            textBox.TextChanged -= OnTextChanged;
            textBox.Loaded -= OnLoaded;
            textBox.LostFocus -= OnLostFocus;
            DataObject.RemovePastingHandler(textBox, OnPaste);
        }
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            Format(textBox);
    }

    private static void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            e.CancelCommand();
            return;
        }

        var pasted = e.DataObject.GetData(DataFormats.UnicodeText) as string ?? string.Empty;
        var allowDecimal = GetAllowDecimal(textBox);
        var cleaned = allowDecimal
            ? pasted.Trim()
            : new string(pasted.Where(char.IsDigit).ToArray());

        e.CancelCommand();
        var start = textBox.SelectionStart;
        var newText = textBox.Text.Remove(start, textBox.SelectionLength).Insert(start, cleaned);
        textBox.Text = newText;
        textBox.CaretIndex = start + cleaned.Length;
    }

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;
        if ((bool)textBox.GetValue(IsUpdatingProperty))
            return;

        Format(textBox);
    }

    private static void Format(TextBox textBox)
    {
        if (textBox.IsReadOnly)
            return;
        if ((bool)textBox.GetValue(IsUpdatingProperty))
            return;

        if (!ViNumberFormat.TryFormatInput(
                textBox.Text,
                GetAllowDecimal(textBox),
                textBox.CaretIndex,
                out var formatted,
                out var newCaret))
            return;

        textBox.SetValue(IsUpdatingProperty, true);
        try
        {
            textBox.Text = formatted;
            textBox.CaretIndex = newCaret;
        }
        finally
        {
            textBox.SetValue(IsUpdatingProperty, false);
        }

        // Gán Text ngay trong TextChanged nằm trong lượt cập nhật binding,
        // nên giá trị đã ngắt nghìn không được ghi vào source. Đẩy lại sau lượt đó.
        textBox.Dispatcher.BeginInvoke(CommitBinding, DispatcherPriority.DataBind, textBox);
    }

    private static void OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            CommitBinding(textBox);
    }

    private static void CommitBinding(TextBox textBox)
    {
        BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty)?.UpdateSource();
    }
}
