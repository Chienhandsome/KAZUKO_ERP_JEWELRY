using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesNewGoldDetailByTypeRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesNewGoldDetailByTypeRowViewModel(SalesNewGoldDetailByTypeReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesNewGoldDetailByTypeReportRow Item { get; }
    public int Index { get; }
    public string GoldType => Item.GoldType;
    public string OrderDateText => Item.OrderDate.ToString("dd/MM/yyyy", Vi);
    public string OrderCode => Item.OrderCode;
    public string ProductCode => Item.ProductCode;

    public string GoldWeightText => Item.GoldWeight.ToString("N3", Vi);
    public string AmountText => Item.Amount.ToString("N0", Vi);
}
