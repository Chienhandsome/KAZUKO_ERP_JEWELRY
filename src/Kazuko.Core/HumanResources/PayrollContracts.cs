using Kazuko.Core.Authentication;

namespace Kazuko.Core.HumanResources;

public sealed record PayrollSettings(int ShiftsPerDay, bool IncludePaidLeave);
public sealed record PayrollAttendance(long ShiftId, string ShiftName, DateTime WorkDate, string StatusCode);
public sealed record PayrollInput(EmployeeDetail Employee, IReadOnlyList<PayrollAttendance> Attendance);
public sealed record PayrollSnapshot(int Version, int Year, int Month, PayrollSettings Settings, PayrollInput Input);
public sealed record PayrollAmounts(int StandardDays, decimal StandardUnits, decimal ActualUnits,
    int WorkShifts, int PaidLeaveShifts, int UnpaidLeaveShifts, decimal Rate, decimal Salary, string Warning);
public sealed record PayrollLine(long Id, long EmployeeId, string Code, string FullName, string SalaryMethod,
    decimal StandardUnits, decimal ActualUnits, decimal Rate, decimal Salary,
    decimal Bonus, string BonusNote, decimal Deduction, string DeductionNote, string Warning, string SnapshotJson)
{
    public decimal NetSalary => Salary + Bonus - Deduction;
    public string MethodLabel => SalaryMethodCodes.ToDisplay(SalaryMethod);
}
public sealed record PayrollPeriod(long Id, int Year, int Month, string Status, int Revision,
    int ShiftsPerDay, bool IncludePaidLeave, DateTime? ClosedAt, IReadOnlyList<PayrollLine> Lines)
{
    public bool IsClosed => Status == "CLOSED";
}
public sealed record PayrollAdjustment(long PeriodId, int Revision, long LineId, decimal Bonus,
    string BonusNote, decimal Deduction, string DeductionNote);
public interface IPayrollService
{
    Task<PayrollPeriod?> GetAsync(int year, int month, CancellationToken cancellationToken = default);
    Task<PayrollPeriod> CalculateAsync(int year, int month, PayrollSettings settings, int? revision,
        AuthenticatedSession session, CancellationToken cancellationToken = default);
    Task<PayrollPeriod> AdjustAsync(PayrollAdjustment request, AuthenticatedSession session,
        CancellationToken cancellationToken = default);
    Task<PayrollPeriod> CloseAsync(long periodId, int revision, AuthenticatedSession session,
        CancellationToken cancellationToken = default);
    Task<PayrollPeriod> ReopenAsync(long periodId, int revision, string reason, AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}
