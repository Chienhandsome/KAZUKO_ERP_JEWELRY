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
    decimal BaseSalary,
    decimal Allowance,
    string Notes,
    string StatusCode,
    bool IsActive);

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
    decimal BaseSalary,
    decimal Allowance,
    string Notes,
    string StatusCode,
    bool IsActive);

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
}

/*
 * Future slice contracts (not implemented in phase 1):
 * - Organizational structure (departments / positions UI)
 * - Monthly attendance / leave / late-early
 * - Payroll, bonuses, deductions
 * - Employment contracts
 * - Rewards and discipline
 */
