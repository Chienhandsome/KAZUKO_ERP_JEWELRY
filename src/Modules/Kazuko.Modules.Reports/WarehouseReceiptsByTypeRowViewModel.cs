using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class WarehouseReceiptsByTypeRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public WarehouseReceiptsByTypeRowViewModel(WarehouseReceiptsByTypeReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public WarehouseReceiptsByTypeReportRow Item { get; }
    public int Index { get; }
    public string TypeName => Item.TypeName;
    public string DocumentCountText => Item.DocumentCount.ToString("N0", Vi);
    public string LineCountText => Item.LineCount.ToString("N0", Vi);
    public string TotalQuantityText => Item.TotalQuantity.ToString("N3", Vi);
    public string TotalAmountVndText => Item.TotalAmountVnd.ToString("N0", Vi);
}
