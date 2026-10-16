using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class WarehousePendingReceiptsRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public WarehousePendingReceiptsRowViewModel(WarehousePendingReceiptsReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public WarehousePendingReceiptsReportRow Item { get; }
    public int Index { get; }
    public string DocumentDateText => Item.DocumentDate.ToString("dd/MM/yyyy", Vi);
    public string DocumentCode => Item.DocumentCode;
    public string MaterialCode => Item.MaterialCode;
    public string MaterialName => Item.MaterialName;
    public string GoldTypeName => Item.GoldTypeName;
    public string SupplierName => Item.SupplierName;

    public string QuantityText =>
        string.IsNullOrWhiteSpace(Item.UnitCode)
            ? Item.Quantity.ToString("N3", Vi)
            : $"{Item.Quantity.ToString("N3", Vi)} {Item.UnitCode}";

    public string UnitPriceText => Item.UnitPrice.ToString("N0", Vi);
    public string AmountVndText => Item.AmountVnd.ToString("N0", Vi);
}
