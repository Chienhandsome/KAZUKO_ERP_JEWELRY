using System.Globalization;
using System.Windows.Data;
using Kazuko.Core.Sales;

namespace Kazuko.Modules.Sales.Converters;

public sealed class CraftOrderStatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string)?.Trim().ToUpperInvariant() switch
        {
            CraftOrderStatuses.Draft => "Bản nháp",
            CraftOrderStatuses.Confirmed => "Đã xác nhận",
            CraftOrderStatuses.Deposited => "Đã đặt cọc",
            CraftOrderStatuses.InProduction => "Đang chế tác",
            CraftOrderStatuses.Ready => "Chờ giao hàng",
            CraftOrderStatuses.PartiallyDelivered => "Đã giao một phần",
            CraftOrderStatuses.Completed => "Hoàn tất",
            CraftOrderStatuses.Cancelled => "Đã hủy",
            _ => value as string ?? string.Empty
        };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
