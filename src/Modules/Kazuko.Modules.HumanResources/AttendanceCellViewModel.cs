using Kazuko.Core.HumanResources;
using Kazuko.Core.Mvvm;

namespace Kazuko.Modules.HumanResources;

/// <summary>Một ô chấm công (nhân viên × ca × ngày) trên lưới tháng.</summary>
public sealed class AttendanceCellViewModel : ObservableObject
{
    private string _statusCode = AttendanceStatusCodes.None;
    private bool _isSelected;

    public AttendanceCellViewModel(
        long employeeId,
        long shiftId,
        DateTime date,
        int rowIndex,
        int dayIndex)
    {
        EmployeeId = employeeId;
        ShiftId = shiftId;
        Date = date.Date;
        RowIndex = rowIndex;
        DayIndex = dayIndex;
    }

    public long EmployeeId { get; }
    public long ShiftId { get; }
    public DateTime Date { get; }

    /// <summary>Chỉ số hàng toàn cục trong lưới (để chọn vùng hình chữ nhật).</summary>
    public int RowIndex { get; }

    /// <summary>Chỉ số cột ngày (0-based) trong tháng.</summary>
    public int DayIndex { get; }

    public string StatusCode
    {
        get => _statusCode;
        set => SetProperty(ref _statusCode, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
