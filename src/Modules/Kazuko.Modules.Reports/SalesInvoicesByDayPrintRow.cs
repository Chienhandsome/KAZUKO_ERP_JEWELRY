namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesInvoicesByDayPrintRow
{
    public int Index { get; set; }
    public string DocumentDateText { get; set; } = string.Empty;
    public string InvoiceCountText { get; set; } = string.Empty;
    public string TotalRevenueText { get; set; } = string.Empty;
    public string TotalCostText { get; set; } = string.Empty;
    public string ProfitText { get; set; } = string.Empty;
    public decimal TotalRevenue { get; set; }
    public decimal TotalCost { get; set; }
    public decimal Profit { get; set; }
}
