using System.Globalization;
using Kazuko.Core.Finance;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierPaymentHistoryRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public SupplierPaymentHistoryRowViewModel(DebtLedgerEntry entry)
    {
        Entry = entry;
    }

    public DebtLedgerEntry Entry { get; }

    public DateTime EntryDate => Entry.EntryDate;
    public string DocumentCode => Entry.DocumentCode;
    public string Description => Entry.Description;
    public decimal ReceiptAmountVnd => Entry.ReceiptAmountVnd;
    public decimal PaymentAmountVnd => Entry.PaymentAmountVnd;

    public string EntryDateText => EntryDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string ReceiptAmountText => ReceiptAmountVnd.ToString("N0", Vi);
    public string PaymentAmountText => PaymentAmountVnd.ToString("N0", Vi);

    public string MethodText => Entry.Description.Split('/').LastOrDefault()?.Trim().ToUpperInvariant() switch
    {
        "CASH" => "Tiền mặt",
        "BANK" or "TRANSFER" or "BANK_TRANSFER" => "Chuyển khoản",
        "CARD" => "Thẻ",
        _ => "—"
    };
}
