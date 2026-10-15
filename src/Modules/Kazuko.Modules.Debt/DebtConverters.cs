using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Kazuko.Core.Finance;

namespace Kazuko.Modules.Debt;

public sealed class BooleanToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert)
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || (value is string text && string.IsNullOrWhiteSpace(text));
        if (Invert)
            empty = !empty;
        return empty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class DebtStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = (value as string ?? string.Empty).Trim().ToUpperInvariant();
        return status switch
        {
            DebtDisplayStatuses.Overdue => Application.Current.TryFindResource("DangerBrush") as Brush
                                           ?? Brushes.IndianRed,
            DebtDisplayStatuses.Paid => Application.Current.TryFindResource("SuccessBrush") as Brush
                                        ?? Brushes.SeaGreen,
            _ => Application.Current.TryFindResource("PrimaryBrush") as Brush
                 ?? Brushes.DarkOrange
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Tô màu số tiền theo HƯỚNG công nợ, không theo dấu âm/dương:
/// RECEIVE &amp; &gt; 0 → SuccessBrush (xanh lá), PAY &amp; &gt; 0 → DangerBrush (đỏ),
/// = 0 → TextSecondaryBrush (trung tính).
/// </summary>
public sealed class DebtDirectionToBrushConverter : IValueConverter
{
    /// <summary>DebtLedgerDirections.Receive hoặc DebtLedgerDirections.Pay.</summary>
    public string Direction { get; set; } = DebtLedgerDirections.Receive;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var amount = value switch
        {
            decimal d => d,
            double dbl => (decimal)dbl,
            int i => i,
            _ => 0m
        };

        if (amount > 0)
        {
            var brushKey = string.Equals(
                Direction, DebtLedgerDirections.Pay, StringComparison.OrdinalIgnoreCase)
                ? "DangerBrush"
                : "SuccessBrush";
            return Application.Current.TryFindResource(brushKey) as Brush ?? Brushes.Gray;
        }

        return Application.Current.TryFindResource("TextSecondaryBrush") as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
