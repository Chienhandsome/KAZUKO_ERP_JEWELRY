using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class SalesInvoicesRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SalesInvoicesRowViewModel(SalesInvoicesReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public SalesInvoicesReportRow Item { get; }
    public int Index { get; }
    public string Code => Item.Code;
    public string DocumentDateText => Item.DocumentDate.ToString("dd/MM/yyyy", Vi);
    public string CustomerName => Item.CustomerName;

    public string TotalAmountText => Item.TotalAmount.ToString("N0", Vi);
    public string PaidAmountText => Item.PaidAmount.ToString("N0", Vi);
    public string RemainingAmountText => Item.RemainingAmount.ToString("N0", Vi);
}
