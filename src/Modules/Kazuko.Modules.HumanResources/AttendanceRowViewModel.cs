using System.Collections.ObjectModel;

namespace Kazuko.Modules.HumanResources;

/// <summary>Một hàng của lưới chấm công: một ca của một nhân viên.</summary>
public sealed class AttendanceRowViewModel
{
    public AttendanceRowViewModel(long employeeId, long shiftId, string shiftName)
    {
        EmployeeId = employeeId;
        ShiftId = shiftId;
        ShiftName = shiftName;
    }

    public long EmployeeId { get; }
    public long ShiftId { get; }
    public string ShiftName { get; }
    public ObservableCollection<AttendanceCellViewModel> Cells { get; } = new();
}

/// <summary>Nhóm các hàng ca của cùng một nhân viên (tên nhân viên hiển thị gộp).</summary>
public sealed class AttendanceEmployeeGroupViewModel
{
    public AttendanceEmployeeGroupViewModel(long employeeId, string employeeName)
    {
        EmployeeId = employeeId;
        EmployeeName = employeeName;
    }

    public long EmployeeId { get; }
    public string EmployeeName { get; }
    public ObservableCollection<AttendanceRowViewModel> ShiftRows { get; } = new();
}
