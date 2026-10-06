namespace Kazuko.Modules.Reports;

public sealed class JewelryOnHandSummaryPrintRow
{
    public int Index { get; set; }
    public string ScopeDisplay { get; set; } = string.Empty;
    public string GoldPurity { get; set; } = string.Empty;
    public string ItemCountText { get; set; } = string.Empty;
    public string GoldWeightText { get; set; } = string.Empty;
    public string TotalWeightText { get; set; } = string.Empty;
    public string StockValueText { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public decimal GoldWeight { get; set; }
    public decimal StockValue { get; set; }
}
