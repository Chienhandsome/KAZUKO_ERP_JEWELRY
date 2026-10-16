using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesTopItemsRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesTopItemsRowViewModel(SalesTopItemsReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesTopItemsReportRow Item { get; }
    public int Index { get; }
    public string ProductCode => Item.ProductCode;
    public string ProductName => Item.ProductName;
    public string GoldType => Item.GoldType;
    public string QuantitySoldText => Item.QuantitySold.ToString("N0", Vi);

    public string RevenueText => Item.Revenue.ToString("N0", Vi);
}
