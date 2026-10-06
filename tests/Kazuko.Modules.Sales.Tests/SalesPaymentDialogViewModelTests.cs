using Kazuko.Core.Finance;
using Kazuko.Modules.Sales.Dialogs;
using Xunit;

namespace Kazuko.Modules.Sales.Tests;

public sealed class SalesPaymentDialogViewModelTests
{
    [Fact]
    public void Receipt_allocation_plus_debt_must_equal_net()
    {
        var vm = CreateReceipt(net: 311m, allowDebt: true);
        vm.Allocations[0].AmountVnd = 200m;
        vm.ReceivableAmountVnd = 111m;
        vm.NotifyAllocationChanged();

        Assert.True(vm.ConfirmCommand.CanExecute(null));
        vm.ConfirmCommand.Execute(null);
        Assert.True(vm.DialogResult);
        Assert.Equal(111m, vm.Result!.ReceivableAmountVnd);
    }

    [Fact]
    public void Payout_rejects_card()
    {
        var vm = CreatePayout(89m);
        vm.Allocations[0].PaymentMethod = PaymentMethods.Card;
        vm.Allocations[0].BankAccount = vm.BankAccounts[0];
        vm.Allocations[0].CashFund = null;
        vm.NotifyAllocationChanged();

        Assert.False(vm.ConfirmCommand.CanExecute(null));
        vm.ConfirmCommand.Execute(null);
        Assert.Null(vm.DialogResult);
        Assert.Contains("thẻ", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payout_requires_exact_full_allocation()
    {
        var vm = CreatePayout(89m);
        vm.Allocations[0].AmountVnd = 80m;
        vm.NotifyAllocationChanged();
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Allocations[0].AmountVnd = 89m;
        vm.NotifyAllocationChanged();
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Card_requires_bank_account_not_cash_fund()
    {
        var vm = CreateReceipt(311m, allowDebt: false);
        vm.Allocations[0].PaymentMethod = PaymentMethods.Card;
        vm.Allocations[0].CashFund = vm.CashFunds[0];
        vm.Allocations[0].BankAccount = null;
        vm.NotifyAllocationChanged();
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        vm.Allocations[0].CashFund = null;
        vm.Allocations[0].BankAccount = vm.BankAccounts[0];
        vm.NotifyAllocationChanged();
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Walk_in_cannot_use_receivable()
    {
        var vm = CreateReceipt(311m, allowDebt: false);
        Assert.False(vm.CanUseReceivable);
        vm.ReceivableAmountVnd = 50m;
        vm.Allocations[0].AmountVnd = 261m;
        vm.NotifyAllocationChanged();
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void Add_and_remove_allocation_rows()
    {
        var vm = CreateReceipt(311m, allowDebt: true);
        Assert.Single(vm.Allocations);
        vm.AddAllocationCommand.Execute(null);
        Assert.Equal(2, vm.Allocations.Count);
        vm.SelectedAllocation = vm.Allocations[1];
        vm.RemoveAllocationCommand.Execute(null);
        Assert.Single(vm.Allocations);
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
