using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class FundCashRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public FundCashRowViewModel(FundCashReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public FundCashReportRow Item { get; }
    public int Index { get; }
    public DateTime Day => Item.Day;
    public string DayText => Item.Day.ToString("dd/MM/yyyy", Vi);
    public string TotalInText => Item.TotalIn.ToString("N0", Vi);
    public string TotalOutText => Item.TotalOut.ToString("N0", Vi);
    public string ClosingBalanceText => Item.ClosingBalance.ToString("N0", Vi);
}
