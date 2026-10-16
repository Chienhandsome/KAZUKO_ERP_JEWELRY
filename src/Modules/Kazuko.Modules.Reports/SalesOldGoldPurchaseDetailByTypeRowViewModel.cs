using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesOldGoldPurchaseDetailByTypeRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesOldGoldPurchaseDetailByTypeRowViewModel(SalesOldGoldPurchaseDetailByTypeReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesOldGoldPurchaseDetailByTypeReportRow Item { get; }
    public int Index { get; }
    public string GoldType => Item.GoldType;
    public string PurchaseDateText => Item.PurchaseDate.ToString("dd/MM/yyyy", Vi);
    public string DocumentCode => Item.DocumentCode;

    public string GoldWeightText => Item.GoldWeight.ToString("N3", Vi);
    public string UnitPriceText => Item.UnitPrice.ToString("N0", Vi);
    public string AmountText => Item.Amount.ToString("N0", Vi);
}
