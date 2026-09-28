using System.Globalization;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.HumanResources;

public sealed class EmployeeRowViewModel : ObservableObject
{
    public EmployeeRowViewModel(EmployeeListItem item, int index)
    {
        Item = item;
        Index = index;
    }

    public EmployeeListItem Item { get; }
    public int Index { get; }
    public long Id => Item.Id;
    public string Code => Item.Code;
    public string FullName => Item.FullName;
    public string? PhotoPath => Item.PhotoPath;
    public bool HasPhoto => !string.IsNullOrWhiteSpace(PhotoPath);
    public string Gender => Item.Gender;
    public DateTime? DateOfBirth => Item.DateOfBirth;
    public string Phone => Item.Phone;
    public string Email => Item.Email;
    public DateTime? HireDate => Item.HireDate;
    public string StatusCode => Item.StatusCode;
    public bool IsActive => Item.IsActive;
    public string StatusText => EmployeeStatusCodes.ToDisplay(StatusCode);
    public string DateOfBirthText => DateOfBirth?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string HireDateText => HireDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "—";
    public string ToggleActiveLabel => IsActive ? "Ngừng làm việc" : "Kích hoạt lại";
}
