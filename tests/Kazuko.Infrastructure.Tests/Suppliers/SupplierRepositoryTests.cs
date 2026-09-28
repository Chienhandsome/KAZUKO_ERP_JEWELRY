using Kazuko.Core.Authentication;
using Kazuko.Core.Suppliers;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Suppliers;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Suppliers;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SupplierRepositoryTests
{
    private readonly FirebirdSupplierRepository _repository = new();

    private static AuthenticatedSession CreateTestSession() =>
        new(
            UserId: 1,
            Username: "test_admin",
            DisplayName: "Test Administrator",
            RoleCode: "ADMIN",
            BranchId: 1,
            BranchCode: "CN1",
            BranchName: "Chi nhánh 1");

    [Fact]
    public void SupplierGroupOption_ToString_ReturnsName()
    {
        var group = new SupplierGroupOption(1, "VANG", "Vàng", true, true);
        Assert.Equal("Vàng", group.ToString());
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenSessionIsNull()
    {
        var request = new SupplierSaveRequest(
            null,
            "Nhà cung cấp ABC",
            1,
            "Nguyễn Văn A",
            "Giám đốc",
            "0901234567",
            "abc@example.com",
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "0301234567",
            "Ghi chú",
            true);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.SaveAsync(request, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SaveAsync_ThrowsWhenNameIsEmpty(string? name)
    {
        var session = CreateTestSession();
        var request = new SupplierSaveRequest(
            null,
            name!,
            1,
            "Nguyễn Văn A",
            "Giám đốc",
            "0901234567",
            "abc@example.com",
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "0301234567",
            "Ghi chú",
            true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.SaveAsync(request, session));
        Assert.Contains("Tên nhà cung cấp là bắt buộc", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task SaveAsync_ThrowsWhenPhoneIsEmpty(string? phone)
    {
        var session = CreateTestSession();
        var request = new SupplierSaveRequest(
            null,
            "Nhà cung cấp Vàng",
            1,
            "Nguyễn Văn A",
            "Giám đốc",
            phone!,
            "abc@example.com",
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "0301234567",
            "Ghi chú",
            true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.SaveAsync(request, session));
        Assert.Contains("Số điện thoại là bắt buộc", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SaveAsync_ThrowsWhenSupplierGroupIdIsInvalid(long groupId)
    {
        var session = CreateTestSession();
        var request = new SupplierSaveRequest(
            null,
            "Nhà cung cấp Vàng",
            groupId,
            "Nguyễn Văn A",
            "Giám đốc",
            "0901234567",
            "abc@example.com",
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "0301234567",
            "Ghi chú",
            true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.SaveAsync(request, session));
        Assert.Contains("nhóm đang hoạt động", ex.Message);
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("invalid@")]
    [InlineData("@domain.com")]
    public async Task SaveAsync_ThrowsWhenEmailFormatIsInvalid(string email)
    {
        var session = CreateTestSession();
        var request = new SupplierSaveRequest(
            null,
            "Nhà cung cấp Vàng",
            1,
            "Nguyễn Văn A",
            "Giám đốc",
            "0901234567",
            email,
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "0301234567",
            "Ghi chú",
            true);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _repository.SaveAsync(request, session));
        Assert.Contains("Email không đúng định dạng", ex.Message);
    }

    [Fact]
    public async Task SetActiveAsync_ThrowsWhenSessionIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _repository.SetActiveAsync(1, false, null!));
    }

    [Fact]
    public async Task Database_GetGroupsAsync_ReturnsSeededGroups_WhenDbIsAvailable()
    {
        if (!IsDatabaseConfigured())
            return;

        var groups = await _repository.GetGroupsAsync(activeOnly: false);
        Assert.NotNull(groups);
        Assert.True(groups.Count >= 4, "Expected at least 4 seeded supplier groups.");

        var defaultGroup = groups.SingleOrDefault(g => g.IsDefault);
        Assert.NotNull(defaultGroup);
        Assert.Equal("VANG", defaultGroup.Code);
        Assert.True(defaultGroup.IsActive);
    }

    [Fact]
    public async Task Database_SearchAsync_ReturnsList_WhenDbIsAvailable()
    {
        if (!IsDatabaseConfigured())
            return;

        var filter = new SupplierSearchFilter(
            SearchText: string.Empty,
            GroupId: 0,
            Region: string.Empty,
            IsActive: null);

        var results = await _repository.SearchAsync(filter);
        Assert.NotNull(results);
    }

    [Fact]
    public async Task FindDuplicateWarningAsync_ReturnsEmpty_WhenFieldsAreEmpty()
    {
        var request = new SupplierSaveRequest(
            null,
            "Nhà cung cấp Vàng",
            1,
            "Nguyễn Văn A",
            "Giám đốc",
            "   ",
            "abc@example.com",
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "   ",
            "Ghi chú",
            true);

        if (!IsDatabaseConfigured())
            return;

        var warning = await _repository.FindDuplicateWarningAsync(request);
        Assert.Empty(warning);
    }

    [Fact]
    public void SupplierDetail_Record_EqualityAndProperties()
    {
        var detail = new SupplierDetail(
            1,
            "NCC000001",
            "Công ty Vàng Phú Quý",
            1,
            "Vàng",
            "Nguyễn Văn A",
            "Giám đốc",
            "0901234567",
            "phuquy@example.com",
            "123 Lê Lợi",
            "TP. Hồ Chí Minh",
            "0301234567",
            "Ghi chú đối tác",
            true);

        Assert.Equal(1, detail.Id);
        Assert.Equal("NCC000001", detail.Code);
        Assert.Equal("Công ty Vàng Phú Quý", detail.Name);
        Assert.Equal(1, detail.SupplierGroupId);
        Assert.Equal("Vàng", detail.SupplierGroupName);
        Assert.Equal("Nguyễn Văn A", detail.ContactName);
        Assert.Equal("Giám đốc", detail.ContactTitle);
        Assert.Equal("0901234567", detail.Phone);
        Assert.Equal("phuquy@example.com", detail.Email);
        Assert.Equal("123 Lê Lợi", detail.Address);
        Assert.Equal("TP. Hồ Chí Minh", detail.Region);
        Assert.Equal("0301234567", detail.TaxCode);
        Assert.Equal("Ghi chú đối tác", detail.Notes);
        Assert.True(detail.IsActive);
    }

    [Fact]
    public void SupplierListItem_CalculatedFields_Preserved()
    {
        var item = new SupplierListItem(
            1,
            "NCC000001",
            "Công ty Vàng Phú Quý",
            "0901234567",
            "phuquy@example.com",
            "Vàng",
            "TP. Hồ Chí Minh",
            150000000m,
            0m,
            new DateTime(2026, 9, 25),
            true,
            "123 Lê Lợi",
            "Nguyễn Văn A",
            "Giám đốc",
            "0301234567",
            "Ghi chú",
            1);

        Assert.Equal(150000000m, item.TotalPurchases);
        Assert.Equal(0m, item.CurrentDebt);
        Assert.Equal(new DateTime(2026, 9, 25), item.LastPurchaseDate);
        Assert.True(item.IsActive);
    }

    private static bool IsDatabaseConfigured()
    {
        try
        {
            var profile = FirebirdDatabase.Profile;
            if (string.IsNullOrWhiteSpace(profile.Password) || !File.Exists(FirebirdDatabase.DatabasePath))
                return false;

            using var connection = FirebirdDatabase.CreateConnection();
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
