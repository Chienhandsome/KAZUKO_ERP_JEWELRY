using System.Globalization;
using Kazuko.Core.Reports;

namespace Kazuko.Modules.Reports;

public sealed class FundBalanceRowViewModel
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");

    public FundBalanceRowViewModel(FundBalanceReportRow item, int index)
    {
        Item = item;
        Index = index;
    }

    public FundBalanceReportRow Item { get; }
    public int Index { get; }
    public string FundName => Item.FundName;

    public string FundKindDisplay => Item.FundKind switch
    {
        "CASH" => "Tiền mặt",
        "BANK" => "Ngân hàng",
        _ => Item.FundKind
    };

    public string OpeningBalanceText => Item.OpeningBalance.ToString("N0", Vi);
    public string TotalInText => Item.TotalIn.ToString("N0", Vi);
    public string TotalOutText => Item.TotalOut.ToString("N0", Vi);
    public string ClosingBalanceText => Item.ClosingBalance.ToString("N0", Vi);
}
