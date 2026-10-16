namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesDetailByDayPrintRow
{
    public int Index { get; set; }
    public string OrderDateText { get; set; } = string.Empty;
    public string OrderCode { get; set; } = string.Empty;
    public string ProductCode { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string GoldType { get; set; } = string.Empty;
    public string GoldWeightText { get; set; } = string.Empty;
    public string AmountText { get; set; } = string.Empty;
    public decimal GoldWeight { get; set; }
    public decimal Amount { get; set; }
}
