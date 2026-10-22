using Kazuko.Core.Authentication;

namespace Kazuko.Core.HumanResources;

/// <summary>
/// Permission keys for Human Resources. Shell RBAC may filter later;
/// commands currently require an authenticated session only.
/// </summary>
public static class HumanResourcesPermissions
{
    public const string EmployeeRead = "human-resources.employee.read";
    public const string EmployeeCreate = "human-resources.employee.create";
    public const string EmployeeUpdate = "human-resources.employee.update";
    public const string EmployeeDeactivate = "human-resources.employee.deactivate";
}

public static class EmployeeStatusCodes
{
    public const string Working = "WORKING";
    public const string OnLeave = "ON_LEAVE";
    public const string Terminated = "TERMINATED";

    public static string ToDisplay(string? code) =>
        code switch
        {
            Working => "Đang làm việc",
            OnLeave => "Nghỉ phép",
            Terminated => "Ngừng làm việc",
            _ => code ?? string.Empty
        };
}

/// <summary>Cách tính lương cấu hình trên hồ sơ nhân viên.</summary>
public static class SalaryMethodCodes
{
    /// <summary>Lương = Tổng ca làm việc × Lương ca.</summary>
    public const string PerShift = "PER_SHIFT";

    /// <summary>Lương = Tổng ca làm việc × Lương tháng / công chuẩn tháng.</summary>
    public const string MonthlyByShift = "MONTHLY_BY_SHIFT";

    /// <summary>Lương = Tổng ngày làm việc × Lương tháng / công chuẩn tháng.</summary>
    public const string MonthlyByDay = "MONTHLY_BY_DAY";

    public static string ToDisplay(string? code) =>
        code switch
        {
            PerShift => "Lương theo ca",
            MonthlyByShift => "Lương tháng theo ca",
            MonthlyByDay => "Lương tháng theo ngày",
            _ => code ?? string.Empty
        };

    public static bool IsValid(string? code) =>
        code is PerShift or MonthlyByShift or MonthlyByDay;
}

/// <summary>Trạng thái một ô chấm công (nhân viên × ca × ngày).</summary>
public static class AttendanceStatusCodes
{
    /// <summary>Không có lịch — trạng thái mặc định, không lưu record.</summary>
    public const string None = "NONE";

    public const string Work = "WORK";
    public const string PaidLeave = "PAID_LEAVE";
    public const string UnpaidLeave = "UNPAID_LEAVE";

    public static string ToDisplay(string? code) =>
        code switch
        {
            Work => "Đi làm",
            PaidLeave => "Nghỉ có phép",
            UnpaidLeave => "Nghỉ không phép",
            _ => "Không có lịch"
        };

    /// <summary>Trạng thái được lưu trong HR_ATTENDANCE_RECORDS (khác mặc định).</summary>
    public static bool IsStored(string? code) =>
        code is Work or PaidLeave or UnpaidLeave;
}

/// <summary>Lookup for future org-structure tab (not linked on employee).</summary>
public sealed record HrDepartmentOption(long Id, string Code, string Name, bool IsActive)
{
    public override string ToString() => Name;
}

/// <summary>Lookup for future org-structure tab (not linked on employee).</summary>
public sealed record HrPositionOption(long Id, string Code, string Name, bool IsActive)
{
    public override string ToString() => Name;
}

public sealed record EmployeeListItem(
    long Id,
    string Code,
    string FullName,
    string? PhotoPath,
    string Gender,
    DateTime? DateOfBirth,
    string Phone,
    string Email,
    DateTime? HireDate,
    string StatusCode,
    bool IsActive);

public sealed record EmployeeDetail(
    long Id,
    string Code,
    string FullName,
    string? PhotoPath,
    string Gender,
    DateTime? DateOfBirth,
    string AttendanceCode,
    string Phone,
    string Email,
    string Address,
    DateTime? HireDate,
    string ContractType,
    string Notes,
    string StatusCode,
    bool IsActive,
    string SalaryMethod,
    decimal ShiftSalary,
    decimal MonthlySalary,
    bool RestOnSunday,
    bool RestOnSaturday);

