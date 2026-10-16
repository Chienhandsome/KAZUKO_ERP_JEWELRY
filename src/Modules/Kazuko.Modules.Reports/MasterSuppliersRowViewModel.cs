using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class MasterSuppliersRowViewModel
{
    public MasterSuppliersRowViewModel(MasterSuppliersReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public MasterSuppliersReportRow Item { get; }
    public int Index { get; }
    public string Code => Item.Code;
    public string Name => Item.Name;
    public string Phone => Item.Phone;
    public string Email => Item.Email;
    public string Address => Item.Address;
    public string TaxCode => Item.TaxCode;
    public string ContactName => Item.ContactName;
}
