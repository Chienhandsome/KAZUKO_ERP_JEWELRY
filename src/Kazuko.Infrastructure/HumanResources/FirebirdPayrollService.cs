using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.HumanResources;

public sealed class FirebirdPayrollService : IPayrollService
{
    private const string Company = "(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO')";
    private sealed class Header
    {
        public long Id { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public string Status { get; set; } = "";
        public int Revision { get; set; }
        public int ShiftsPerDay { get; set; }
        public bool IncludePaidLeave { get; set; }
        public string InputHash { get; set; } = "";
        public DateTime? ClosedAt { get; set; }
    }
    private sealed record SourceAttendance(long EmployeeId, long ShiftId, string ShiftName, DateTime WorkDate, string StatusCode);
    private const string HeaderSql = "SELECT ID AS Id, PAY_YEAR AS \"Year\", PAY_MONTH AS \"Month\", STATUS AS Status, REVISION AS Revision, SHIFTS_PER_DAY AS ShiftsPerDay, INCLUDE_PAID_LEAVE AS IncludePaidLeave, INPUT_HASH AS InputHash, CLOSED_AT AS ClosedAt FROM HR_PAYROLL_PERIODS WHERE COMPANY_ID=" + Company;

    public async Task<PayrollPeriod?> GetAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        ValidateDate(year, month);
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var h = await c.QuerySingleOrDefaultAsync<Header>(Cmd(HeaderSql + " AND PAY_YEAR=@year AND PAY_MONTH=@month", new { year, month }, tx, cancellationToken));
        return h is null ? null : await ReadAsync(c, tx, h, cancellationToken);
    }

