using Kazuko.Core.Mvvm;
using Kazuko.Core.Suppliers;

namespace Kazuko.Modules.Suppliers;

public sealed class SupplierGroupRowViewModel : ObservableObject
{
    public SupplierGroupRowViewModel(SupplierGroupDetail detail)
    {
        Detail = detail;
    }

    public SupplierGroupDetail Detail { get; }

    public long Id => Detail.Id;
    public string Code => Detail.Code;
    public string Name => Detail.Name;
    public string Description => Detail.Description;
    public bool IsActive => Detail.IsActive;
    public bool IsDefault => Detail.IsDefault;
    public int SupplierCount => Detail.SupplierCount;

    public string StatusText => IsDefault ? "Mặc định" : IsActive ? "Đang dùng" : "Đã ẩn";
}
