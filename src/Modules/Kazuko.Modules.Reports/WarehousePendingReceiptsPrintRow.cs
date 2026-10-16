namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class WarehousePendingReceiptsPrintRow
{
    public int Index { get; set; }
    public string DocumentDateText { get; set; } = string.Empty;
    public string DocumentCode { get; set; } = string.Empty;
    public string MaterialCode { get; set; } = string.Empty;
    public string MaterialName { get; set; } = string.Empty;
    public string GoldTypeName { get; set; } = string.Empty;
    public string QuantityText { get; set; } = string.Empty;
    public string UnitPriceText { get; set; } = string.Empty;
    public string AmountVndText { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal AmountVnd { get; set; }
}
