using System.Globalization;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed class DistributionRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public DistributionRowViewModel(string name, int customerCount, decimal totalSales)
    {
        Name = name;
        CustomerCount = customerCount;
        TotalSales = totalSales;
    }

    public string Name { get; }
    public int CustomerCount { get; }
    public decimal TotalSales { get; }

    public string CustomerCountText => CustomerCount.ToString("N0", Vi);
    public string TotalSalesText => TotalSales.ToString("N0", Vi);
}
