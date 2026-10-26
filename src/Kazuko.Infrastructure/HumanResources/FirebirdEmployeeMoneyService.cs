using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.HumanResources;

public sealed class FirebirdEmployeeMoneyService : IEmployeeMoneyService
{
    private const string AwardSql = """
        SELECT A.ID AS Id,A.EMPLOYEE_ID AS EmployeeId,A.EMPLOYEE_CODE AS EmployeeCode,A.EMPLOYEE_NAME AS EmployeeName,
          A.EVENT_DATE AS EventDate,A.KIND AS Kind,A.HANDLING AS Handling,A.AMOUNT AS Amount,A.DESCRIPTION AS Description,
          A.PAY_YEAR AS PayYear,A.PAY_MONTH AS PayMonth,A.STATUS AS Status,A.VOUCHER_ID AS VoucherId,V.CODE AS VoucherCode,
          L.PERIOD_ID AS AppliedPeriodId,P.STATUS AS AppliedPeriodStatus
        FROM HR_EMPLOYEE_AWARDS A LEFT JOIN CASH_VOUCHERS V ON V.ID=A.VOUCHER_ID
        LEFT JOIN HR_PAYROLL_AWARD_LINKS L ON L.AWARD_ID=A.ID LEFT JOIN HR_PAYROLL_PERIODS P ON P.ID=L.PERIOD_ID
        WHERE A.COMPANY_ID=@company
        """;
    public async Task<IReadOnlyList<EmployeeMoneyOption>> GetEmployeesAsync(AuthenticatedSession session, CancellationToken ct = default)
    {
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct);
        return (await c.QueryAsync<EmployeeMoneyOption>(new CommandDefinition("""
            SELECT E.ID AS Id,E.CODE AS Code,E.FULL_NAME AS FullName FROM HR_EMPLOYEES E JOIN BRANCHES B ON B.COMPANY_ID=E.COMPANY_ID
            WHERE B.ID=@BranchId ORDER BY E.CODE
            """, new { session.BranchId }, cancellationToken: ct))).AsList();
    }
    public async Task<IReadOnlyList<EmployeeAward>> ListAsync(DateTime from, DateTime to, AuthenticatedSession session, CancellationToken ct = default)
    {
        if (from.Date > to.Date || to.Year >= 9999) throw new InvalidOperationException("Khoảng ngày không hợp lệ.");
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct);
        var company = await c.QuerySingleAsync<long>(new CommandDefinition("SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { session.BranchId }, cancellationToken: ct));
        return (await c.QueryAsync<EmployeeAward>(new CommandDefinition(AwardSql + " AND A.EVENT_DATE>=@from AND A.EVENT_DATE<@end ORDER BY A.EVENT_DATE DESC,A.ID DESC",
            new { company, from = from.Date, end = to.Date.AddDays(1) }, cancellationToken: ct))).AsList();
    }
    public async Task<EmployeeAward> CreateAsync(EmployeeAwardRequest request, AuthenticatedSession session, CancellationToken ct = default)
    {
        request = EmployeeMoneyRules.Validate(request);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var company = await EmployeeMoneyStore.LockCompanyAsync(c, tx, session, ct);
        var existing = await c.QuerySingleOrDefaultAsync<RequestRow>(new CommandDefinition("SELECT ID AS Id,REQUEST_HASH AS Hash,BRANCH_ID AS BranchId FROM HR_EMPLOYEE_AWARDS WHERE COMPANY_ID=@company AND REQUEST_ID=@key",
            new { company, key = request.RequestId.ToString() }, tx, cancellationToken: ct));
        if (existing is not null)
        {
            if (existing.Hash.Trim() != hash || existing.BranchId != session.BranchId) throw new InvalidOperationException("Yêu cầu này đã được dùng với nội dung khác. Tải lại danh sách.");
            return await c.QuerySingleAsync<EmployeeAward>(new CommandDefinition(AwardSql + " AND A.ID=@id", new { company, id = existing.Id }, tx, cancellationToken: ct));
        }
        var employee = await c.QuerySingleOrDefaultAsync<EmployeeMoneyOption>(new CommandDefinition("SELECT ID AS Id,CODE AS Code,FULL_NAME AS FullName FROM HR_EMPLOYEES WHERE ID=@id AND COMPANY_ID=@company",
            new { id = request.EmployeeId, company }, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("Nhân viên không thuộc doanh nghiệp hiện tại.");
        if (request.Handling == "PAYROLL") await EnsureOpenPeriodAsync(c, tx, company, request.PayYear!.Value, request.PayMonth!.Value, ct);
        var id = await c.QuerySingleAsync<long>(new CommandDefinition("""
            INSERT INTO HR_EMPLOYEE_AWARDS(COMPANY_ID,BRANCH_ID,EMPLOYEE_ID,EMPLOYEE_CODE,EMPLOYEE_NAME,EVENT_DATE,KIND,HANDLING,AMOUNT,DESCRIPTION,PAY_YEAR,PAY_MONTH,REQUEST_ID,REQUEST_HASH,CREATED_BY)
            VALUES(@company,@BranchId,@EmployeeId,@Code,@FullName,@EventDate,@Kind,@Handling,@Amount,@Description,@PayYear,@PayMonth,@key,@hash,@UserId) RETURNING ID
            """, new { company, session.BranchId, request.EmployeeId, employee.Code, employee.FullName, request.EventDate, request.Kind, request.Handling,
                request.Amount, request.Description, request.PayYear, request.PayMonth, key = request.RequestId.ToString(), hash, session.UserId }, tx, cancellationToken: ct));
        if (request.Handling == "CASH")
        {
            var voucher = await EmployeeMoneyStore.PostAsync(c, tx, company, session, request.Kind == "REWARD" ? "OUT" : "IN", "HR_AWARD", id,
                request.Payment!, request.CategoryId!.Value, request.EventDate, employee.FullName, request.Description, ct);
            await c.ExecuteAsync(new CommandDefinition("UPDATE HR_EMPLOYEE_AWARDS SET VOUCHER_ID=@VoucherId WHERE ID=@id", new { voucher.VoucherId, id }, tx, cancellationToken: ct));
        }
        var result = await c.QuerySingleAsync<EmployeeAward>(new CommandDefinition(AwardSql + " AND A.ID=@id", new { company, id }, tx, cancellationToken: ct));
        await AuditLogWriter.WriteAsync(c, tx, "EMPLOYEE_AWARD_CREATED", session.UserId, session.Username, session.BranchId, "HR_EMPLOYEE_AWARDS", id.ToString(), JsonSerializer.Serialize(result), ct);
        await tx.CommitAsync(ct); return result;
    }
    public async Task CancelAsync(long id, string reason, AuthenticatedSession session, CancellationToken ct = default)
    {
        reason = EmployeeMoneyStore.Reason(reason);
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct); await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var company = await EmployeeMoneyStore.LockCompanyAsync(c, tx, session, ct);
        var award = await c.QuerySingleOrDefaultAsync<EmployeeAward>(new CommandDefinition(AwardSql + " AND A.ID=@id", new { company, id }, tx, cancellationToken: ct))
            ?? throw new InvalidOperationException("Không tìm thấy khoản thưởng/kỷ luật.");
        if (award.Status == "CANCELLED") return;
        if (award.Handling == "PAYROLL") await EnsureOpenPeriodAsync(c, tx, company, award.PayYear!.Value, award.PayMonth!.Value, ct);
        if (award.VoucherId is long voucher) await EmployeeMoneyStore.CancelVoucherAsync(c, tx, company, session, voucher, "HR_AWARD", id, reason, ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE HR_EMPLOYEE_AWARDS SET STATUS='CANCELLED',CANCELLED_BY=@UserId,CANCELLED_AT=CURRENT_TIMESTAMP,CANCEL_REASON=@reason WHERE ID=@id",
            new { session.UserId, reason, id }, tx, cancellationToken: ct));
        await AuditLogWriter.WriteAsync(c, tx, "EMPLOYEE_AWARD_CANCELLED", session.UserId, session.Username, session.BranchId, "HR_EMPLOYEE_AWARDS", id.ToString(), JsonSerializer.Serialize(new { Before = award, Reason = reason }), ct);
        await tx.CommitAsync(ct);
    }
    public async Task<EmployeeMoneyResult> PaySalaryAsync(PayrollPayRequest request, AuthenticatedSession session, CancellationToken ct = default)
    {
        if (request.PaymentDate == default) throw new InvalidOperationException("Chọn ngày chi lương.");
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct); await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var company = await EmployeeMoneyStore.LockCompanyAsync(c, tx, session, ct);
        var period = await c.QuerySingleOrDefaultAsync<PeriodRow>(new CommandDefinition("SELECT ID AS Id,STATUS AS Status,REVISION AS Revision,PAY_YEAR AS PayYear,PAY_MONTH AS PayMonth FROM HR_PAYROLL_PERIODS WHERE ID=@id AND COMPANY_ID=@company",
            new { id = request.PeriodId, company }, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("Không tìm thấy kỳ lương.");
        var paid = await c.QuerySingleOrDefaultAsync<PaidRow>(new CommandDefinition("""
            SELECT P.ID AS Id,P.VOUCHER_ID AS VoucherId,V.CODE AS VoucherCode,P.AMOUNT AS Amount
            FROM HR_PAYROLL_PAYMENTS P JOIN CASH_VOUCHERS V ON V.ID=P.VOUCHER_ID
            WHERE P.PERIOD_ID=@PeriodId AND P.EMPLOYEE_ID=@EmployeeId AND P.STATUS='POSTED'
            """, request, tx, cancellationToken: ct));
        if (paid is not null) return new(paid.VoucherId, paid.VoucherCode, true);
        if (period.Status != "CLOSED" || period.Revision != request.Revision) throw new InvalidOperationException("Chỉ chi bảng lương đã chốt và đúng phiên bản. Tải lại kỳ lương.");
        var line = await c.QuerySingleOrDefaultAsync<PayLine>(new CommandDefinition("""
            SELECT FULL_NAME AS FullName,SALARY+BONUS+RECORDED_BONUS-DEDUCTION-RECORDED_DEDUCTION AS Amount
            FROM HR_PAYROLL_LINES WHERE PERIOD_ID=@PeriodId AND EMPLOYEE_ID=@EmployeeId
            """, request, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("Nhân viên không có trong bảng lương.");
        EmployeeMoneyRules.ValidatePayment(request.Payment, line.Amount);
        var category = await c.QuerySingleOrDefaultAsync<long?>(new CommandDefinition("SELECT ID FROM VOUCHER_CATEGORIES WHERE COMPANY_ID=@company AND VOUCHER_TYPE='OUT' AND CODE='SALARY' AND IS_ACTIVE=TRUE",
            new { company }, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("Bật loại chi có mã SALARY (Chi lương) trong danh mục loại chi trước khi thanh toán.");
        var id = await c.QuerySingleAsync<long>(new CommandDefinition("""
            INSERT INTO HR_PAYROLL_PAYMENTS(COMPANY_ID,BRANCH_ID,PERIOD_ID,EMPLOYEE_ID,EMPLOYEE_NAME,AMOUNT,PAYMENT_DATE,STATUS,CREATED_BY)
            VALUES(@company,@BranchId,@PeriodId,@EmployeeId,@FullName,@Amount,@PaymentDate,'PENDING',@UserId) RETURNING ID
            """, new { company, session.BranchId, request.PeriodId, request.EmployeeId, line.FullName, line.Amount, request.PaymentDate, session.UserId }, tx, cancellationToken: ct));
        var voucher = await EmployeeMoneyStore.PostAsync(c, tx, company, session, "OUT", "HR_PAYROLL", id, request.Payment, category, request.PaymentDate,
            line.FullName, $"Chi lương {period.PayMonth:00}/{period.PayYear} · {line.FullName}", ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE HR_PAYROLL_PAYMENTS SET VOUCHER_ID=@VoucherId,STATUS='POSTED' WHERE ID=@id", new { voucher.VoucherId, id }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("UPDATE HR_PAYROLL_PERIODS SET REVISION=REVISION+1,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=@PeriodId", request, tx, cancellationToken: ct));
        await AuditLogWriter.WriteAsync(c, tx, "PAYROLL_PAID", session.UserId, session.Username, session.BranchId, "HR_PAYROLL_PAYMENTS", id.ToString(), JsonSerializer.Serialize(new { request.PeriodId, request.EmployeeId, line.Amount, voucher.Code }), ct);
        await tx.CommitAsync(ct); return new(voucher.VoucherId, voucher.Code);
    }
    public async Task CancelSalaryPaymentAsync(long periodId, long employeeId, long voucherId, string reason, AuthenticatedSession session, CancellationToken ct = default)
    {
        reason = EmployeeMoneyStore.Reason(reason);
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(ct); await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var company = await EmployeeMoneyStore.LockCompanyAsync(c, tx, session, ct);
        var payment = await c.QuerySingleOrDefaultAsync<PaidRow>(new CommandDefinition("""
            SELECT P.ID AS Id,P.VOUCHER_ID AS VoucherId,V.CODE AS VoucherCode,P.AMOUNT AS Amount
            FROM HR_PAYROLL_PAYMENTS P JOIN CASH_VOUCHERS V ON V.ID=P.VOUCHER_ID
            WHERE P.COMPANY_ID=@company AND P.PERIOD_ID=@periodId AND P.EMPLOYEE_ID=@employeeId AND P.STATUS='POSTED'
            """, new { company, periodId, employeeId }, tx, cancellationToken: ct)) ?? throw new InvalidOperationException("Không có khoản chi lương đang ghi sổ. Tải lại kỳ lương.");
        if (payment.VoucherId != voucherId) throw new InvalidOperationException("Phiếu chi lương đã thay đổi. Tải lại kỳ trước khi hủy.");
        await EmployeeMoneyStore.CancelVoucherAsync(c, tx, company, session, payment.VoucherId, "HR_PAYROLL", payment.Id, reason, ct);
        await c.ExecuteAsync(new CommandDefinition("UPDATE HR_PAYROLL_PAYMENTS SET STATUS='CANCELLED',CANCELLED_BY=@UserId,CANCELLED_AT=CURRENT_TIMESTAMP,CANCEL_REASON=@reason WHERE ID=@Id",
            new { session.UserId, reason, payment.Id }, tx, cancellationToken: ct));
        await c.ExecuteAsync(new CommandDefinition("UPDATE HR_PAYROLL_PERIODS SET REVISION=REVISION+1,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=@periodId", new { periodId }, tx, cancellationToken: ct));
        await AuditLogWriter.WriteAsync(c, tx, "PAYROLL_PAYMENT_CANCELLED", session.UserId, session.Username, session.BranchId, "HR_PAYROLL_PAYMENTS", payment.Id.ToString(), JsonSerializer.Serialize(new { payment.VoucherCode, Reason = reason }), ct);
        await tx.CommitAsync(ct);
    }
    private static async Task EnsureOpenPeriodAsync(FirebirdSql.Data.FirebirdClient.FbConnection c, FirebirdSql.Data.FirebirdClient.FbTransaction tx, long company, int year, int month, CancellationToken ct)
    {
        var status = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT STATUS FROM HR_PAYROLL_PERIODS WHERE COMPANY_ID=@company AND PAY_YEAR=@year AND PAY_MONTH=@month", new { company, year, month }, tx, cancellationToken: ct));
        if (status == "CLOSED") throw new InvalidOperationException("Kỳ lương đã chốt. Mở chốt trước khi thêm hoặc hủy khoản vào lương.");
    }
    private sealed record RequestRow(long Id, string Hash, long BranchId);
    private sealed record PeriodRow(long Id, string Status, int Revision, int PayYear, int PayMonth);
    private sealed record PayLine(string FullName, decimal Amount);
    private sealed record PaidRow(long Id, long VoucherId, string VoucherCode, decimal Amount);
}
