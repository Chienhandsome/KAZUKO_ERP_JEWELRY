namespace Kazuko.Core.HumanResources;

public static class PayrollCalculator
{
    public static PayrollAmounts Calculate(int year, int month, PayrollSettings settings, PayrollInput input)
    {
        var days = DateTime.DaysInMonth(year, month);
        if (settings.ShiftsPerDay is < 1 or > 24)
            throw new InvalidOperationException("Số ca chuẩn/ngày phải từ 1 đến 24.");
        var employee = input.Employee;
        if (!SalaryMethodCodes.IsValid(employee.SalaryMethod) || employee.ShiftSalary < 0 || employee.MonthlySalary < 0)
            throw new InvalidOperationException($"Cấu hình lương của {employee.Code} không hợp lệ.");
        var standardDays = Enumerable.Range(1, days).Select(d => new DateTime(year, month, d))
            .Count(d => !(employee.RestOnSunday && d.DayOfWeek == DayOfWeek.Sunday)
                && !(employee.RestOnSaturday && d.DayOfWeek == DayOfWeek.Saturday));
        var records = input.Attendance.Where(a => a.WorkDate.Year == year && a.WorkDate.Month == month
                && (employee.HireDate is null || a.WorkDate.Date >= employee.HireDate.Value.Date))
            .GroupBy(a => (a.WorkDate.Date, a.ShiftId)).Select(g => g.Single()).ToArray();
        var payable = records.Where(a => a.StatusCode == AttendanceStatusCodes.Work
            || settings.IncludePaidLeave && a.StatusCode == AttendanceStatusCodes.PaidLeave).ToArray();
        var perShift = employee.SalaryMethod == SalaryMethodCodes.PerShift;
        var byDay = employee.SalaryMethod == SalaryMethodCodes.MonthlyByDay;
        decimal standardUnits = byDay ? standardDays : standardDays * settings.ShiftsPerDay;
        decimal actualUnits = byDay ? payable.Select(a => a.WorkDate.Date).Distinct().Count() : payable.Length;
        var rate = perShift ? employee.ShiftSalary : employee.MonthlySalary;
        var salary = perShift ? actualUnits * rate : actualUnits * rate / standardUnits;
        var warnings = new List<string>();
        if (records.Length == 0) warnings.Add("Chưa có chấm công trong kỳ");
        if (rate == 0) warnings.Add("Đơn giá bằng 0");
        if (actualUnits > standardUnits) warnings.Add("Công thực tế vượt công chuẩn");
        return new(standardDays, standardUnits, actualUnits,
            records.Count(a => a.StatusCode == AttendanceStatusCodes.Work),
            records.Count(a => a.StatusCode == AttendanceStatusCodes.PaidLeave),
            records.Count(a => a.StatusCode == AttendanceStatusCodes.UnpaidLeave), rate,
            decimal.Round(salary, 0, MidpointRounding.AwayFromZero), string.Join("; ", warnings));
    }

    public static void ValidateAdjustment(decimal salary, PayrollAdjustment request)
    {
        if (request.Bonus < 0 || request.Deduction < 0)
            throw new InvalidOperationException("Thưởng và khấu trừ không được âm.");
        if (request.Bonus > 0 && string.IsNullOrWhiteSpace(request.BonusNote)
            || request.Deduction > 0 && string.IsNullOrWhiteSpace(request.DeductionNote))
            throw new InvalidOperationException("Nhập diễn giải cho khoản thưởng/khấu trừ.");
        if (request.Deduction > salary + request.Bonus)
            throw new InvalidOperationException("Khấu trừ vượt tổng lương và thưởng.");
        if (request.BonusNote.Length > 500 || request.DeductionNote.Length > 500)
            throw new InvalidOperationException("Diễn giải tối đa 500 ký tự.");
    }
}
