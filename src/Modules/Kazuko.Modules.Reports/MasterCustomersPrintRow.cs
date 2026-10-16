namespace Kazuko.Modules.Reports;

/// <summary>DTO phẳng để bind FastReport (business object / DataBand).</summary>
public sealed class MasterCustomersPrintRow
{
    public int Index { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string IdentityNumber { get; set; } = string.Empty;
    public string CreditLimitText { get; set; } = string.Empty;
    public decimal CreditLimit { get; set; }
}
