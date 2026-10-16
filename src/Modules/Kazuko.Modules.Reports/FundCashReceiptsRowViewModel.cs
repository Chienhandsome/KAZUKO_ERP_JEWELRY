using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class FundCashReceiptsRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public FundCashReceiptsRowViewModel(FundCashReceiptsReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public FundCashReceiptsReportRow Item { get; }
    public int Index { get; }
    public string Code => Item.Code;
    public string PartyName => Item.PartyName;
    public string Notes => Item.Notes;
    public string CreatedByName => Item.CreatedByName;
    public string FundName => Item.FundName;
    public string VoucherDateText => Item.VoucherDate.ToString("dd/MM/yyyy", Vi);
    public string AmountVndText => Item.AmountVnd.ToString("N0", Vi);
}
