namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class JewelryOnHandPrintRow
{
    public int Index { get; set; }
    public string Code { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string GoldPurity { get; set; } = string.Empty;
    public string GoldWeightText { get; set; } = string.Empty;
    public string TotalWeightText { get; set; } = string.Empty;
    public string StockValueText { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string ScopeDisplay { get; set; } = string.Empty;
    public string StatusDisplay { get; set; } = string.Empty;
    public decimal GoldWeight { get; set; }
    public decimal StockValue { get; set; }
}
