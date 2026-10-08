using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.HumanResources;

public sealed class FirebirdHumanResourcesRepository : IHumanResourcesRepository
{
    public async Task<IReadOnlyList<HrDepartmentOption>> GetDepartmentsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<HrDepartmentOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, NAME, IS_ACTIVE
            FROM HR_DEPARTMENTS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
            ORDER BY DISPLAY_ORDER, NAME
            """, connection);
        command.Parameters.Add("@ACTIVE_ONLY", FbDbType.Integer).Value = activeOnly ? 1 : 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HrDepartmentOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return result;
    }

    public async Task<IReadOnlyList<HrPositionOption>> GetPositionsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<HrPositionOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, NAME, IS_ACTIVE
            FROM HR_POSITIONS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
            ORDER BY DISPLAY_ORDER, NAME
            """, connection);
        command.Parameters.Add("@ACTIVE_ONLY", FbDbType.Integer).Value = activeOnly ? 1 : 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HrPositionOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return result;
    }

    public async Task<IReadOnlyList<EmployeeListItem>> SearchAsync(
        EmployeeSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var result = new List<EmployeeListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT E.ID, E.CODE, E.FULL_NAME, E.PHOTO_PATH, COALESCE(E.GENDER, ''),
                   E.DATE_OF_BIRTH, COALESCE(E.PHONE, ''), COALESCE(E.EMAIL, ''),
                   E.HIRE_DATE, E.STATUS_CODE, E.IS_ACTIVE
            FROM HR_EMPLOYEES E
            WHERE E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@HAS_SEARCH = 0
                   OR CAST(E.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR E.FULL_NAME CONTAINING @SEARCH
                   OR COALESCE(E.PHONE, '') CONTAINING @SEARCH)
              AND (@HAS_STATUS = 0 OR E.STATUS_CODE = @STATUS_CODE)
            ORDER BY E.FULL_NAME, E.CODE
            """, connection);

        var search = filter.SearchText?.Trim() ?? string.Empty;
        var status = filter.StatusCode?.Trim() ?? string.Empty;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;
        command.Parameters.Add("@HAS_STATUS", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS_CODE", FbDbType.VarChar, 20).Value = status;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new EmployeeListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                reader.GetString(9),
                reader.GetBoolean(10)));
        }

        return result;
    }

    public async Task<EmployeeDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT E.ID, E.CODE, E.FULL_NAME, E.PHOTO_PATH, COALESCE(E.GENDER, ''),
                   E.DATE_OF_BIRTH, COALESCE(E.ATTENDANCE_CODE, ''), COALESCE(E.PHONE, ''), COALESCE(E.EMAIL, ''),
                   COALESCE(E.ADDRESS, ''), E.HIRE_DATE, COALESCE(E.CONTRACT_TYPE, ''),
                   E.BASE_SALARY, E.ALLOWANCE, COALESCE(E.NOTES, ''), E.STATUS_CODE, E.IS_ACTIVE
            FROM HR_EMPLOYEES E
            WHERE E.ID = @ID
              AND E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@ID", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new EmployeeDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetDateTime(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetDateTime(10),
            reader.GetString(11),
            reader.GetDecimal(12),
            reader.GetDecimal(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.GetBoolean(16));
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
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM HR_EMPLOYEES
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND UPPER(ATTENDANCE_CODE) = UPPER(@CODE)
              AND (@EXCLUDE_ID = 0 OR ID <> @EXCLUDE_ID)
            """, connection);
        command.Parameters.AddWithValue("@CODE", code);
        command.Parameters.AddWithValue("@EXCLUDE_ID", excludeEmployeeId ?? 0L);
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
        return count > 0;
    }

    public async Task<string> SuggestNextAttendanceCodeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT COALESCE(ATTENDANCE_CODE, '')
            FROM HR_EMPLOYEES
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND ATTENDANCE_CODE IS NOT NULL
            """, connection);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxNumeric = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var code = reader.GetString(0).Trim();
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
                await using var insert = new FbCommand("""
                    INSERT INTO HR_EMPLOYEES
                        (COMPANY_ID, CREATED_BRANCH_ID, CODE, FULL_NAME, PHOTO_PATH, GENDER, DATE_OF_BIRTH,
                         ATTENDANCE_CODE, PHONE, EMAIL, ADDRESS, HIRE_DATE,
                         CONTRACT_TYPE, BASE_SALARY, ALLOWANCE, NOTES, STATUS_CODE, IS_ACTIVE,
                         CREATED_BY, UPDATED_BY, UPDATED_AT)
                    VALUES
                        ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BRANCH_ID, @TEMP_CODE, @FULL_NAME,
                         @PHOTO_PATH, @GENDER, @DATE_OF_BIRTH, @ATTENDANCE_CODE,
                         @PHONE, @EMAIL, @ADDRESS, @HIRE_DATE, @CONTRACT_TYPE, @BASE_SALARY, @ALLOWANCE,
                         @NOTES, @STATUS_CODE, @IS_ACTIVE, @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """, connection, (FbTransaction)transaction);
                AddSaveParameters(insert, normalized, session);
                insert.Parameters.AddWithValue("@TEMP_CODE", $"TMP_{Guid.NewGuid():N}"[..30]);
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
                code = $"NV{id:000000}";

                await using var updateCode = new FbCommand("""
                    UPDATE HR_EMPLOYEES SET CODE=@CODE
                    WHERE ID=@ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, connection, (FbTransaction)transaction);
                updateCode.Parameters.AddWithValue("@CODE", code);
                updateCode.Parameters.AddWithValue("@ID", id);
                await updateCode.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                id = normalized.Id!.Value;
                previousStatus = await GetStatusAsync(connection, (FbTransaction)transaction, id, cancellationToken);

                await using var update = new FbCommand("""
                    UPDATE HR_EMPLOYEES SET
                        FULL_NAME=@FULL_NAME, PHOTO_PATH=@PHOTO_PATH, GENDER=@GENDER,
                        DATE_OF_BIRTH=@DATE_OF_BIRTH, ATTENDANCE_CODE=@ATTENDANCE_CODE, PHONE=@PHONE,
                        EMAIL=@EMAIL, ADDRESS=@ADDRESS, HIRE_DATE=@HIRE_DATE, CONTRACT_TYPE=@CONTRACT_TYPE,
                        BASE_SALARY=@BASE_SALARY, ALLOWANCE=@ALLOWANCE, NOTES=@NOTES,
                        STATUS_CODE=@STATUS_CODE, IS_ACTIVE=@IS_ACTIVE,
                        UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                    WHERE ID=@ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, connection, (FbTransaction)transaction);
                AddSaveParameters(update, normalized, session);
                update.Parameters.AddWithValue("@ID", id);
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                    throw new InvalidOperationException("Nhân viên không còn tồn tại.");

                code = await GetCodeAsync(connection, (FbTransaction)transaction, id, cancellationToken);
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
        await using var command = new FbCommand("""
            UPDATE HR_EMPLOYEES
            SET IS_ACTIVE=@ACTIVE,
                STATUS_CODE=@STATUS_CODE,
                UPDATED_BY=@USER_ID,
                UPDATED_AT=CURRENT_TIMESTAMP
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@ACTIVE", isActive);
        command.Parameters.AddWithValue("@STATUS_CODE", status);
        command.Parameters.AddWithValue("@USER_ID", session.UserId > 0 ? session.UserId : DBNull.Value);
        command.Parameters.AddWithValue("@ID", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
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

    private static EmployeeSaveRequest Normalize(EmployeeSaveRequest request)
    {
        var status = string.IsNullOrWhiteSpace(request.StatusCode)
            ? EmployeeStatusCodes.Working
            : request.StatusCode.Trim().ToUpperInvariant();

        var isActive = status != EmployeeStatusCodes.Terminated && request.IsActive;

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
            IsActive = isActive
        };
    }

    private static void AddSaveParameters(FbCommand command, EmployeeSaveRequest item, AuthenticatedSession session)
    {
        command.Parameters.AddWithValue("@BRANCH_ID", session.BranchId > 0 ? session.BranchId : DBNull.Value);
        command.Parameters.AddWithValue("@FULL_NAME", item.FullName);
        command.Parameters.AddWithValue("@PHOTO_PATH", (object?)item.PhotoPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@GENDER", NullIfEmpty(item.Gender));
        command.Parameters.AddWithValue("@DATE_OF_BIRTH", item.DateOfBirth.HasValue ? item.DateOfBirth.Value.Date : DBNull.Value);
        command.Parameters.AddWithValue("@ATTENDANCE_CODE", NullIfEmpty(item.AttendanceCode));
        command.Parameters.AddWithValue("@PHONE", NullIfEmpty(item.Phone));
        command.Parameters.AddWithValue("@EMAIL", NullIfEmpty(item.Email));
        command.Parameters.AddWithValue("@ADDRESS", NullIfEmpty(item.Address));
        command.Parameters.AddWithValue("@HIRE_DATE", item.HireDate.HasValue ? item.HireDate.Value.Date : DBNull.Value);
        command.Parameters.AddWithValue("@CONTRACT_TYPE", NullIfEmpty(item.ContractType));
        command.Parameters.AddWithValue("@BASE_SALARY", item.BaseSalary);
        command.Parameters.AddWithValue("@ALLOWANCE", item.Allowance);
        command.Parameters.AddWithValue("@NOTES", NullIfEmpty(item.Notes));
        command.Parameters.AddWithValue("@STATUS_CODE", item.StatusCode);
        command.Parameters.AddWithValue("@IS_ACTIVE", item.IsActive && item.StatusCode != EmployeeStatusCodes.Terminated);
        command.Parameters.AddWithValue("@USER_ID", session.UserId > 0 ? session.UserId : DBNull.Value);
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<string> GetCodeAsync(
        FbConnection connection,
        FbTransaction transaction,
        long employeeId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE FROM HR_EMPLOYEES
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", employeeId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    private static async Task<string?> GetStatusAsync(
        FbConnection connection,
        FbTransaction transaction,
        long employeeId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT STATUS_CODE FROM HR_EMPLOYEES
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", employeeId);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToString(value);
    }
}
