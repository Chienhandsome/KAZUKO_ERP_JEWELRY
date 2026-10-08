using Kazuko.Core.Finance;
using Kazuko.Modules.Sales.Dialogs;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class SalesPaymentDialogViewModelTests
{
    [Fact]
    public void Receipt_creates_three_fixed_rows_with_cash_default()
    {
        var vm = CreateReceipt(net: 311m, allowDebt: true);
        Assert.Equal(3, vm.Allocations.Count);
        Assert.True(vm.ShowCardAllocation);
        Assert.Equal(PaymentMethods.Cash, vm.CashAllocation.PaymentMethod);
        Assert.Equal(311m, vm.CashAllocation.AmountVnd);
        Assert.Equal(0m, vm.BankTransferAllocation.AmountVnd);
        Assert.Equal(0m, vm.CardAllocation.AmountVnd);
        Assert.NotNull(vm.CashAllocation.CashFund);
        Assert.NotNull(vm.BankTransferAllocation.BankAccount);
        Assert.NotNull(vm.CardAllocation.BankAccount);
    }

    [Fact]
    public void Payout_hides_card_and_uses_cash_plus_transfer_only()
    {
        var vm = CreatePayout(89m);
        Assert.Equal(2, vm.Allocations.Count);
        Assert.False(vm.ShowCardAllocation);
        Assert.DoesNotContain(vm.Allocations, x =>
            string.Equals(x.PaymentMethod, PaymentMethods.Card, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(89m, vm.CashAllocation.AmountVnd);
        Assert.Equal(0m, vm.BankTransferAllocation.AmountVnd);
    }

    [Fact]
    public void Receipt_allocation_plus_debt_must_equal_net()
    {
        var vm = CreateReceipt(net: 311m, allowDebt: true);
        vm.CashAllocation.AmountVnd = 200m;
        vm.ReceivableAmountVnd = 111m;
        vm.NotifyAllocationChanged();

        Assert.True(vm.ConfirmCommand.CanExecute(null));
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogResult);
        Assert.Equal(111m, vm.Result!.ReceivableAmountVnd);
        Assert.Single(vm.Result.Allocations);
        Assert.Equal(PaymentMethods.Cash, vm.Result.Allocations[0].PaymentMethod);
    }

    [Fact]
    public void Confirm_filters_zero_amount_rows()
    {
        var vm = CreateReceipt(311m, allowDebt: false);
        vm.CashAllocation.AmountVnd = 200m;
        vm.BankTransferAllocation.AmountVnd = 111m;
        vm.CardAllocation!.AmountVnd = 0m;
        vm.NotifyAllocationChanged();

        Assert.True(vm.ConfirmCommand.CanExecute(null));
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogResult);
        Assert.Equal(2, vm.Result!.Allocations.Count);
        Assert.DoesNotContain(vm.Result.Allocations, x =>
            string.Equals(x.PaymentMethod, PaymentMethods.Card, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Payout_requires_exact_full_allocation()
    {
        var vm = CreatePayout(89m);
        vm.CashAllocation.AmountVnd = 80m;
        vm.NotifyAllocationChanged();
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.CashAllocation.AmountVnd = 89m;
        vm.NotifyAllocationChanged();
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Card_requires_bank_account_not_cash_fund()
    {
        var vm = CreateReceipt(311m, allowDebt: false);
        vm.CashAllocation.AmountVnd = 0m;
        vm.CardAllocation!.AmountVnd = 311m;
        vm.CardAllocation.CashFund = vm.CashFunds[0];
        vm.CardAllocation.BankAccount = null;
        vm.NotifyAllocationChanged();
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.CardAllocation.CashFund = null;
        vm.CardAllocation.BankAccount = vm.BankAccounts[0];
        vm.NotifyAllocationChanged();
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Walk_in_cannot_use_receivable()
    {
        var vm = CreateReceipt(311m, allowDebt: false);
        Assert.False(vm.CanUseReceivable);
        vm.ReceivableAmountVnd = 50m;
        vm.CashAllocation.AmountVnd = 261m;
        vm.NotifyAllocationChanged();
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    private static SalesPaymentDialogViewModel CreateReceipt(decimal net, bool allowDebt) =>
        new(net, isPayout: false, canApproveDebt: true, allowReceivable: allowDebt,
            [new(1, "QTM", "Quỹ", true)],
            [new(2, "BIDV", "BIDV", "001")],
            "SALE");

    private static SalesPaymentDialogViewModel CreatePayout(decimal amount) =>
        new(-amount, isPayout: true, canApproveDebt: false, allowReceivable: false,
            [new(1, "QTM", "Quỹ", true)],
            [new(2, "BIDV", "BIDV", "001")],
            "TRADE_IN");
}
