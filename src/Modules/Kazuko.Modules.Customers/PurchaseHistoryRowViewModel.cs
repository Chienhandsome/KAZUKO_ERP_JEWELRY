using System.Globalization;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed class PurchaseHistoryRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public PurchaseHistoryRowViewModel(CustomerPurchaseHistoryItem item)
    {
        Item = item;
    }

    public CustomerPurchaseHistoryItem Item { get; }
    public long OrderId => Item.OrderId;
    public DateTime OrderDate => Item.OrderDate;
    public string InvoiceCode => Item.InvoiceCode;
    public decimal TotalAmount => Item.TotalAmount;
    public decimal PaidAmount => Item.PaidAmount;
    public string PaymentStatusCode => Item.PaymentStatusCode;

    public string OrderDateText => OrderDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string TotalAmountText => TotalAmount.ToString("N0", Vi);

    public string PaymentStatusText => PaymentStatusCode switch
    {
        "PAID" => "Đã thanh toán",
        "PARTIAL" => "Thanh toán một phần",
        _ => "Chưa thanh toán"
    };

    public string PaymentStatusKind => PaymentStatusCode switch
    {
        "PAID" => "Success",
        "PARTIAL" => "Warning",
        _ => "Danger"
    };
}
