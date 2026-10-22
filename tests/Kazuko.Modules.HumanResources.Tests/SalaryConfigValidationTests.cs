using Kazuko.Core.HumanResources;
using Kazuko.Modules.HumanResources;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class SalaryConfigValidationTests
{
    private static EmployeeSaveRequest Request(
        string salaryMethod = SalaryMethodCodes.MonthlyByDay,
        decimal shiftSalary = 0m,
        decimal monthlySalary = 0m) =>
        new(null, "Test NV", null, "Nam", null, string.Empty, string.Empty, string.Empty,
            string.Empty, null, string.Empty, string.Empty,
            EmployeeStatusCodes.Working, true,
            salaryMethod, shiftSalary, monthlySalary, true, false);

    [Fact]
    public void Validate_ValidSalaryConfig_ReturnsNull()
    {
        Assert.Null(EmployeeValidator.Validate(Request(SalaryMethodCodes.PerShift, 150_000m, 0m)));
        Assert.Null(EmployeeValidator.Validate(Request(SalaryMethodCodes.MonthlyByShift, 0m, 9_000_000m)));
        Assert.Null(EmployeeValidator.Validate(Request(SalaryMethodCodes.MonthlyByDay, 0m, 9_000_000m)));
    }

    [Fact]
    public void Validate_InvalidSalaryMethod_ReturnsError()
    {
        var error = EmployeeValidator.Validate(Request(salaryMethod: "WEEKLY"));
        Assert.Equal("Cách tính lương không hợp lệ.", error);
    }

    [Fact]
    public void Validate_NegativeShiftSalary_ReturnsError()
    {
        var error = EmployeeValidator.Validate(Request(shiftSalary: -1m));
        Assert.Equal("Lương ca không được âm.", error);
    }

    [Fact]
    public void Validate_NegativeMonthlySalary_ReturnsError()
    {
        var error = EmployeeValidator.Validate(Request(monthlySalary: -1m));
        Assert.Equal("Lương tháng không được âm.", error);
    }

    [Theory]
    [InlineData("6:00", 6)]
    [InlineData("06:30", 6.5)]
    [InlineData("22:00", 22)]
    public void TryParseTime_ValidInput(string text, double expectedHours)
    {
        Assert.True(ShiftEditDialogViewModel.TryParseTime(text, out var value));
        Assert.Equal(TimeSpan.FromHours(expectedHours), value);
    }

    [Fact]
    public void TryParseTime_EmptyInput_MeansNoTime()
    {
        Assert.True(ShiftEditDialogViewModel.TryParseTime("  ", out var value));
        Assert.Null(value);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("abc")]
    [InlineData("6h00")]
    public void TryParseTime_InvalidInput(string text)
    {
        Assert.False(ShiftEditDialogViewModel.TryParseTime(text, out var value));
        Assert.Null(value);
    }
}
