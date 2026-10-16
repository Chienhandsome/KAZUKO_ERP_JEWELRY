namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesOldGoldPurchaseDetailByTypePrintRow
{
    public int Index { get; set; }
    public string GoldType { get; set; } = string.Empty;
    public string PurchaseDateText { get; set; } = string.Empty;
    public string DocumentCode { get; set; } = string.Empty;
    public string GoldWeightText { get; set; } = string.Empty;
    public string UnitPriceText { get; set; } = string.Empty;
    public string AmountText { get; set; } = string.Empty;
    public decimal GoldWeight { get; set; }
    public decimal Amount { get; set; }
}
