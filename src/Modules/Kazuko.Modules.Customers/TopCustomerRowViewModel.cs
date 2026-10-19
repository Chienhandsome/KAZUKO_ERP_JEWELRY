using System.Globalization;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed class TopCustomerRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public TopCustomerRowViewModel(CustomerTopSalesItem item, int rank)
    {
        Item = item;
        Rank = rank;
    }

    public CustomerTopSalesItem Item { get; }
    public int Rank { get; }
    public string Code => Item.Code;
    public string Name => Item.Name;
    public string Phone => Item.Phone;
    public string CustomerGroupName => Item.CustomerGroupName;
    public int OrderCount => Item.OrderCount;
    public decimal TotalSales => Item.TotalSales;

    public string OrderCountText => OrderCount.ToString("N0", Vi);
    public string TotalSalesText => TotalSales.ToString("N0", Vi);
}
