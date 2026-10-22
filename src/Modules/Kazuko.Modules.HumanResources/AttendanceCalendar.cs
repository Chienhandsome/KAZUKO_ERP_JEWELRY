using System.Globalization;

namespace Kazuko.Modules.HumanResources;

/// <summary>Một cột ngày trong lưới chấm công tháng.</summary>
public sealed class AttendanceDayHeader
{
    public AttendanceDayHeader(DateTime date)
    {
        Date = date.Date;
        Day = date.Day;
        WeekdayLabel = AttendanceCalendar.WeekdayLabel(date.DayOfWeek);
        IsSunday = date.DayOfWeek == DayOfWeek.Sunday;
        IsSaturday = date.DayOfWeek == DayOfWeek.Saturday;
    }

    public DateTime Date { get; }
    public int Day { get; }
    public string WeekdayLabel { get; }
    public bool IsSunday { get; }
    public bool IsSaturday { get; }
}

/// <summary>Lịch tháng dùng cho lưới chấm công (thuần logic, có unit test).</summary>
public static class AttendanceCalendar
{
    private static readonly CultureInfo ViCulture = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Nhãn thứ kiểu phần mềm cũ: CN, T2, T3, T4, T5, T6, T7.</summary>
    public static string WeekdayLabel(DayOfWeek dayOfWeek) =>
        dayOfWeek switch
        {
            DayOfWeek.Sunday => "CN",
            DayOfWeek.Monday => "T2",
            DayOfWeek.Tuesday => "T3",
            DayOfWeek.Wednesday => "T4",
            DayOfWeek.Thursday => "T5",
            DayOfWeek.Friday => "T6",
            DayOfWeek.Saturday => "T7",
            _ => string.Empty
        };

    /// <summary>Danh sách các ngày của một tháng theo thứ tự tăng dần.</summary>
    public static IReadOnlyList<AttendanceDayHeader> BuildDays(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var days = new List<AttendanceDayHeader>(daysInMonth);
        for (var day = 1; day <= daysInMonth; day++)
            days.Add(new AttendanceDayHeader(new DateTime(year, month, day)));
        return days;
    }

    /// <summary>Tên kỳ hiển thị, VD: "Tháng 10/2026".</summary>
    public static string PeriodDisplayName(int year, int month) =>
        $"Tháng {month}/{year}";

    public static string MonthTitle(int year, int month) =>
        new DateTime(year, month, 1).ToString("'Tháng' MM/yyyy", ViCulture);
}
