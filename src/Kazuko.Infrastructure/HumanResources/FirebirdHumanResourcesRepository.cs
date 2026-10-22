using System.Data;
using FirebirdSql.Data.FirebirdClient;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.HumanResources;

// Dapper migration (phase 2): SQL giữ nguyên 100% so với bản ADO.NET tay,
// chỉ thêm alias cột để Dapper map theo TÊN vào constructor param của record
// (vd. ID AS Id). Param SQL vẫn dùng tên có dấu gạch dưới như cũ.
public sealed class FirebirdHumanResourcesRepository : IHumanResourcesRepository
{
    public async Task<IReadOnlyList<HrDepartmentOption>> GetDepartmentsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<HrDepartmentOption>(
            new CommandDefinition("""
                SELECT ID AS Id, CODE AS Code, NAME AS Name, IS_ACTIVE AS IsActive
                FROM HR_DEPARTMENTS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
                ORDER BY DISPLAY_ORDER, NAME
                """,
                new { ACTIVE_ONLY = activeOnly ? 1 : 0 },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<HrPositionOption>> GetPositionsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<HrPositionOption>(
            new CommandDefinition("""
                SELECT ID AS Id, CODE AS Code, NAME AS Name, IS_ACTIVE AS IsActive
                FROM HR_POSITIONS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
                ORDER BY DISPLAY_ORDER, NAME
                """,
                new { ACTIVE_ONLY = activeOnly ? 1 : 0 },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<EmployeeListItem>> SearchAsync(
        EmployeeSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<EmployeeListItem>(
            new CommandDefinition("""
                SELECT E.ID AS Id, E.CODE AS Code, E.FULL_NAME AS FullName, E.PHOTO_PATH AS PhotoPath,
                       COALESCE(E.GENDER, '') AS Gender,
                       E.DATE_OF_BIRTH AS DateOfBirth, COALESCE(E.PHONE, '') AS Phone, COALESCE(E.EMAIL, '') AS Email,
                       E.HIRE_DATE AS HireDate, E.STATUS_CODE AS StatusCode, E.IS_ACTIVE AS IsActive
                FROM HR_EMPLOYEES E
                WHERE E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@HAS_SEARCH = 0
                       OR CAST(E.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR E.FULL_NAME CONTAINING @SEARCH
                       OR COALESCE(E.PHONE, '') CONTAINING @SEARCH)
                  AND (@HAS_STATUS = 0 OR E.STATUS_CODE = @STATUS_CODE)
                ORDER BY E.FULL_NAME, E.CODE
                """,
                new
                {
                    HAS_SEARCH = (filter.SearchText?.Trim() ?? string.Empty).Length > 0 ? 1 : 0,
                    SEARCH = filter.SearchText?.Trim() ?? string.Empty,
                    HAS_STATUS = (filter.StatusCode?.Trim() ?? string.Empty).Length > 0 ? 1 : 0,
                    STATUS_CODE = filter.StatusCode?.Trim() ?? string.Empty
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<EmployeeDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QueryFirstOrDefaultAsync<EmployeeDetail?>(
            new CommandDefinition("""
                SELECT E.ID AS Id, E.CODE AS Code, E.FULL_NAME AS FullName, E.PHOTO_PATH AS PhotoPath,
                       COALESCE(E.GENDER, '') AS Gender,
                       E.DATE_OF_BIRTH AS DateOfBirth, COALESCE(E.ATTENDANCE_CODE, '') AS AttendanceCode,
                       COALESCE(E.PHONE, '') AS Phone, COALESCE(E.EMAIL, '') AS Email,
                       COALESCE(E.ADDRESS, '') AS Address, E.HIRE_DATE AS HireDate,
                       COALESCE(E.CONTRACT_TYPE, '') AS ContractType,
                       COALESCE(E.NOTES, '') AS Notes, E.STATUS_CODE AS StatusCode, E.IS_ACTIVE AS IsActive,
                       COALESCE(E.SALARY_METHOD, 'MONTHLY_BY_DAY') AS SalaryMethod,
                       COALESCE(E.SHIFT_SALARY, 0) AS ShiftSalary,
                       COALESCE(E.MONTHLY_SALARY, 0) AS MonthlySalary,
                       COALESCE(E.REST_ON_SUNDAY, TRUE) AS RestOnSunday,
                       COALESCE(E.REST_ON_SATURDAY, FALSE) AS RestOnSaturday
                FROM HR_EMPLOYEES E
                WHERE E.ID = @ID
                  AND E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = id },
                cancellationToken: cancellationToken));
    }

    public async Task<bool> IsAttendanceCodeTakenAsync(
        string attendanceCode,
        long? excludeEmployeeId,
        CancellationToken cancellationToken = default)
    {
        var code = attendanceCode?.Trim() ?? string.Empty;
        if (code.Length == 0)
            return false;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var count = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("""
                SELECT COUNT(*) FROM HR_EMPLOYEES
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND UPPER(ATTENDANCE_CODE) = UPPER(@CODE)
                  AND (@EXCLUDE_ID = 0 OR ID <> @EXCLUDE_ID)
                """,
                new { CODE = code, EXCLUDE_ID = excludeEmployeeId ?? 0L },
                cancellationToken: cancellationToken));
        return count > 0;
    }

    public async Task<string> SuggestNextAttendanceCodeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var codes = await connection.QueryAsync<string>(
            new CommandDefinition("""
                SELECT COALESCE(ATTENDANCE_CODE, '')
                FROM HR_EMPLOYEES
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND ATTENDANCE_CODE IS NOT NULL
                """, cancellationToken: cancellationToken));

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxNumeric = 0;
        foreach (var raw in codes)
        {
            var code = raw.Trim();
            if (code.Length == 0)
                continue;
            used.Add(code);
            if (int.TryParse(code, out var n) && n > maxNumeric)
                maxNumeric = n;
        }

        for (var candidate = Math.Max(1, maxNumeric + 1); candidate < maxNumeric + 10_000; candidate++)
        {
            var text = candidate.ToString();
            if (!used.Contains(text))
                return text;
        }

        return (maxNumeric + 1).ToString();
    }

    public async Task<EmployeeEditResult> SaveAsync(
        EmployeeSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalized = Normalize(request);
        var validationError = EmployeeValidator.Validate(normalized);
        if (validationError is not null)
            throw new InvalidOperationException(validationError);

        if (!string.IsNullOrWhiteSpace(normalized.AttendanceCode) &&
            await IsAttendanceCodeTakenAsync(normalized.AttendanceCode, normalized.Id, cancellationToken))
            throw new InvalidOperationException("Mã chấm công đã tồn tại.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            long id;
            string code;
            var isCreate = normalized.Id is null or 0;
            string? previousStatus = null;

            if (isCreate)
            {
                var insertParameters = new DynamicParameters(SaveParameters(normalized, session));
                insertParameters.Add("TEMP_CODE", $"TMP_{Guid.NewGuid():N}"[..30]);
                id = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("""
                        INSERT INTO HR_EMPLOYEES
                            (COMPANY_ID, CREATED_BRANCH_ID, CODE, FULL_NAME, PHOTO_PATH, GENDER, DATE_OF_BIRTH,
                             ATTENDANCE_CODE, PHONE, EMAIL, ADDRESS, HIRE_DATE,
                             CONTRACT_TYPE, NOTES, STATUS_CODE, IS_ACTIVE,
                             SALARY_METHOD, SHIFT_SALARY, MONTHLY_SALARY, REST_ON_SUNDAY, REST_ON_SATURDAY,
                             CREATED_BY, UPDATED_BY, UPDATED_AT)
                        VALUES
                            ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BRANCH_ID, @TEMP_CODE, @FULL_NAME,
                             @PHOTO_PATH, @GENDER, @DATE_OF_BIRTH, @ATTENDANCE_CODE,
                             @PHONE, @EMAIL, @ADDRESS, @HIRE_DATE, @CONTRACT_TYPE,
                             @NOTES, @STATUS_CODE, @IS_ACTIVE,
                             @SALARY_METHOD, @SHIFT_SALARY, @MONTHLY_SALARY, @REST_ON_SUNDAY, @REST_ON_SATURDAY,
                             @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                        RETURNING ID
                        """,
                        insertParameters,
                        transaction,
                        cancellationToken: cancellationToken));
                code = $"NV{id:000000}";

                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE HR_EMPLOYEES SET CODE=@CODE
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new { CODE = code, ID = id },
                        transaction,
                        cancellationToken: cancellationToken));
            }
            else
            {
                id = normalized.Id!.Value;
                previousStatus = await connection.QuerySingleOrDefaultAsync<string?>(
                    new CommandDefinition("""
                        SELECT STATUS_CODE FROM HR_EMPLOYEES
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new { ID = id },
                        transaction,
                        cancellationToken: cancellationToken));

                var updateParameters = new DynamicParameters(SaveParameters(normalized, session));
                updateParameters.Add("ID", id);
                var affected = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE HR_EMPLOYEES SET
                            FULL_NAME=@FULL_NAME, PHOTO_PATH=@PHOTO_PATH, GENDER=@GENDER,
                            DATE_OF_BIRTH=@DATE_OF_BIRTH, ATTENDANCE_CODE=@ATTENDANCE_CODE, PHONE=@PHONE,
                            EMAIL=@EMAIL, ADDRESS=@ADDRESS, HIRE_DATE=@HIRE_DATE, CONTRACT_TYPE=@CONTRACT_TYPE,
                            NOTES=@NOTES,
                            STATUS_CODE=@STATUS_CODE, IS_ACTIVE=@IS_ACTIVE,
                            SALARY_METHOD=@SALARY_METHOD, SHIFT_SALARY=@SHIFT_SALARY,
                            MONTHLY_SALARY=@MONTHLY_SALARY, REST_ON_SUNDAY=@REST_ON_SUNDAY,
                            REST_ON_SATURDAY=@REST_ON_SATURDAY,
                            UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        updateParameters,
                        transaction,
                        cancellationToken: cancellationToken));
                if (affected == 0)
                    throw new InvalidOperationException("Nhân viên không còn tồn tại.");

                code = await connection.QuerySingleOrDefaultAsync<string?>(
                    new CommandDefinition("""
                        SELECT CODE FROM HR_EMPLOYEES
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new { ID = id },
                        transaction,
                        cancellationToken: cancellationToken)) ?? string.Empty;
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await AuditLogWriter.WriteAsync(
                isCreate ? "EMPLOYEE_CREATED" : "EMPLOYEE_UPDATED",
                session.UserId,
                session.Username,
                session.BranchId,
                "HR_EMPLOYEES",
                id.ToString(),
                $"Name={normalized.FullName}; Status={normalized.StatusCode}",
                cancellationToken);

            if (!isCreate &&
                !string.IsNullOrWhiteSpace(previousStatus) &&
                !string.Equals(previousStatus, normalized.StatusCode, StringComparison.OrdinalIgnoreCase))
            {
                await AuditLogWriter.WriteAsync(
                    "EMPLOYEE_STATUS_CHANGED",
                    session.UserId,
                    session.Username,
                    session.BranchId,
                    "HR_EMPLOYEES",
                    id.ToString(),
                    $"OldStatus={previousStatus}; NewStatus={normalized.StatusCode}",
                    cancellationToken);
            }

            return new EmployeeEditResult(id, code, normalized.FullName, normalized.StatusCode, normalized.IsActive);
        }
        catch
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Swallow rollback failures; rethrow original.
                }
            }

            throw;
        }
    }

    public async Task SetActiveAsync(
        long id,
        bool isActive,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var status = isActive ? EmployeeStatusCodes.Working : EmployeeStatusCodes.Terminated;
        var affected = await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE HR_EMPLOYEES
                SET IS_ACTIVE=@ACTIVE,
                    STATUS_CODE=@STATUS_CODE,
                    UPDATED_BY=@USER_ID,
                    UPDATED_AT=CURRENT_TIMESTAMP
                WHERE ID=@ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new
                {
                    ACTIVE = isActive,
                    STATUS_CODE = status,
                    USER_ID = session.UserId > 0 ? (object)session.UserId : DBNull.Value,
                    ID = id
                },
                cancellationToken: cancellationToken));
        if (affected == 0)
            throw new InvalidOperationException("Nhân viên không còn tồn tại.");

        await AuditLogWriter.WriteAsync(
            isActive ? "EMPLOYEE_REACTIVATED" : "EMPLOYEE_TERMINATED",
            session.UserId,
            session.Username,
            session.BranchId,
            "HR_EMPLOYEES",
            id.ToString(),
            cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<WorkShiftItem>> GetWorkShiftsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<WorkShiftItem>(
            new CommandDefinition("""
                SELECT ID AS Id, NAME AS Name, START_TIME AS StartTime, END_TIME AS EndTime,
                       DISPLAY_ORDER AS DisplayOrder, IS_ACTIVE AS IsActive
                FROM HR_WORK_SHIFTS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
                ORDER BY DISPLAY_ORDER, NAME
                """,
                new { ACTIVE_ONLY = activeOnly ? 1 : 0 },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<WorkShiftItem> SaveWorkShiftAsync(
        WorkShiftSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var name = (request.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Tên ca là bắt buộc.");
        if (request.StartTime is { } start && request.EndTime is { } end && start == end)
            throw new InvalidOperationException("Giờ bắt đầu và giờ kết thúc không được trùng nhau.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var duplicate = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("""
                SELECT COUNT(*) FROM HR_WORK_SHIFTS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND UPPER(NAME) = UPPER(@NAME)
                  AND (@EXCLUDE_ID = 0 OR ID <> @EXCLUDE_ID)
                """,
                new { NAME = name, EXCLUDE_ID = request.Id ?? 0L },
                cancellationToken: cancellationToken));
        if (duplicate > 0)
            throw new InvalidOperationException("Tên ca đã tồn tại.");

        var userId = session.UserId > 0 ? (object)session.UserId : DBNull.Value;
        long id;
        var isCreate = request.Id is null or 0;
        if (isCreate)
        {
            id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition("""
                    INSERT INTO HR_WORK_SHIFTS
                        (COMPANY_ID, NAME, START_TIME, END_TIME, DISPLAY_ORDER, IS_ACTIVE,
                         CREATED_BY, UPDATED_BY, UPDATED_AT)
                    VALUES
                        ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @NAME, @START_TIME, @END_TIME,
                         (SELECT COALESCE(MAX(DISPLAY_ORDER), 0) + 1 FROM HR_WORK_SHIFTS
                          WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE='KAZUKO')),
                         TRUE, @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """,
                    new
                    {
                        NAME = name,
                        START_TIME = (object?)request.StartTime ?? DBNull.Value,
                        END_TIME = (object?)request.EndTime ?? DBNull.Value,
                        USER_ID = userId
                    },
                    cancellationToken: cancellationToken));
        }
        else
        {
            id = request.Id!.Value;
            var affected = await connection.ExecuteAsync(
                new CommandDefinition("""
                    UPDATE HR_WORK_SHIFTS SET
                        NAME=@NAME, START_TIME=@START_TIME, END_TIME=@END_TIME,
                        UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                    WHERE ID=@ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """,
                    new
                    {
                        NAME = name,
                        START_TIME = (object?)request.StartTime ?? DBNull.Value,
                        END_TIME = (object?)request.EndTime ?? DBNull.Value,
                        USER_ID = userId,
                        ID = id
                    },
                    cancellationToken: cancellationToken));
            if (affected == 0)
                throw new InvalidOperationException("Ca làm việc không còn tồn tại.");
        }

        await AuditLogWriter.WriteAsync(
            isCreate ? "WORK_SHIFT_CREATED" : "WORK_SHIFT_UPDATED",
            session.UserId,
            session.Username,
            session.BranchId,
            "HR_WORK_SHIFTS",
            id.ToString(),
            $"Name={name}",
            cancellationToken);

        var saved = await connection.QueryFirstOrDefaultAsync<WorkShiftItem>(
            new CommandDefinition("""
                SELECT ID AS Id, NAME AS Name, START_TIME AS StartTime, END_TIME AS EndTime,
                       DISPLAY_ORDER AS DisplayOrder, IS_ACTIVE AS IsActive
                FROM HR_WORK_SHIFTS
                WHERE ID = @ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = id },
                cancellationToken: cancellationToken));
        return saved ?? new WorkShiftItem(id, name, request.StartTime, request.EndTime, 0, true);
    }

    private static DateTime AttendanceMonthStart(int year, int month)
    {
        if (year is < 1900 or > 9998 || month is < 1 or > 12)
            throw new InvalidOperationException("Kỳ chấm công không hợp lệ.");
        return new DateTime(year, month, 1);
    }

    private static Task EnsureAttendanceMonthAsync(IDbConnection connection, IDbTransaction transaction,
        int year, int month, CancellationToken cancellationToken) => connection.ExecuteAsync(new CommandDefinition("""
            UPDATE OR INSERT INTO HR_ATTENDANCE_PERIODS(COMPANY_ID,PERIOD_YEAR,PERIOD_MONTH)
            VALUES((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'),@year,@month)
            MATCHING(COMPANY_ID,PERIOD_YEAR,PERIOD_MONTH)
            """, new { year, month }, transaction, cancellationToken: cancellationToken));

    public async Task CreateAttendanceMonthAsync(int year, int month, AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        AttendanceMonthStart(year, month);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await EnsureAttendanceMonthAsync(connection, transaction, year, month, cancellationToken);
        await AuditLogWriter.WriteAsync(connection, (FbTransaction)transaction, "ATTENDANCE_PERIOD_CREATED",
            session.UserId, session.Username, session.BranchId, "HR_ATTENDANCE_PERIODS", $"{year:0000}-{month:00}",
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAttendanceMonthAsync(int year, int month, AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var start = AttendanceMonthStart(year, month);
        var end = start.AddMonths(1);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var exists = await connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
            SELECT PERIOD_YEAR FROM HR_ATTENDANCE_PERIODS
            WHERE COMPANY_ID=(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO')
              AND PERIOD_YEAR=@year AND PERIOD_MONTH=@month WITH LOCK
            """, new { year, month }, transaction, cancellationToken: cancellationToken));
        if (exists is null) return;
        var count = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            SELECT COUNT(*) FROM HR_ATTENDANCE_RECORDS
            WHERE COMPANY_ID=(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO')
              AND WORK_DATE>=@start AND WORK_DATE<@end
            """, new { start, end }, transaction, cancellationToken: cancellationToken));
        if (count > 0) throw new InvalidOperationException("Kỳ đã có dữ liệu chấm công, không thể xóa. Đặt các ô về 'Không có lịch' trước khi xóa kỳ.");
        await connection.ExecuteAsync(new CommandDefinition("""
            DELETE FROM HR_ATTENDANCE_PERIODS WHERE COMPANY_ID=(SELECT ID FROM COMPANIES WHERE CODE='KAZUKO')
              AND PERIOD_YEAR=@year AND PERIOD_MONTH=@month
            """, new { year, month }, transaction, cancellationToken: cancellationToken));
        await AuditLogWriter.WriteAsync(connection, (FbTransaction)transaction, "ATTENDANCE_PERIOD_DELETED",
            session.UserId, session.Username, session.BranchId, "HR_ATTENDANCE_PERIODS", $"{year:0000}-{month:00}",
            cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AttendanceMonthItem>> GetAttendanceMonthsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AttendanceMonthItem>(
            new CommandDefinition("""
                SELECT P.PERIOD_YEAR AS "Year", P.PERIOD_MONTH AS "Month",
                       EXISTS(SELECT 1 FROM HR_ATTENDANCE_RECORDS A
                              WHERE A.COMPANY_ID=P.COMPANY_ID
                                AND EXTRACT(YEAR FROM A.WORK_DATE)=P.PERIOD_YEAR
                                AND EXTRACT(MONTH FROM A.WORK_DATE)=P.PERIOD_MONTH) AS HasData
                FROM HR_ATTENDANCE_PERIODS P
                WHERE P.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                ORDER BY P.PERIOD_YEAR DESC, P.PERIOD_MONTH DESC
                """,
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<AttendanceRecordItem>> GetAttendanceRecordsAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        var fromDate = new DateTime(year, month, 1);
        var toDate = fromDate.AddMonths(1);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<AttendanceRecordItem>(
            new CommandDefinition("""
                SELECT EMPLOYEE_ID AS EmployeeId, SHIFT_ID AS ShiftId,
                       WORK_DATE AS WorkDate, STATUS_CODE AS StatusCode
                FROM HR_ATTENDANCE_RECORDS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND WORK_DATE >= @FROM_DATE AND WORK_DATE < @TO_DATE
                """,
                new { FROM_DATE = fromDate, TO_DATE = toDate },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task SetAttendanceStatusAsync(
        IReadOnlyList<AttendanceEntry> entries,
        string statusCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(entries);
        var status = (statusCode ?? string.Empty).Trim().ToUpperInvariant();
        if (status != AttendanceStatusCodes.None && !AttendanceStatusCodes.IsStored(status))
            throw new InvalidOperationException("Trạng thái chấm công không hợp lệ.");
        if (entries.Count == 0)
            return;

        var userId = session.UserId > 0 ? (object)session.UserId : DBNull.Value;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            foreach (var period in entries.Select(e => (e.WorkDate.Year, e.WorkDate.Month)).Distinct().OrderBy(x => x.Year).ThenBy(x => x.Month))
            {
                AttendanceMonthStart(period.Year, period.Month);
                await EnsureAttendanceMonthAsync(connection, transaction, period.Year, period.Month, cancellationToken);
            }
            foreach (var entry in entries)
            {
                var workDate = entry.WorkDate.Date;
                if (status == AttendanceStatusCodes.None)
                {
                    await connection.ExecuteAsync(
                        new CommandDefinition("""
                            DELETE FROM HR_ATTENDANCE_RECORDS
                            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                              AND EMPLOYEE_ID = @EMPLOYEE_ID AND SHIFT_ID = @SHIFT_ID AND WORK_DATE = @WORK_DATE
                            """,
                            new { EMPLOYEE_ID = entry.EmployeeId, SHIFT_ID = entry.ShiftId, WORK_DATE = workDate },
                            transaction,
                            cancellationToken: cancellationToken));
                    continue;
                }

                var updated = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE HR_ATTENDANCE_RECORDS SET
                            STATUS_CODE = @STATUS_CODE, UPDATED_BY = @USER_ID, UPDATED_AT = CURRENT_TIMESTAMP
                        WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                          AND EMPLOYEE_ID = @EMPLOYEE_ID AND SHIFT_ID = @SHIFT_ID AND WORK_DATE = @WORK_DATE
                        """,
                        new
                        {
                            STATUS_CODE = status,
                            USER_ID = userId,
                            EMPLOYEE_ID = entry.EmployeeId,
                            SHIFT_ID = entry.ShiftId,
                            WORK_DATE = workDate
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                if (updated == 0)
                {
                    await connection.ExecuteAsync(
                        new CommandDefinition("""
                            INSERT INTO HR_ATTENDANCE_RECORDS
                                (COMPANY_ID, EMPLOYEE_ID, SHIFT_ID, WORK_DATE, STATUS_CODE,
                                 CREATED_BY, UPDATED_BY, UPDATED_AT)
                            VALUES
                                ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @EMPLOYEE_ID, @SHIFT_ID,
                                 @WORK_DATE, @STATUS_CODE, @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                            """,
                            new
                            {
                                EMPLOYEE_ID = entry.EmployeeId,
                                SHIFT_ID = entry.ShiftId,
                                WORK_DATE = workDate,
                                STATUS_CODE = status,
                                USER_ID = userId
                            },
                            transaction,
                            cancellationToken: cancellationToken));
                }
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            var first = entries.Min(e => e.WorkDate.Date);
            var last = entries.Max(e => e.WorkDate.Date);
            await AuditLogWriter.WriteAsync(
                "ATTENDANCE_STATUS_SET",
                session.UserId,
                session.Username,
                session.BranchId,
                "HR_ATTENDANCE_RECORDS",
                $"{first:yyyy-MM-dd}..{last:yyyy-MM-dd}",
                $"Status={status}; Cells={entries.Count}",
                cancellationToken);
        }
        catch
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Swallow rollback failures; rethrow original.
                }
            }

            throw;
        }
    }

    private static EmployeeSaveRequest Normalize(EmployeeSaveRequest request)
    {
        var status = string.IsNullOrWhiteSpace(request.StatusCode)
            ? EmployeeStatusCodes.Working
            : request.StatusCode.Trim().ToUpperInvariant();

        var isActive = status != EmployeeStatusCodes.Terminated && request.IsActive;

        var salaryMethod = string.IsNullOrWhiteSpace(request.SalaryMethod)
            ? SalaryMethodCodes.MonthlyByDay
            : request.SalaryMethod.Trim().ToUpperInvariant();

        return request with
        {
            FullName = (request.FullName ?? string.Empty).Trim(),
            PhotoPath = string.IsNullOrWhiteSpace(request.PhotoPath) ? null : request.PhotoPath.Trim(),
            Gender = (request.Gender ?? string.Empty).Trim(),
            AttendanceCode = (request.AttendanceCode ?? string.Empty).Trim(),
            Phone = (request.Phone ?? string.Empty).Trim(),
            Email = (request.Email ?? string.Empty).Trim(),
            Address = (request.Address ?? string.Empty).Trim(),
            ContractType = (request.ContractType ?? string.Empty).Trim(),
            Notes = (request.Notes ?? string.Empty).Trim(),
            StatusCode = status,
            IsActive = isActive,
            SalaryMethod = salaryMethod
        };
    }

    private static object SaveParameters(EmployeeSaveRequest item, AuthenticatedSession session) =>
        new
        {
            BRANCH_ID = session.BranchId > 0 ? (object)session.BranchId : DBNull.Value,
            FULL_NAME = item.FullName,
            PHOTO_PATH = (object?)item.PhotoPath ?? DBNull.Value,
            GENDER = NullIfEmpty(item.Gender),
            DATE_OF_BIRTH = (object?)item.DateOfBirth?.Date ?? DBNull.Value,
            ATTENDANCE_CODE = NullIfEmpty(item.AttendanceCode),
            PHONE = NullIfEmpty(item.Phone),
            EMAIL = NullIfEmpty(item.Email),
            ADDRESS = NullIfEmpty(item.Address),
            HIRE_DATE = (object?)item.HireDate?.Date ?? DBNull.Value,
            CONTRACT_TYPE = NullIfEmpty(item.ContractType),
            NOTES = NullIfEmpty(item.Notes),
            STATUS_CODE = item.StatusCode,
            IS_ACTIVE = item.IsActive && item.StatusCode != EmployeeStatusCodes.Terminated,
            SALARY_METHOD = item.SalaryMethod,
            SHIFT_SALARY = item.ShiftSalary,
            MONTHLY_SALARY = item.MonthlySalary,
            REST_ON_SUNDAY = item.RestOnSunday,
            REST_ON_SATURDAY = item.RestOnSaturday,
            USER_ID = session.UserId > 0 ? (object)session.UserId : DBNull.Value
        };

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
}
