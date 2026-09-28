using Kazuko.Core.HumanResources;
using Xunit;

namespace Kazuko.Infrastructure.Tests.HumanResources;

public sealed class EmployeeValidatorTests
{
    private static EmployeeSaveRequest ValidRequest() =>
        new(
            null,
            "Nguyễn Văn A",
            null,
            "Nam",
            new DateTime(1990, 1, 1),
            "CC001",
            "0901234567",
            "a@example.com",
            "HCM",
            DateTime.Today,
            "Chính thức",
            10_000_000m,
            500_000m,
            string.Empty,
            EmployeeStatusCodes.Working,
            true);

    [Fact]
    public void Validate_ReturnsNull_WhenRequestIsValid()
    {
        Assert.Null(EmployeeValidator.Validate(ValidRequest()));
    }

    [Fact]
    public void Validate_RequiresFullName()
    {
        var error = EmployeeValidator.Validate(ValidRequest() with { FullName = "  " });
        Assert.Equal("Họ và tên là bắt buộc.", error);
    }

    [Fact]
    public void Validate_RejectsNegativeSalaryAndAllowance()
    {
        Assert.Equal(
            "Lương cơ bản không được âm.",
            EmployeeValidator.Validate(ValidRequest() with { BaseSalary = -1 }));
        Assert.Equal(
            "Phụ cấp không được âm.",
            EmployeeValidator.Validate(ValidRequest() with { Allowance = -1 }));
    }

    [Fact]
    public void Validate_RejectsInvalidEmail()
    {
        Assert.Equal(
            "Email không đúng định dạng.",
            EmployeeValidator.Validate(ValidRequest() with { Email = "not-an-email" }));
    }

    [Fact]
    public void Validate_AllowsEmptyEmail()
    {
        Assert.Null(EmployeeValidator.Validate(ValidRequest() with { Email = "" }));
    }

    [Fact]
    public void StatusDisplay_MapsKnownCodes()
    {
        Assert.Equal("Đang làm việc", EmployeeStatusCodes.ToDisplay(EmployeeStatusCodes.Working));
        Assert.Equal("Ngừng làm việc", EmployeeStatusCodes.ToDisplay(EmployeeStatusCodes.Terminated));
    }
}
