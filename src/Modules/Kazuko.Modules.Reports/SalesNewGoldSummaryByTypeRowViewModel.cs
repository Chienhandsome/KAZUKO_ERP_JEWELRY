using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesNewGoldSummaryByTypeRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesNewGoldSummaryByTypeRowViewModel(SalesNewGoldSummaryByTypeReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesNewGoldSummaryByTypeReportRow Item { get; }
    public int Index { get; }
    public string GoldType => Item.GoldType;
    public string ItemCountText => Item.ItemCount.ToString("N0", Vi);

    public string TotalGoldWeightText => Item.TotalGoldWeight.ToString("N3", Vi);
    public string TotalAmountText => Item.TotalAmount.ToString("N0", Vi);
}
