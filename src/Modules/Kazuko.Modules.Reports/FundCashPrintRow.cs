namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class FundCashPrintRow
{
    public int Index { get; set; }
    public string DayText { get; set; } = string.Empty;
    public string TotalInText { get; set; } = string.Empty;
    public string TotalOutText { get; set; } = string.Empty;
    public string ClosingBalanceText { get; set; } = string.Empty;
    public decimal TotalIn { get; set; }
    public decimal TotalOut { get; set; }
    public decimal ClosingBalance { get; set; }
}
