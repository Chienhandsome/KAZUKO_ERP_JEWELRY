using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;

namespace Kazuko.Core.Reports;

public sealed record PayrollReportFilter(int Year, int Month, long? EmployeeId = null, bool IncludeDraft = false);
public sealed record PayrollReportEmployee(long Id, string Code, string Name)
{
    public override string ToString() => Id == 0 ? Name : $"{Code} · {Name}";
}
public sealed record PayrollReportRow(long EmployeeId, string Code, string Name, string SalaryMethod,
    decimal StandardUnits, decimal ActualUnits, decimal Rate, decimal Salary,
    decimal ManualBonus, string ManualBonusNote, decimal RecordedBonus,
    decimal ManualDeduction, string ManualDeductionNote, decimal RecordedDeduction, string RecordedNotes,
    decimal PaidAmount, string? VoucherCode, DateTime? PaymentDate, string? PaymentMethod)
{
    public string MethodLabel => SalaryMethodCodes.ToDisplay(SalaryMethod);
    public decimal TotalBonus => ManualBonus + RecordedBonus;
    public decimal TotalDeduction => ManualDeduction + RecordedDeduction;
    public decimal NetSalary => Salary + TotalBonus - TotalDeduction;
    public decimal RemainingAmount => NetSalary - PaidAmount;
    public string PaymentStatus => NetSalary == 0 ? "Không phát sinh chi" : PaidAmount == 0 ? "Chưa chi" : RemainingAmount == 0 ? "Đã chi" : "Còn phải chi";
}
public sealed record PayrollReportResult(PayrollReportFilter Filter, long? PeriodId, string CompanyName, string? CompanyAddress,
    string Status, int Revision, DateTime? ClosedAt, IReadOnlyList<PayrollReportRow> Rows)
{
    public bool IsDraft => Status == "DRAFT";
}
public interface IPayrollReportService
{
    Task<IReadOnlyList<PayrollReportEmployee>> GetEmployeesAsync(int year, int month, bool includeDraft, AuthenticatedSession session, CancellationToken ct = default);
    Task<PayrollReportResult> GetAsync(PayrollReportFilter filter, AuthenticatedSession session, CancellationToken ct = default);
}

/// <summary>Opens the report module's WPF workspace without an HR -> Reports assembly dependency.</summary>
public interface IEmployeeReportsPresenter
{
    Task ShowPayrollAsync(bool individual);
}
