using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.HumanResources;
using Xunit;

namespace Kazuko.Infrastructure.Tests.HumanResources;

public sealed class HumanResourcesRepositoryTests
{
    private readonly FirebirdHumanResourcesRepository _repository = new();

    [Fact]
    public async Task SaveAsync_ThrowsWhenSessionIsNull()
    {
        var request = new EmployeeSaveRequest(
            null,
            "Test NV",
            null,
            "Nam",
            null,
            "CC-TEST",
            "0900000000",
            string.Empty,
            string.Empty,
            DateTime.Today,
            "Chính thức",
            1_000_000m,
            0m,
            string.Empty,
            EmployeeStatusCodes.Working,
            true);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.SaveAsync(request, null!));
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenFullNameMissing()
    {
        var session = new AuthenticatedSession(
            1, "test", "Test", "ADMIN", 1, "CN1", "Chi nhánh 1");

        var request = new EmployeeSaveRequest(
            null,
            "  ",
            null,
            "Nam",
            null,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            null,
            string.Empty,
            0m,
            0m,
            string.Empty,
            EmployeeStatusCodes.Working,
            true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.SaveAsync(request, session));
        Assert.Equal("Họ và tên là bắt buộc.", ex.Message);
    }

    [Fact]
    public void DepartmentOption_ToString_ReturnsName()
    {
        var dept = new HrDepartmentOption(1, "KD", "Kinh doanh", true);
        Assert.Equal("Kinh doanh", dept.ToString());
    }
}
