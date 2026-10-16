using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class MasterCustomersRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public MasterCustomersRowViewModel(MasterCustomersReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public MasterCustomersReportRow Item { get; }
    public int Index { get; }
    public string Code => Item.Code;
    public string Name => Item.Name;
    public string Phone => Item.Phone;
    public string Email => Item.Email;
    public string Address => Item.Address;
    public string GroupName => Item.GroupName;
    public string IdentityNumber => Item.IdentityNumber;

    public string CreditLimitText => Item.CreditLimit.ToString("N0", Vi);
}
