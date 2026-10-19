using System.Globalization;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class TopSupplierRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public TopSupplierRowViewModel(SupplierTopPurchaseItem item, int rank)
    {
        Item = item;
        Rank = rank;
    }

    public SupplierTopPurchaseItem Item { get; }
    public int Rank { get; }

    public string TotalPurchasesText => Item.TotalPurchases.ToString("N0", Vi);
}
