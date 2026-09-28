using System.Globalization;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierRowViewModel : ObservableObject
{
    public SupplierRowViewModel(SupplierListItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public SupplierListItem Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public string Code => Item.Code;
    public string Name => Item.Name;
    public string Phone => Item.Phone;
    public string Email => Item.Email;
    public string SupplierGroupName => Item.SupplierGroupName;
    public string Region => Item.Region;
    public decimal TotalPurchases => Item.TotalPurchases;
    public decimal CurrentDebt => Item.CurrentDebt;
    public DateTime? LastPurchaseDate => Item.LastPurchaseDate;
    public bool IsActive => Item.IsActive;
    public string Address => Item.Address;
    public string ContactName => Item.ContactName;
    public string ContactTitle => Item.ContactTitle;
    public string TaxCode => Item.TaxCode;
    public string Notes => Item.Notes;

    public string StatusText => IsActive ? "Đang hợp tác" : "Ngừng hợp tác";
    public string TotalPurchasesText => TotalPurchases.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    public string CurrentDebtText => CurrentDebt.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    public string LastPurchaseText => LastPurchaseDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string ToggleActiveLabel => IsActive ? "Ngừng hợp tác" : "Hợp tác lại";
}
