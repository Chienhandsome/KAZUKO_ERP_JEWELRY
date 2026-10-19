using System.Globalization;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierDistributionRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SupplierDistributionRowViewModel(string name, int supplierCount, decimal totalPurchases)
    {
        Name = name;
        SupplierCount = supplierCount;
        TotalPurchases = totalPurchases;
    }

    public string Name { get; }
    public int SupplierCount { get; }
    public decimal TotalPurchases { get; }

    public string TotalPurchasesText => TotalPurchases.ToString("N0", Vi);
}
