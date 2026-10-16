namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class WarehouseReceiptsByTypePrintRow
{
    public int Index { get; set; }
    public string TypeName { get; set; } = string.Empty;
    public string DocumentCountText { get; set; } = string.Empty;
    public string LineCountText { get; set; } = string.Empty;
    public string TotalQuantityText { get; set; } = string.Empty;
    public string TotalAmountVndText { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
    public int LineCount { get; set; }
    public decimal TotalQuantity { get; set; }
    public decimal TotalAmountVnd { get; set; }
}
