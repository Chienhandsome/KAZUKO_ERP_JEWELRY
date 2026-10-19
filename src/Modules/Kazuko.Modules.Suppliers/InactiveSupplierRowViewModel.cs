using System.Globalization;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Suppliers;

public sealed class InactiveSupplierRowViewModel : ObservableObject
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public InactiveSupplierRowViewModel(
        long supplierId,
        string code,
        string name,
        string phone,
        string groupName,
        DateTime? lastPurchaseDate,
        int daysSinceLastPurchase,
        decimal totalPurchases)
    {
        SupplierId = supplierId;
        Code = code;
        Name = name;
        Phone = phone;
        GroupName = groupName;
        LastPurchaseDate = lastPurchaseDate;
        DaysSinceLastPurchase = daysSinceLastPurchase;
        TotalPurchases = totalPurchases;
    }

    public long SupplierId { get; }
    public string Code { get; }
    public string Name { get; }
    public string Phone { get; }
    public string GroupName { get; }
    public DateTime? LastPurchaseDate { get; }
    public int DaysSinceLastPurchase { get; }
    public decimal TotalPurchases { get; }

    public string LastPurchaseText => LastPurchaseDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "Chưa nhập";
    public string TotalPurchasesText => TotalPurchases.ToString("N0", Vi);
}
