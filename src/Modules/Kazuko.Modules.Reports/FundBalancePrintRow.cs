namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class FundBalancePrintRow
{
    public int Index { get; set; }
    public string FundKindDisplay { get; set; } = string.Empty;
    public string FundName { get; set; } = string.Empty;
    public string OpeningBalanceText { get; set; } = string.Empty;
    public string TotalInText { get; set; } = string.Empty;
    public string TotalOutText { get; set; } = string.Empty;
    public string ClosingBalanceText { get; set; } = string.Empty;
    public decimal OpeningBalance { get; set; }
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal ClosingBalance { get; set; }
}
