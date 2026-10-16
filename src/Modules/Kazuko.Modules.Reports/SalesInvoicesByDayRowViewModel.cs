using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesInvoicesByDayRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesInvoicesByDayRowViewModel(SalesInvoicesByDayReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesInvoicesByDayReportRow Item { get; }
    public int Index { get; }
    public string DocumentDateText => Item.DocumentDate.ToString("dd/MM/yyyy", Vi);
    public string InvoiceCountText => Item.InvoiceCount.ToString("N0", Vi);

    public string TotalRevenueText => Item.TotalRevenue.ToString("N0", Vi);
    public string TotalCostText => Item.TotalCost.ToString("N0", Vi);
    public string ProfitText => Item.Profit.ToString("N0", Vi);
}
