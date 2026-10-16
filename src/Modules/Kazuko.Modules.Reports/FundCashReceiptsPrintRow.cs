namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class FundCashReceiptsPrintRow
{
    public int Index { get; set; }
    public string Code { get; set; } = string.Empty;
    public string VoucherDateText { get; set; } = string.Empty;
    public string PartyName { get; set; } = string.Empty;
    public string AmountVndText { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string FundName { get; set; } = string.Empty;
    public decimal AmountVnd { get; set; }
}