public sealed record EmployeeSearchFilter(
    string SearchText,
    string StatusCode);

public sealed record EmployeeSaveRequest(
    long? Id,
    string FullName,
    string? PhotoPath,
    string Gender,
    DateTime? DateOfBirth,
    string AttendanceCode,
    string Phone,
    string Email,
    string Address,
    DateTime? HireDate,
    string ContractType,
    string Notes,
    string StatusCode,
    bool IsActive,
    string SalaryMethod,
    decimal ShiftSalary,
    decimal MonthlySalary,
    bool RestOnSunday,
    bool RestOnSaturday);

/// <summary>Ca làm việc: mỗi ca tính 1 công.</summary>
public sealed record WorkShiftItem(
    long Id,
    string Name,
    TimeSpan? StartTime,
    TimeSpan? EndTime,
    int DisplayOrder,
    bool IsActive)
{
    public override string ToString() => Name;
}

public sealed record WorkShiftSaveRequest(
    long? Id,
    string Name,
    TimeSpan? StartTime,
    TimeSpan? EndTime);

/// <summary>Một record chấm công đã lưu (trạng thái khác "Không có lịch").</summary>
public sealed record AttendanceRecordItem(
    long EmployeeId,
    long ShiftId,
    DateTime WorkDate,
    string StatusCode);

/// <summary>Tháng đã có dữ liệu chấm công.</summary>
public sealed record AttendanceMonthItem(int Year, int Month, bool HasData = true);

/// <summary>Một ô chấm công cần gán trạng thái hàng loạt.</summary>
public sealed record AttendanceEntry(
    long EmployeeId,
    long ShiftId,
    DateTime WorkDate);

public sealed record EmployeeEditResult(
    long Id,
    string Code,
    string FullName,
    string StatusCode,
    bool IsActive);

public interface IEmployeeEditorService
{
    EmployeeEditResult? ShowCreate();
    EmployeeEditResult? ShowEdit(long employeeId);
}

public interface IHumanResourcesRepository
{
    Task<IReadOnlyList<HrDepartmentOption>> GetDepartmentsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HrPositionOption>> GetPositionsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EmployeeListItem>> SearchAsync(
        EmployeeSearchFilter filter,
        CancellationToken cancellationToken = default);

    Task<EmployeeDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    Task<bool> IsAttendanceCodeTakenAsync(
        string attendanceCode,
        long? excludeEmployeeId,
        CancellationToken cancellationToken = default);

    /// <summary>Gợi ý mã chấm công tiếp theo (số nguyên tăng dần, người dùng có thể sửa).</summary>
    Task<string> SuggestNextAttendanceCodeAsync(CancellationToken cancellationToken = default);

    Task<EmployeeEditResult> SaveAsync(
        EmployeeSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task SetActiveAsync(
        long id,
        bool isActive,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkShiftItem>> GetWorkShiftsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default);

    Task<WorkShiftItem> SaveWorkShiftAsync(
        WorkShiftSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task CreateAttendanceMonthAsync(int year, int month, AuthenticatedSession session, CancellationToken cancellationToken = default);

    Task DeleteAttendanceMonthAsync(int year, int month, AuthenticatedSession session, CancellationToken cancellationToken = default);

    /// <summary>Các kỳ đã lưu, kể cả kỳ chưa có ô chấm công.</summary>
    Task<IReadOnlyList<AttendanceMonthItem>> GetAttendanceMonthsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>Toàn bộ record chấm công đã lưu trong một tháng.</summary>
    Task<IReadOnlyList<AttendanceRecordItem>> GetAttendanceRecordsAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gán một trạng thái cho nhiều ô cùng lúc. Trạng thái
    /// <see cref="AttendanceStatusCodes.None"/> nghĩa là xóa record (về mặc định).
    /// </summary>
    Task SetAttendanceStatusAsync(
        IReadOnlyList<AttendanceEntry> entries,
        string statusCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

/*
 * Future slice contracts (not implemented yet):
 * - Organizational structure (departments / positions UI)
 * - Employment contracts
 * - Rewards and discipline
 */
