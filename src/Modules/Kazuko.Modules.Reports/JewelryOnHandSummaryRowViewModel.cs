using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class JewelryOnHandSummaryRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public JewelryOnHandSummaryRowViewModel(JewelryOnHandSummaryRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public JewelryOnHandSummaryRow Item { get; }
    public int Index { get; }

    public string ScopeDisplay => Item.InventoryScope switch
    {
        "FINISHED_GOODS" => "Kho thành phẩm",
        "SALES_FLOOR" => "Kho bán hàng",
        _ => string.IsNullOrWhiteSpace(Item.InventoryScope) ? "(Không xác định)" : Item.InventoryScope
    };

    public string GoldPurity => string.IsNullOrWhiteSpace(Item.GoldPurity) ? "(Chưa gán)" : Item.GoldPurity;
    public string ItemCountText => Item.ItemCount.ToString("N0", Vi);
    public string GoldWeightText => Item.TotalGoldWeight.ToString("N3", Vi);
    public string TotalWeightText => Item.TotalWeight.ToString("N3", Vi);
    public string StockValueText => Item.TotalStockValue.ToString("N0", Vi);
}
