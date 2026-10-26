using System.Data;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Reports;

public sealed class FirebirdPayrollReportService : IPayrollReportService
{
    private static void Validate(int year, int month)
    {
        if (year is < 1900 or > 9998 || month is < 1 or > 12) throw new InvalidOperationException("Chọn tháng/năm hợp lệ từ 1900 đến 9998.");
    }
    public async Task<IReadOnlyList<PayrollReportEmployee>> GetEmployeesAsync(int year, int month, bool includeDraft, AuthenticatedSession session, CancellationToken ct = default)
    {
        Validate(year, month);
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct);
        return (await c.QueryAsync<PayrollReportEmployee>(new CommandDefinition("""
            SELECT L.EMPLOYEE_ID AS Id,L.CODE AS Code,L.FULL_NAME AS Name
            FROM HR_PAYROLL_PERIODS P JOIN HR_PAYROLL_LINES L ON L.PERIOD_ID=P.ID
            JOIN BRANCHES B ON B.COMPANY_ID=P.COMPANY_ID
            WHERE B.ID=@BranchId AND P.PAY_YEAR=@year AND P.PAY_MONTH=@month
             AND (P.STATUS='CLOSED' OR (@draft=1 AND P.STATUS='DRAFT')) ORDER BY L.CODE,L.EMPLOYEE_ID
            """, new { session.BranchId, year, month, draft = includeDraft ? 1 : 0 }, cancellationToken: ct))).AsList();
    }
    public async Task<PayrollReportResult> GetAsync(PayrollReportFilter filter, AuthenticatedSession session, CancellationToken ct = default)
    {
        Validate(filter.Year, filter.Month);
        if (filter.EmployeeId is <= 0) throw new InvalidOperationException("Nhân viên không hợp lệ.");
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var company = await c.QuerySingleOrDefaultAsync<CompanyRow>(new CommandDefinition("""
            SELECT C.ID AS Id,C.NAME AS Name,C.ADDRESS AS Address FROM COMPANIES C JOIN BRANCHES B ON B.COMPANY_ID=C.ID WHERE B.ID=@BranchId
            """, new { session.BranchId }, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("Không tìm thấy doanh nghiệp của phiên đăng nhập.");
        var period = await c.QuerySingleOrDefaultAsync<PeriodRow>(new CommandDefinition("""
            SELECT ID AS Id,STATUS AS Status,REVISION AS Revision,CLOSED_AT AS ClosedAt FROM HR_PAYROLL_PERIODS
            WHERE COMPANY_ID=@company AND PAY_YEAR=@Year AND PAY_MONTH=@Month
             AND (STATUS='CLOSED' OR (@draft=1 AND STATUS='DRAFT'))
            """, new { company = company.Id, filter.Year, filter.Month, draft = filter.IncludeDraft ? 1 : 0 }, tx, cancellationToken: ct));
        if (period is null) return new(filter, null, company.Name, company.Address, "NONE", 0, null, []);
        var rows = (await c.QueryAsync<PayrollReportRow>(new CommandDefinition("""
            SELECT L.EMPLOYEE_ID AS EmployeeId,L.CODE AS Code,L.FULL_NAME AS Name,L.SALARY_METHOD AS SalaryMethod,
             L.STANDARD_UNITS AS StandardUnits,L.ACTUAL_UNITS AS ActualUnits,L.RATE AS Rate,L.SALARY AS Salary,
             L.BONUS AS ManualBonus,L.BONUS_NOTE AS ManualBonusNote,L.RECORDED_BONUS AS RecordedBonus,
             L.DEDUCTION AS ManualDeduction,L.DEDUCTION_NOTE AS ManualDeductionNote,L.RECORDED_DEDUCTION AS RecordedDeduction,L.RECORDED_NOTES AS RecordedNotes,
             COALESCE(V.AMOUNT_VND,0) AS PaidAmount,V.CODE AS VoucherCode,CAST(V.VOUCHER_DATE AS DATE) AS PaymentDate,V.PAYMENT_METHOD AS PaymentMethod
            FROM HR_PAYROLL_LINES L
            LEFT JOIN HR_PAYROLL_PAYMENTS M ON M.PERIOD_ID=L.PERIOD_ID AND M.EMPLOYEE_ID=L.EMPLOYEE_ID AND M.COMPANY_ID=@company AND M.STATUS='POSTED'
            LEFT JOIN CASH_VOUCHERS V ON V.ID=M.VOUCHER_ID AND V.COMPANY_ID=@company AND V.STATUS_CODE='POSTED'
            WHERE L.PERIOD_ID=@period AND (@employee IS NULL OR L.EMPLOYEE_ID=@employee) ORDER BY L.CODE,L.EMPLOYEE_ID
            """, new { company = company.Id, period = period.Id, employee = filter.EmployeeId }, tx, cancellationToken: ct))).AsList();
        if (rows.Any(x => x.PaidAmount < 0 || x.RemainingAmount < 0)) throw new InvalidOperationException("Số tiền chi lương không khớp bảng lương. Kiểm tra phiếu liên kết.");
        return new(filter, period.Id, company.Name, company.Address, period.Status, period.Revision, period.ClosedAt, rows);
    }
    private sealed record CompanyRow(long Id, string Name, string? Address);
    private sealed record PeriodRow(long Id, string Status, int Revision, DateTime? ClosedAt);
}
