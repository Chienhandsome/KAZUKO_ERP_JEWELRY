using System.Globalization;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierPurchaseHistoryRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SupplierPurchaseHistoryRowViewModel(SupplierPurchaseHistoryItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public SupplierPurchaseHistoryItem Item { get; }
    public int Index { get; }

    public string PurchaseDateText => Item.PurchaseDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string TotalAmountText => Item.TotalAmount.ToString("N0", Vi);
    public string PaidAmountText => Item.PaidAmount.ToString("N0", Vi);
    public string RemainingAmountText => Item.RemainingAmount.ToString("N0", Vi);

    public string StatusText => Item.StatusCode switch
    {
        "OPEN" => "Chưa trả",
        "PARTIAL" => "Trả một phần",
        "PAID" => "Đã trả hết",
        _ => Item.StatusCode
    };
}
