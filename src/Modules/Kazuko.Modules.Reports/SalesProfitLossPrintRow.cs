namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesProfitLossPrintRow
{
    public int Index { get; set; }
    public string OrderDateText { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string GoldType { get; set; } = string.Empty;
    public string CostAmountText { get; set; } = string.Empty;
    public string SaleAmountText { get; set; } = string.Empty;
    public string ProfitText { get; set; } = string.Empty;
    public decimal CostAmount { get; set; }
    public decimal SaleAmount { get; set; }
    public decimal Profit { get; set; }
}