    public async Task<PayrollPeriod> CalculateAsync(int year, int month, PayrollSettings settings, int? revision,
        AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session); ValidateDate(year, month);
        if (settings.ShiftsPerDay is < 1 or > 24) throw new InvalidOperationException("Số ca chuẩn/ngày phải từ 1 đến 24.");
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var h = await c.QuerySingleOrDefaultAsync<Header>(Cmd(HeaderSql + " AND PAY_YEAR=@year AND PAY_MONTH=@month", new { year, month }, tx, cancellationToken));
        PayrollPeriod? before = null;
        if (h is not null)
        {
            before = await ReadAsync(c, tx, h, cancellationToken);
            await ClaimAsync(c, tx, h, revision, "DRAFT", cancellationToken);
        }
        else if (revision is not null) throw new InvalidOperationException("Kỳ lương đã thay đổi. Hãy tải lại.");
        var source = await SourceAsync(c, tx, year, month, cancellationToken);
        if (source.Count == 0) throw new InvalidOperationException("Không có nhân viên thuộc kỳ lương này.");
        if (before?.Lines.Any(l => (l.Bonus != 0 || l.Deduction != 0) && !source.Any(s => s.Employee.Id == l.EmployeeId)) == true)
            throw new InvalidOperationException("Có nhân viên không còn trong nguồn nhưng vẫn có điều chỉnh. Bỏ điều chỉnh trước khi tính lại.");
        var hash = Hash(year, month, settings, source);
        if (h is null)
        {
            var id = await c.QuerySingleAsync<long>(Cmd("INSERT INTO HR_PAYROLL_PERIODS(COMPANY_ID,PAY_YEAR,PAY_MONTH,SHIFTS_PER_DAY,INCLUDE_PAID_LEAVE,INPUT_HASH,CREATED_BY) VALUES(" + Company + ",@year,@month,@ShiftsPerDay,@IncludePaidLeave,@hash,@user) RETURNING ID",
                new { year, month, settings.ShiftsPerDay, settings.IncludePaidLeave, hash, user = session.UserId }, tx, cancellationToken));
            h = new Header { Id = id, Year = year, Month = month, Status = "DRAFT", Revision = 1 };
        }
        else
        {
            await c.ExecuteAsync(Cmd("UPDATE HR_PAYROLL_PERIODS SET SHIFTS_PER_DAY=@ShiftsPerDay, INCLUDE_PAID_LEAVE=@IncludePaidLeave, INPUT_HASH=@hash WHERE ID=@Id", new { h.Id, settings.ShiftsPerDay, settings.IncludePaidLeave, hash }, tx, cancellationToken));
            await c.ExecuteAsync(Cmd("DELETE FROM HR_PAYROLL_LINES WHERE PERIOD_ID=@Id", new { h.Id }, tx, cancellationToken));
        }
        h.ShiftsPerDay = settings.ShiftsPerDay; h.IncludePaidLeave = settings.IncludePaidLeave; h.InputHash = hash;
        foreach (var input in source)
        {
            var a = PayrollCalculator.Calculate(year, month, settings, input);
            var old = before?.Lines.FirstOrDefault(l => l.EmployeeId == input.Employee.Id);
            var bonus = old?.Bonus ?? 0; var deduction = old?.Deduction ?? 0;
            PayrollCalculator.ValidateAdjustment(a.Salary, new(h.Id, h.Revision, 0, bonus, old?.BonusNote ?? "", deduction, old?.DeductionNote ?? ""));
            await c.ExecuteAsync(Cmd("""
                INSERT INTO HR_PAYROLL_LINES(PERIOD_ID,EMPLOYEE_ID,CODE,FULL_NAME,SALARY_METHOD,STANDARD_UNITS,ACTUAL_UNITS,RATE,SALARY,BONUS,BONUS_NOTE,DEDUCTION,DEDUCTION_NOTE,WARNING,SNAPSHOT_JSON)
                VALUES(@period,@employee,@code,@name,@method,@StandardUnits,@ActualUnits,@Rate,@Salary,@bonus,@bonusNote,@deduction,@deductionNote,@Warning,@snapshot)
                """, new { period = h.Id, employee = input.Employee.Id, code = input.Employee.Code, name = input.Employee.FullName,
                    method = input.Employee.SalaryMethod, a.StandardUnits, a.ActualUnits, a.Rate, a.Salary, bonus, bonusNote = old?.BonusNote ?? "", deduction, deductionNote = old?.DeductionNote ?? "", a.Warning,
                    snapshot = new TextBlob(JsonSerializer.Serialize(new PayrollSnapshot(1, year, month, settings, input))) }, tx, cancellationToken));
        }
        var result = await ReadAsync(c, tx, h, cancellationToken);
        await AuditAsync(c, tx, session, "PAYROLL_CALCULATE", h.Id, new { Before = before, After = result }, cancellationToken);
        await tx.CommitAsync(cancellationToken); return result;
    }

    public async Task<PayrollPeriod> AdjustAsync(PayrollAdjustment request, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var h = await RequireAsync(c, tx, request.PeriodId, cancellationToken);
        await ClaimAsync(c, tx, h, request.Revision, "DRAFT", cancellationToken);
        var before = await ReadAsync(c, tx, h, cancellationToken);
        var line = before.Lines.SingleOrDefault(l => l.Id == request.LineId) ?? throw new InvalidOperationException("Dòng lương không tồn tại.");
        PayrollCalculator.ValidateAdjustment(line.Salary, request);
        request = request with { Bonus = decimal.Round(request.Bonus, 0, MidpointRounding.AwayFromZero), Deduction = decimal.Round(request.Deduction, 0, MidpointRounding.AwayFromZero), BonusNote = request.BonusNote.Trim(), DeductionNote = request.DeductionNote.Trim() };
        PayrollCalculator.ValidateAdjustment(line.Salary, request);
        await c.ExecuteAsync(Cmd("UPDATE HR_PAYROLL_LINES SET BONUS=@Bonus,BONUS_NOTE=@BonusNote,DEDUCTION=@Deduction,DEDUCTION_NOTE=@DeductionNote WHERE ID=@LineId AND PERIOD_ID=@PeriodId", request, tx, cancellationToken));
        var result = await ReadAsync(c, tx, h, cancellationToken);
        await AuditAsync(c, tx, session, "PAYROLL_ADJUST", h.Id, new { Before = line, After = request }, cancellationToken);
        await tx.CommitAsync(cancellationToken); return result;
    }

    public async Task<PayrollPeriod> CloseAsync(long periodId, int revision, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var h = await RequireAsync(c, tx, periodId, cancellationToken);
        await ClaimAsync(c, tx, h, revision, "DRAFT", cancellationToken);
        var source = await SourceAsync(c, tx, h.Year, h.Month, cancellationToken);
        if (Hash(h.Year, h.Month, new(h.ShiftsPerDay, h.IncludePaidLeave), source) != h.InputHash)
            throw new InvalidOperationException("Chấm công hoặc hồ sơ đã thay đổi. Tính lại bảng lương trước khi chốt.");
        var before = await ReadAsync(c, tx, h, cancellationToken);
        if (before.Lines.Count == 0) throw new InvalidOperationException("Bảng lương rỗng, không thể chốt.");
        await c.ExecuteAsync(Cmd("UPDATE HR_PAYROLL_PERIODS SET STATUS='CLOSED',CLOSED_BY=@user,CLOSED_AT=CURRENT_TIMESTAMP WHERE ID=@periodId", new { periodId, user = session.UserId }, tx, cancellationToken));
        h = await RequireAsync(c, tx, periodId, cancellationToken);
        var result = await ReadAsync(c, tx, h, cancellationToken);
        await AuditAsync(c, tx, session, "PAYROLL_CLOSE", h.Id, new { Before = "DRAFT", After = result }, cancellationToken);
        await tx.CommitAsync(cancellationToken); return result;
    }

    public async Task<PayrollPeriod> ReopenAsync(long periodId, int revision, string reason, AuthenticatedSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 500) throw new InvalidOperationException("Nhập lý do mở chốt (tối đa 500 ký tự).");
        await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync(cancellationToken);
        await using var tx = c.BeginTransaction(IsolationLevel.Serializable);
        var h = await RequireAsync(c, tx, periodId, cancellationToken);
        var before = await ReadAsync(c, tx, h, cancellationToken);
        await ClaimAsync(c, tx, h, revision, "CLOSED", cancellationToken);
        await c.ExecuteAsync(Cmd("UPDATE HR_PAYROLL_PERIODS SET STATUS='DRAFT',CLOSED_BY=NULL,CLOSED_AT=NULL WHERE ID=@periodId", new { periodId }, tx, cancellationToken));
        h.Status = "DRAFT"; h.ClosedAt = null;
        var result = await ReadAsync(c, tx, h, cancellationToken);
        await AuditAsync(c, tx, session, "PAYROLL_REOPEN", h.Id, new { Before = before, Reason = reason.Trim(), After = "DRAFT" }, cancellationToken);
        await tx.CommitAsync(cancellationToken); return result;
    }

    private static async Task<Header> RequireAsync(FbConnection c, FbTransaction tx, long id, CancellationToken ct) =>
        await c.QuerySingleOrDefaultAsync<Header>(Cmd(HeaderSql + " AND ID=@id", new { id }, tx, ct)) ?? throw new InvalidOperationException("Không tìm thấy kỳ lương.");
    private static async Task ClaimAsync(FbConnection c, FbTransaction tx, Header h, int? revision, string status, CancellationToken ct)
    {
        if (h.Status != status) throw new InvalidOperationException(status == "DRAFT" ? "Bảng lương đã chốt, cần mở chốt trước." : "Bảng lương chưa chốt.");
        if (revision != h.Revision) throw new InvalidOperationException("Bảng lương đã thay đổi. Hãy tải lại.");
        var count = await c.ExecuteAsync(Cmd("UPDATE HR_PAYROLL_PERIODS SET REVISION=REVISION+1,UPDATED_AT=CURRENT_TIMESTAMP WHERE ID=@Id AND REVISION=@revision AND STATUS=@status", new { h.Id, revision, status }, tx, ct));
        if (count != 1) throw new InvalidOperationException("Bảng lương đã thay đổi. Hãy tải lại.");
        h.Revision++;
    }
    private static async Task<PayrollPeriod> ReadAsync(FbConnection c, FbTransaction tx, Header h, CancellationToken ct)
    {
        var lines = (await c.QueryAsync<PayrollLine>(Cmd("""
            SELECT ID AS Id, EMPLOYEE_ID AS EmployeeId, CODE AS Code, FULL_NAME AS FullName, SALARY_METHOD AS SalaryMethod,
                STANDARD_UNITS AS StandardUnits, ACTUAL_UNITS AS ActualUnits, RATE AS Rate, SALARY AS Salary,
                BONUS AS Bonus, BONUS_NOTE AS BonusNote, DEDUCTION AS Deduction, DEDUCTION_NOTE AS DeductionNote,
                WARNING AS Warning, SNAPSHOT_JSON AS SnapshotJson
            FROM HR_PAYROLL_LINES WHERE PERIOD_ID=@Id ORDER BY CODE,EMPLOYEE_ID
            """, new { h.Id }, tx, ct))).AsList();
        return new(h.Id, h.Year, h.Month, h.Status, h.Revision, h.ShiftsPerDay, h.IncludePaidLeave, h.ClosedAt, lines);
    }
    private static async Task<IReadOnlyList<PayrollInput>> SourceAsync(FbConnection c, FbTransaction tx, int year, int month, CancellationToken ct)
    {
        var start = new DateTime(year, month, 1); var end = start.AddMonths(1);
        var employees = await c.QueryAsync<EmployeeDetail>(Cmd("""
            SELECT E.ID AS Id,E.CODE AS Code,E.FULL_NAME AS FullName,E.PHOTO_PATH AS PhotoPath,
                COALESCE(E.GENDER,'') AS Gender,E.DATE_OF_BIRTH AS DateOfBirth,COALESCE(E.ATTENDANCE_CODE,'') AS AttendanceCode,
                COALESCE(E.PHONE,'') AS Phone,COALESCE(E.EMAIL,'') AS Email,COALESCE(E.ADDRESS,'') AS Address,
                E.HIRE_DATE AS HireDate,COALESCE(E.CONTRACT_TYPE,'') AS ContractType,COALESCE(E.NOTES,'') AS Notes,
                E.STATUS_CODE AS StatusCode,E.IS_ACTIVE AS IsActive,E.SALARY_METHOD AS SalaryMethod,
                E.SHIFT_SALARY AS ShiftSalary,E.MONTHLY_SALARY AS MonthlySalary,E.REST_ON_SUNDAY AS RestOnSunday,E.REST_ON_SATURDAY AS RestOnSaturday
            FROM HR_EMPLOYEES E WHERE E.COMPANY_ID=(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO')
             AND (E.HIRE_DATE IS NULL OR E.HIRE_DATE<@end)
             AND ((E.IS_ACTIVE=TRUE AND E.STATUS_CODE<>'TERMINATED') OR EXISTS(SELECT 1 FROM HR_ATTENDANCE_RECORDS A
                WHERE A.EMPLOYEE_ID=E.ID AND A.COMPANY_ID=E.COMPANY_ID AND A.WORK_DATE>=@start AND A.WORK_DATE<@end))
            ORDER BY E.ID
            """, new { start, end }, tx, ct));
        var records = (await c.QueryAsync<SourceAttendance>(Cmd("""
            SELECT A.EMPLOYEE_ID AS EmployeeId,A.SHIFT_ID AS ShiftId,S.NAME AS ShiftName,A.WORK_DATE AS WorkDate,A.STATUS_CODE AS StatusCode
            FROM HR_ATTENDANCE_RECORDS A JOIN HR_WORK_SHIFTS S ON S.ID=A.SHIFT_ID AND S.COMPANY_ID=A.COMPANY_ID
            WHERE A.COMPANY_ID=(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO') AND A.WORK_DATE>=@start AND A.WORK_DATE<@end
            ORDER BY A.EMPLOYEE_ID,A.WORK_DATE,A.SHIFT_ID
            """, new { start, end }, tx, ct))).ToLookup(a => a.EmployeeId);
        return employees.Select(e => new PayrollInput(e, records[e.Id].Select(a => new PayrollAttendance(a.ShiftId, a.ShiftName, a.WorkDate, a.StatusCode)).ToArray())).ToArray();
    }
    private static string Hash(int year, int month, PayrollSettings settings, IReadOnlyList<PayrollInput> inputs) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { year, month, settings, inputs }))));
    private static void ValidateDate(int year, int month)
    {
        if (year is < 1900 or > 9998 || month is < 1 or > 12) throw new InvalidOperationException("Kỳ lương không hợp lệ.");
    }
    private static CommandDefinition Cmd(string sql, object? parameters, FbTransaction tx, CancellationToken ct) => new(sql, parameters, tx, cancellationToken: ct);
    private static Task<int> AuditAsync(FbConnection c, FbTransaction tx, AuthenticatedSession session, string action, long id, object data, CancellationToken ct) =>
        c.ExecuteAsync(Cmd("INSERT INTO AUDIT_LOG(USER_ID,USERNAME,BRANCH_ID,EVENT_TYPE,ENTITY_TYPE,ENTITY_ID,EVENT_DATA,MACHINE_NAME) VALUES(@user,@name,@branch,@action,'HR_PAYROLL_PERIODS',@id,@data,@machine)",
            new { user = session.UserId, name = session.Username, branch = session.BranchId, action, id = id.ToString(), data = new TextBlob(JsonSerializer.Serialize(data)), machine = Environment.MachineName }, tx, ct));
    // Dapper uses Size=-1 for long strings, which Firebird rejects; bind snapshots as text blobs.
    private sealed class TextBlob(string value) : SqlMapper.ICustomQueryParameter
    {
        public void AddParameter(IDbCommand command, string name)
        {
            var parameter = (FbParameter)command.CreateParameter();
            parameter.ParameterName = name; parameter.FbDbType = FbDbType.Text; parameter.Value = value;
            command.Parameters.Add(parameter);
        }
    }
}
