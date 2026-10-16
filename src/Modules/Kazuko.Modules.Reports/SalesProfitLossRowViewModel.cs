using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesProfitLossRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesProfitLossRowViewModel(SalesProfitLossReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesProfitLossReportRow Item { get; }
    public int Index { get; }
    public string OrderDateText => Item.OrderDate.ToString("dd/MM/yyyy", Vi);
    public string OrderCode => Item.OrderCode;
    public string ProductName => Item.ProductName;
    public string GoldType => Item.GoldType;

    public string CostAmountText => Item.CostAmount.ToString("N0", Vi);
    public string SaleAmountText => Item.SaleAmount.ToString("N0", Vi);
    public string ProfitText => Item.Profit.ToString("N0", Vi);
}
