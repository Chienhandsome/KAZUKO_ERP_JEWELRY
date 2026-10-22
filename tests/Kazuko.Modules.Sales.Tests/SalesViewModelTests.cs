using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Modules.Sales.Services;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class SalesViewModelTests
{
    [Fact]
    public async Task Initialize_replaces_mock_options_with_lookups()
    {
        var (vm, _, _, _, _) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);

        Assert.Equal(2, vm.BuySell.CustomerOptions.Count);
        Assert.False(Assert.Single(vm.BuyOldGold.CustomerOptions).IsWalkIn);
        Assert.True(vm.BuySell.SelectedCustomer!.IsWalkIn);
        Assert.False(vm.BuyOldGold.SelectedCustomer!.IsWalkIn);
        Assert.Equal(2, vm.BuySell.StaffOptions.Count);
        Assert.Single(vm.BuySell.OldGoldTypeOptions);
        Assert.Empty(vm.BuySell.SellLines);
        Assert.Empty(vm.BuySell.BuyLines);
        Assert.Equal(9, vm.BuySell.SelectedStaff?.Id);
    }

    [Fact]
    public async Task Old_gold_has_no_default_customer_when_only_walk_in_exists()
    {
        var (vm, _, _, _, queries) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);
        queries.Customers.RemoveAll(x => !x.IsWalkIn);
        await vm.InitializeAsync();
        Assert.Empty(vm.BuyOldGold.CustomerOptions);
        Assert.Null(vm.BuyOldGold.SelectedCustomer);
        await ((Core.Mvvm.AsyncRelayCommand)vm.NewCommand).ExecuteAsyncForTests();
        Assert.Null(vm.BuyOldGold.SelectedCustomer);
        Assert.True(vm.BuySell.SelectedCustomer!.IsWalkIn);
    }

    [Fact]
    public async Task Product_scan_adds_line_and_rejects_duplicate()
    {
        var (vm, _, _, _, _) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);

        vm.BuySell.ProductCodeInput = "SP001";
        await ((Core.Mvvm.AsyncRelayCommand)vm.BuySell.AddSellLineCommand).ExecuteAsyncForTests();
        Assert.Single(vm.BuySell.SellLines);
        Assert.Equal(311m, vm.BuySell.NewGoldTotal);

        vm.BuySell.ProductCodeInput = "SP001";
        await ((Core.Mvvm.AsyncRelayCommand)vm.BuySell.AddSellLineCommand).ExecuteAsyncForTests();
        Assert.Single(vm.BuySell.SellLines);
        Assert.Contains("đã có", vm.BuySell.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Remove_and_clear_recalculate_totals()
    {
        var (vm, _, _, _, queries) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);
        queries.Product = queries.Product! with { FinishedProductId = 101, ProductCode = "SP002" };
        vm.BuySell.ProductCodeInput = "SP001";
        // reset product to SP001 first
        queries.Product = new(
            100, "SP001", "Nhẫn", 50, "Giá 750", 20, "750", 1,
            3.25m, 0.75m, 2.5m, 111m, 25m, 8m);
        await ((Core.Mvvm.AsyncRelayCommand)vm.BuySell.AddSellLineCommand).ExecuteAsyncForTests();
        Assert.Equal(311m, vm.BuySell.NewGoldTotal);

        vm.BuySell.SelectedSellLine = vm.BuySell.SellLines[0];
        vm.BuySell.RemoveSellLineCommand.Execute(null);
        Assert.Empty(vm.BuySell.SellLines);
        Assert.Equal(0m, vm.BuySell.NewGoldTotal);
    }

    [Fact]
    public async Task PayCommand_calls_application_service_once_and_shows_receipt()
    {
        var (vm, checkout, payments, receipts, _) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);
        vm.BuySell.ProductCodeInput = "SP001";
        await ((Core.Mvvm.AsyncRelayCommand)vm.BuySell.AddSellLineCommand).ExecuteAsyncForTests();

        await ((Core.Mvvm.AsyncRelayCommand)vm.PayCommand).ExecuteAsyncForTests();

        Assert.Equal(1, checkout.PostCount);
        Assert.Equal(1, payments.ShowCount);
        Assert.Equal(1, receipts.ShowCount);
        Assert.NotNull(vm.LastResult);
        Assert.Contains("thành công", vm.SuccessMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(vm.BuySell.SellLines);
    }

    [Fact]
    public async Task Failed_post_preserves_form_state()
    {
        var (vm, checkout, _, _, _) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);
        vm.BuySell.ProductCodeInput = "SP001";
        await ((Core.Mvvm.AsyncRelayCommand)vm.BuySell.AddSellLineCommand).ExecuteAsyncForTests();
        checkout.Failure = new InvalidOperationException("Sản phẩm vừa được giao dịch.");

        await ((Core.Mvvm.AsyncRelayCommand)vm.PayCommand).ExecuteAsyncForTests();

        Assert.Single(vm.BuySell.SellLines);
        Assert.Contains("giao dịch", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, checkout.PostCount);
    }

    [Fact]
    public async Task Old_gold_preview_recalculates_derived_fields()
    {
        var (vm, _, _, _, _) = SalesTestFactory.Create();
        await WaitForIdleAsync(vm);
        vm.BuySell.SelectedOldGoldMaterial = vm.BuySell.OldGoldTypeOptions[0];
        vm.BuySell.OldTotalWeightText = "2";
        vm.BuySell.OldStoneWeightText = "0.2";
        vm.BuySell.OldValuePercentText = "100";
        vm.BuySell.OldBuyPriceText = "77";

        Assert.Equal(1.8m, vm.BuySell.OldGoldWeight);
        Assert.Equal(1.8m, vm.BuySell.OldConvertedWeight);
    }

    private static async Task WaitForIdleAsync(SalesViewModel vm)
    {
        for (var i = 0; i < 50 && vm.IsLoading; i++)
            await Task.Delay(20);
        Assert.False(vm.IsLoading);
    }
}

internal static class AsyncRelayCommandTestExtensions
{
    public static async Task ExecuteAsyncForTests(this Core.Mvvm.AsyncRelayCommand command)
    {
        // AsyncRelayCommand.Execute is async void; wait briefly for completion.
        command.Execute(null);
        await Task.Delay(50);
        for (var i = 0; i < 40 && !command.CanExecute(null); i++)
            await Task.Delay(25);
    }
}
