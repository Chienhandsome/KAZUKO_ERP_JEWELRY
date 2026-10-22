using Kazuko.Modules.HumanResources;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class AttendanceCalendarTests
{
    [Fact]
    public void BuildDays_February2026_Has28Days()
    {
        var days = AttendanceCalendar.BuildDays(2026, 2);

        Assert.Equal(28, days.Count);
        Assert.Equal(new DateTime(2026, 2, 1), days[0].Date);
        Assert.Equal(new DateTime(2026, 2, 28), days[^1].Date);
    }

    [Fact]
    public void BuildDays_February2024_LeapYear_Has29Days()
    {
        var days = AttendanceCalendar.BuildDays(2024, 2);

        Assert.Equal(29, days.Count);
    }

    [Fact]
    public void BuildDays_January2026_FirstDayIsThursday()
    {
        var days = AttendanceCalendar.BuildDays(2026, 1);

        Assert.Equal(31, days.Count);
        Assert.Equal("T5", days[0].WeekdayLabel);
        Assert.False(days[0].IsSunday);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, "CN")]
    [InlineData(DayOfWeek.Monday, "T2")]
    [InlineData(DayOfWeek.Tuesday, "T3")]
    [InlineData(DayOfWeek.Wednesday, "T4")]
    [InlineData(DayOfWeek.Thursday, "T5")]
    [InlineData(DayOfWeek.Friday, "T6")]
    [InlineData(DayOfWeek.Saturday, "T7")]
    public void WeekdayLabel_MatchesLegacyLabels(DayOfWeek dayOfWeek, string expected)
    {
        Assert.Equal(expected, AttendanceCalendar.WeekdayLabel(dayOfWeek));
    }

    [Fact]
    public void PeriodDisplayName_FormatsMonthAndYear()
    {
        Assert.Equal("Tháng 10/2026", AttendanceCalendar.PeriodDisplayName(2026, 10));
    }
}
