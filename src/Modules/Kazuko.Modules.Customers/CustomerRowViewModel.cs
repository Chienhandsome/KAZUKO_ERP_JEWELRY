using System.Globalization;
using Kazuko.Core.Customers;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.Customers;

public sealed class CustomerRowViewModel : ObservableObject
{
    public CustomerRowViewModel(CustomerListItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public CustomerListItem Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public string Code => Item.Code;
    public string Name => Item.Name;
    public bool IsWalkIn => Item.IsWalkIn;
    public string ProtectionHint => IsWalkIn ? "Khách vãng lai là bản ghi hệ thống, không được sửa hoặc ngừng hoạt động." : string.Empty;
    public string KindText => IsWalkIn ? "Vãng lai · Hệ thống" : "Có hồ sơ";
    public string Phone => Item.Phone;
    public string CustomerGroupName => Item.CustomerGroupName;
    public string Region => Item.Region;
    public decimal TotalSales => Item.TotalSales;
    public decimal CurrentDebt => Item.CurrentDebt;
    public DateTime? LastPurchaseDate => Item.LastPurchaseDate;
    public bool IsActive => Item.IsActive;
    public string StatusText => IsActive ? "Đang hoạt động" : "Ngừng giao dịch";
    public string TotalSalesText => TotalSales.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    public string CurrentDebtText => CurrentDebt.ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
    public string LastPurchaseText => LastPurchaseDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string ToggleActiveLabel => IsActive ? "Ngừng giao dịch" : "Kích hoạt lại";
}
