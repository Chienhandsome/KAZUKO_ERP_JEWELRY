using System.Globalization;

namespace Kazuko.Modules.Customers;

public sealed record CustomerSalesSummaryRow(string Kind, int OrderCount, decimal TotalSales)
{
    public string OrderCountText => OrderCount.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    public string TotalSalesText => TotalSales.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
}
