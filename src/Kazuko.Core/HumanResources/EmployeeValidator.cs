using System.Text.RegularExpressions;
using Kazuko.Core.HumanResources;

namespace Kazuko.Core.HumanResources;

/// <summary>
/// Shared validation for employee create/update (ViewModel + repository).
/// </summary>
public static class EmployeeValidator
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string? Validate(EmployeeSaveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.FullName))
            return "Họ và tên là bắt buộc.";

        if (request.BaseSalary < 0)
            return "Lương cơ bản không được âm.";

        if (request.Allowance < 0)
            return "Phụ cấp không được âm.";

        var email = request.Email?.Trim() ?? string.Empty;
        if (email.Length > 0 && !EmailRegex.IsMatch(email))
            return "Email không đúng định dạng.";

        var status = request.StatusCode?.Trim() ?? string.Empty;
        if (status is not (
            EmployeeStatusCodes.Working or
            EmployeeStatusCodes.OnLeave or
            EmployeeStatusCodes.Terminated))
            return "Trạng thái nhân viên không hợp lệ.";

        return null;
    }
}
