namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class SalesInvoicesPrintRow
{
    public int Index { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DocumentDateText { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string TotalAmountText { get; set; } = string.Empty;
    public string PaidAmountText { get; set; } = string.Empty;
    public string RemainingAmountText { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal RemainingAmount { get; set; }
}
