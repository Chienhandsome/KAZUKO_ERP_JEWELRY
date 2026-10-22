using Kazuko.Core.HumanResources;
using Kazuko.Modules.HumanResources;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class AttendanceGridBuilderTests
{
    private static EmployeeListItem Employee(long id, string name) =>
        new(id, $"NV{id:000000}", name, null, "Nam", null, string.Empty, string.Empty,
            null, EmployeeStatusCodes.Working, true);

    private static WorkShiftItem Shift(long id, string name, int order) =>
        new(id, name, null, null, order, true);

    [Fact]
    public void Build_AppliesStoredRecords_AndDefaultsOthersToNone()
    {
        var days = AttendanceCalendar.BuildDays(2026, 10);
        var records = new[]
        {
            new AttendanceRecordItem(1, 10, new DateTime(2026, 10, 5), AttendanceStatusCodes.Work),
            new AttendanceRecordItem(1, 10, new DateTime(2026, 10, 6), AttendanceStatusCodes.PaidLeave)
        };

        var groups = AttendanceGridBuilder.Build(
            [Employee(1, "Nguyen Van A")],
            [Shift(10, "Sáng", 1), Shift(11, "Chiều", 2)],
            records,
            days);

        var group = Assert.Single(groups);
        Assert.Equal(2, group.ShiftRows.Count);

        var morning = group.ShiftRows[0];
        Assert.Equal(31, morning.Cells.Count);
        Assert.Equal(AttendanceStatusCodes.Work, morning.Cells[4].StatusCode);
        Assert.Equal(AttendanceStatusCodes.PaidLeave, morning.Cells[5].StatusCode);
        Assert.Equal(AttendanceStatusCodes.None, morning.Cells[0].StatusCode);

        var afternoon = group.ShiftRows[1];
        Assert.All(afternoon.Cells, c => Assert.Equal(AttendanceStatusCodes.None, c.StatusCode));
    }

    [Fact]
    public void Build_RowIndexesAreSequentialAcrossGroups()
    {
        var days = AttendanceCalendar.BuildDays(2026, 9);

        var groups = AttendanceGridBuilder.Build(
            [Employee(1, "A"), Employee(2, "B")],
            [Shift(10, "Sáng", 1), Shift(11, "Chiều", 2)],
            [],
            days);

        Assert.Equal(0, groups[0].ShiftRows[0].Cells[0].RowIndex);
        Assert.Equal(1, groups[0].ShiftRows[1].Cells[0].RowIndex);
        Assert.Equal(2, groups[1].ShiftRows[0].Cells[0].RowIndex);
        Assert.Equal(3, groups[1].ShiftRows[1].Cells[0].RowIndex);
        Assert.Equal(29, groups[1].ShiftRows[1].Cells[^1].DayIndex);
    }

    [Fact]
    public void Build_NoShifts_ProducesGroupsWithoutRows()
    {
        var days = AttendanceCalendar.BuildDays(2026, 10);

        var groups = AttendanceGridBuilder.Build(
            [Employee(1, "A")],
            [],
            [],
            days);

        var group = Assert.Single(groups);
        Assert.Empty(group.ShiftRows);
    }
}
