namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesTopItemsPrintRow
{
    public int Index { get; set; }
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string GoldType { get; set; } = string.Empty;
    public string QuantitySoldText { get; set; } = string.Empty;
    public string RevenueText { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
}
