using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesDetailByDayRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesDetailByDayRowViewModel(SalesDetailByDayReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesDetailByDayReportRow Item { get; }
    public int Index { get; }
    public string OrderDateText => Item.OrderDate.ToString("dd/MM/yyyy", Vi);
    public string OrderCode => Item.OrderCode;
    public string ProductCode => Item.ProductCode;
    public string ProductName => Item.ProductName;
    public string GoldType => Item.GoldType;

    public string GoldWeightText => Item.GoldWeight.ToString("N3", Vi);
    public string AmountText => Item.Amount.ToString("N0", Vi);
}
