namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesOldGoldPurchaseSummaryByTypePrintRow
{
    public int Index { get; set; }
    public string GoldType { get; set; } = string.Empty;
    public string LineCountText { get; set; } = string.Empty;
    public string TotalGoldWeightText { get; set; } = string.Empty;
    public string TotalAmountText { get; set; } = string.Empty;
    public decimal TotalGoldWeight { get; set; }
    public decimal TotalAmount { get; set; }
}
