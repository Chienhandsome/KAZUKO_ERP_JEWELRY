using Kazuko.Core.HumanResources;

namespace Kazuko.Modules.HumanResources;

/// <summary>
/// Dựng lưới chấm công tháng từ danh sách nhân viên, ca và các record đã lưu.
/// Ô không có record giữ trạng thái mặc định "Không có lịch" (không lưu DB).
/// Thuần logic, có unit test.
/// </summary>
public static class AttendanceGridBuilder
{
    public static IReadOnlyList<AttendanceEmployeeGroupViewModel> Build(
        IReadOnlyList<EmployeeListItem> employees,
        IReadOnlyList<WorkShiftItem> shifts,
        IReadOnlyList<AttendanceRecordItem> records,
        IReadOnlyList<AttendanceDayHeader> days)
    {
        var recordLookup = new Dictionary<(long EmployeeId, long ShiftId, DateTime Date), string>();
        foreach (var record in records)
            recordLookup[(record.EmployeeId, record.ShiftId, record.WorkDate.Date)] = record.StatusCode;

        var groups = new List<AttendanceEmployeeGroupViewModel>();
        var rowIndex = 0;

        foreach (var employee in employees)
        {
            var group = new AttendanceEmployeeGroupViewModel(employee.Id, employee.FullName);
            foreach (var shift in shifts)
            {
                var row = new AttendanceRowViewModel(employee.Id, shift.Id, shift.Name);
                for (var dayIndex = 0; dayIndex < days.Count; dayIndex++)
                {
                    var day = days[dayIndex];
                    var cell = new AttendanceCellViewModel(
                        employee.Id, shift.Id, day.Date, rowIndex, dayIndex);
                    if (recordLookup.TryGetValue((employee.Id, shift.Id, day.Date), out var status))
                        cell.StatusCode = status;
                    row.Cells.Add(cell);
                }

                group.ShiftRows.Add(row);
                rowIndex++;
            }

            groups.Add(group);
        }

        return groups;
    }
}
