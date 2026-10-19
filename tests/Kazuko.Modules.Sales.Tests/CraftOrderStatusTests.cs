using System.Globalization;
using Kazuko.Modules.Sales.Converters;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class CraftOrderStatusTests
{
    [Theory]
    [InlineData("DRAFT", "Bản nháp")]
    [InlineData("CONFIRMED", "Đã xác nhận")]
    [InlineData("DEPOSITED", "Đã đặt cọc")]
    [InlineData("IN_PRODUCTION", "Đang chế tác")]
    [InlineData("READY", "Chờ giao hàng")]
    [InlineData("PARTIALLY_DELIVERED", "Đã giao một phần")]
    [InlineData("COMPLETED", "Hoàn tất")]
    [InlineData("CANCELLED", "Đã hủy")]
    [InlineData(" completed ", "Hoàn tất")]
    [InlineData("FUTURE_STATUS", "FUTURE_STATUS")]
    public void Status_has_readable_label_without_changing_database_code(string code, string text) =>
        Assert.Equal(text, new CraftOrderStatusTextConverter().Convert(code, typeof(string), null!, CultureInfo.InvariantCulture));

    [Fact]
    public async Task Payment_hint_does_not_call_all_collections_a_deposit()
    {
        var (vm, service) = CraftOrderWorkspaceTests.Create();
        service.Header = service.Header! with { StatusCode = "COMPLETED", DepositPaidVnd = 1405000000, HasFinishedGoodsIssue = true };
        vm.SelectedItem = CraftOrderWorkspaceTests.SampleOrder;
        await Task.Yield();
        Assert.Contains("100%", vm.SettlementHintText);
        Assert.DoesNotContain("30%", vm.SettlementHintText);
    }
}
