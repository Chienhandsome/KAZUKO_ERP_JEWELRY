using System.Globalization;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed class InactiveCustomerRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public InactiveCustomerRowViewModel(CustomerListItem item, int daysSinceLastPurchase)
    {
        Item = item;
        DaysSinceLastPurchase = daysSinceLastPurchase;
    }

    public CustomerListItem Item { get; }
    public int DaysSinceLastPurchase { get; }
    public string Code => Item.Code;
    public string Name => Item.Name;
    public string Phone => Item.Phone;
    public string CustomerGroupName => Item.CustomerGroupName;

    public string LastPurchaseDateText =>
        Item.LastPurchaseDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";

    public string DaysSinceLastPurchaseText => DaysSinceLastPurchase < 0 ? "Chưa mua" : DaysSinceLastPurchase.ToString("N0", Vi);
    public string TotalSalesText => Item.TotalSales.ToString("N0", Vi);
}
