using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class JewelryOnHandRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public JewelryOnHandRowViewModel(JewelryOnHandReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public JewelryOnHandReportRow Item { get; }
    public int Index { get; }
    public string Code => Item.Code;
    public string ProductName => Item.ProductName;
    public string GoldPurity => Item.GoldPurity;
    public string LocationName => Item.LocationName;
    public string ScopeDisplay => Item.InventoryScope switch
    {
        "FINISHED_GOODS" => "Kho thành phẩm",
        "SALES_FLOOR" => "Kho bán hàng",
        _ => Item.InventoryScope
    };
    public string StatusDisplay => Item.StatusCode switch
    {
        "IN_STOCK" => "Trong kho",
        "RESERVED" => "Đang giữ",
        "DISPLAYED" => "Trưng bày",
        _ => Item.StatusCode
    };

    public string GoldWeightText => Item.GoldWeight.ToString("N3", Vi);
    public string TotalWeightText => Item.TotalWeight.ToString("N3", Vi);
    public string StockValueText => Item.StockValue.ToString("N0", Vi);
}
