using System.Windows.Controls;
using Kazuko.Core.Customers;
using Kazuko.Core.DependencyInjection;
using Kazuko.Core.Finance;
using Kazuko.Core.Modules;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class CustomerTabsTests
{
    [Fact]
    public void Opening_reports_loads_all_sections_and_enables_exports_without_cast_errors()
    {
        CustomerTestContext.Sta(() =>
        {
            var repo = new CustomerRepositoryFake();
            var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
            vm.SelectedTabIndex = 3;
            Assert.Single(vm.TopCustomers);
            Assert.Single(vm.InactiveCustomers);
            Assert.Single(vm.GroupDistribution);
            Assert.Single(vm.RegionDistribution);
            Assert.Empty(vm.ReportErrorMessage);
            Assert.True(vm.ExportTopCustomersCommand.CanExecute(null));
            Assert.True(vm.ExportInactiveCustomersCommand.CanExecute(null));
            Assert.True(vm.ExportDistributionCommand.CanExecute(null));
            repo.Top = [];
            vm.LoadTopCustomersCommand.Execute(null);
            Assert.Empty(vm.TopCustomers);
            Assert.False(vm.ExportTopCustomersCommand.CanExecute(null));
        });
    }

    [Fact]
    public void Reversed_dates_show_error_without_querying_repository()
    {
        CustomerTestContext.Sta(() =>
        {
            var repo = new CustomerRepositoryFake();
            var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
            vm.ReportFromDate = DateTime.Today.AddDays(1);
            vm.ReportToDate = DateTime.Today;
            vm.SelectedTabIndex = 3;
            Assert.Equal(0, repo.TopCalls);
            Assert.Contains("Từ ngày", vm.ReportErrorMessage);
            Assert.False(vm.ExportTopCustomersCommand.CanExecute(null));
        });
    }

    [Fact]
    public async Task While_report_is_loading_other_actions_are_disabled_and_notified()
    {
        var repo = new CustomerRepositoryFake();
        var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
        vm.LoadTopCustomersCommand.Execute(null);
        repo.PendingTop = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var changes = 0;
        vm.ExportTopCustomersCommand.CanExecuteChanged += (_, _) => changes++;
        vm.LoadTopCustomersCommand.Execute(null);
        Assert.True(vm.IsReportLoading);
        Assert.False(vm.LoadInactiveCustomersCommand.CanExecute(null));
        Assert.False(vm.LoadDistributionCommand.CanExecute(null));
        Assert.False(vm.ExportTopCustomersCommand.CanExecute(null));
        repo.PendingTop.SetResult(repo.Top);
        for (var i = 0; i < 100 && vm.IsReportLoading; i++) await Task.Delay(10);
        Assert.False(vm.IsReportLoading);
        Assert.True(vm.LoadInactiveCustomersCommand.CanExecute(null));
        Assert.True(vm.ExportTopCustomersCommand.CanExecute(null));
        Assert.True(changes >= 2);
    }

    [Fact]
    public void Inactivity_uses_strict_day_boundary_and_labels_customers_who_never_purchased()
    {
        var repo = new CustomerRepositoryFake
        {
            Customers = [CustomerRepositoryFake.Sample(1, DateTime.Today.AddDays(-30)),
                CustomerRepositoryFake.Sample(2, DateTime.Today.AddDays(-31)), CustomerRepositoryFake.Sample(3)]
        };
        var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
        vm.LoadInactiveCustomersCommand.Execute(null);
        Assert.Equal(new long[] { 3, 2 }, vm.InactiveCustomers.Select(x => x.Item.Id));
        Assert.Equal("Chưa mua", vm.InactiveCustomers[0].DaysSinceLastPurchaseText);
        Assert.Equal("31", vm.InactiveCustomers[1].DaysSinceLastPurchaseText);
    }

    [Fact]
    public void Debt_history_includes_refunds_and_displays_real_payment_methods()
    {
        var debt = DebtRepositoryProxy.Create((method, _) => method.Name == "GetCustomerLedgerAsync"
            ? Task.FromResult<IReadOnlyList<DebtLedgerEntry>>([
                new(DateTime.Today, "MB01", "PAYMENT / CASH", 1000000, 0, 0, DebtLedgerSourceTypes.OrderPayment),
                new(DateTime.Today, "MB01", "REFUND / BANK_TRANSFER", 0, 500000, 0, DebtLedgerSourceTypes.OrderPayment),
                new(DateTime.Today, "TT01", "Bù trừ vàng cũ", 500000, 0, 0, DebtLedgerSourceTypes.SalesCheckoutOffset)])
            : throw new NotSupportedException());
        var vm = new CustomerViewModel(new CustomerRepositoryFake(), new CustomerEditorFake(), CustomerTestContext.Session(), debt);
        vm.DebtTab.SelectedCustomer = vm.DebtTab.CustomerOptions[0];
        Assert.Equal(2, vm.DebtTab.PaymentHistory.Count);
        Assert.Equal("Tiền mặt", vm.DebtTab.PaymentHistory[0].MethodText);
        Assert.Equal("Chuyển khoản", vm.DebtTab.PaymentHistory[1].MethodText);
        Assert.Equal(500000, vm.DebtTab.PaymentHistory[1].PaymentAmountVnd);
        Assert.False(vm.CollectPaymentCommand.CanExecute(null)); // dialog unavailable
    }

    [Fact]
    public async Task Old_customer_ledger_cannot_overwrite_current_customer_after_selection_changes()
    {
        var first = new TaskCompletionSource<IReadOnlyList<DebtLedgerEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var debt = DebtRepositoryProxy.Create((_, args) => (long)args![0]! == 1 ? first.Task : Task.FromResult<IReadOnlyList<DebtLedgerEntry>>([]));
        var vm = new CustomerViewModel(new CustomerRepositoryFake { Customers = [CustomerRepositoryFake.Sample(1), CustomerRepositoryFake.Sample(2)] },
            new CustomerEditorFake(), CustomerTestContext.Session(), debt);
        vm.DebtTab.SelectedCustomer = vm.DebtTab.CustomerOptions[0];
        vm.DebtTab.SelectedCustomer = vm.DebtTab.CustomerOptions[1];
        first.SetResult([new(DateTime.Today, "OLD", "PAYMENT / CASH", 1, 0, 0, DebtLedgerSourceTypes.OrderPayment)]);
        await Task.Delay(100);
        Assert.Equal(2, vm.DebtTab.SelectedCustomer.Id);
        Assert.Empty(vm.DebtTab.PaymentHistory);
        Assert.False(vm.DebtTab.IsDetailLoading);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collect_payment_resolves_late_registered_dialog_and_refreshes_after_confirmation(bool confirm)
    {
        CustomerTestContext.Sta(() =>
        {
            var repo = new CustomerRepositoryFake { Customers = [CustomerRepositoryFake.Sample(1), CustomerRepositoryFake.Sample(2)] };
            DebtAllocationSettleRequest? saved = null;
            var debt = DebtRepositoryProxy.Create((method, args) => method.Name switch
            {
                "GetCustomerLedgerAsync" => Task.FromResult<IReadOnlyList<DebtLedgerEntry>>([]),
                "GetCustomerOpenReceivablesAsync" => Task.FromResult<IReadOnlyList<OpenCustomerReceivableLine>>([
                    new(1, "MB01", 1000000, DateTime.Today)]),
                "SettleDebtAllocationsAsync" => Settle((DebtAllocationSettleRequest)args![0]!),
                _ => throw new NotSupportedException(method.Name)
            });
            Task Settle(DebtAllocationSettleRequest request)
            {
                saved = request;
                repo.Snapshot = new(25470000, 25470000, 0, 0);
                return Task.CompletedTask;
            }
            var services = new ServiceRegistry();
            services.AddSingleton<ICustomerRepository>(repo);
            services.AddSingleton<ICustomerEditorService>(new CustomerEditorFake());
            services.AddSingleton<Kazuko.Core.Authentication.ICurrentSessionAccessor>(CustomerTestContext.Session());
            services.AddSingleton(debt);
            var nav = new ModuleNavigationRegistry();
            new CustomersModule().RegisterNavigation(nav, services);
            var dialog = new SettlementDialogFake { Confirm = confirm };
            services.AddSingleton<IDebtSettlementDialogService>(dialog);
            var view = (UserControl)Assert.Single(nav.Items).CreateView();
            var vm = Assert.IsType<CustomerViewModel>(view.DataContext);
            Assert.False(vm.CollectPaymentCommand.CanExecute(null));
            vm.DebtTab.SelectedCustomer = vm.DebtTab.CustomerOptions[1];
            Assert.True(vm.CollectPaymentCommand.CanExecute(null));
            vm.CollectPaymentCommand.Execute(null);
            Assert.NotNull(dialog.Request);
            Assert.Equal(1, vm.SelectedCustomer!.Id);
            Assert.Equal(2, vm.DebtTab.SelectedCustomer!.Id);
            if (confirm)
            {
                Assert.NotNull(saved);
                Assert.Equal(1000000, saved.CashAmountVnd);
                Assert.False(vm.DebtTab.HasCurrentDebt);
                Assert.False(vm.CollectPaymentCommand.CanExecute(null));
            }
            else
            {
                Assert.Null(saved);
                Assert.True(vm.DebtTab.HasCurrentDebt);
                Assert.True(vm.CollectPaymentCommand.CanExecute(null));
            }
        });
    }
    [Fact]
    public async Task Activity_tabs_keep_their_own_customer_when_list_selection_or_filters_change()
    {
        var repo = new CustomerRepositoryFake { Customers = [CustomerRepositoryFake.Sample(1), CustomerRepositoryFake.Sample(2)] };
        var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
        Assert.Null(vm.HistoryTab.SelectedCustomer);
        Assert.Null(vm.DebtTab.SelectedCustomer);
        vm.HistoryTab.SelectedCustomer = vm.HistoryTab.CustomerOptions[1];
        vm.DebtTab.SelectedCustomer = vm.DebtTab.CustomerOptions[0];
        vm.SelectedCustomer = vm.Customers[1];
        vm.SearchCommand.Execute(null);
        Assert.Equal(2, vm.HistoryTab.SelectedCustomer.Id);
        Assert.Equal(1, vm.DebtTab.SelectedCustomer.Id);
        Assert.Equal(2, vm.SelectedCustomer!.Id);
        vm.HistoryTab.FromDate = new DateTime(2025, 1, 1);
        vm.HistoryTab.ToDate = new DateTime(2025, 1, 31);
        await vm.HistoryTab.LoadAsync();
        Assert.Equal((2L, vm.HistoryTab.FromDate, vm.HistoryTab.ToDate), repo.HistoryQueries[^1]);
        var calls = repo.HistoryQueries.Count;
        vm.HistoryTab.FromDate = new DateTime(2025, 2, 1);
        await vm.HistoryTab.LoadAsync();
        Assert.Equal(calls, repo.HistoryQueries.Count);
        Assert.NotEmpty(vm.HistoryTab.ErrorMessage);
        Assert.Empty(vm.HistoryTab.FullPurchaseHistory);
        Assert.Null(vm.DebtTab.FromDate);
    }

    [Fact]
    public async Task Debt_dates_filter_payments_but_keep_current_debt_totals()
    {
        var day = new DateTime(2025, 1, 1);
        var debt = DebtRepositoryProxy.Create((_, _) => Task.FromResult<IReadOnlyList<DebtLedgerEntry>>([
            new(day.AddDays(-1), "OLD", "PAYMENT / CASH", 10, 0, 0, DebtLedgerSourceTypes.OrderPayment),
            new(day.AddHours(23), "MATCH", "PAYMENT / CASH", 20, 0, 0, DebtLedgerSourceTypes.OrderPayment),
            new(day.AddDays(1), "NEW", "PAYMENT / CASH", 30, 0, 0, DebtLedgerSourceTypes.OrderPayment)]));
        var vm = new CustomerViewModel(new CustomerRepositoryFake(), new CustomerEditorFake(), CustomerTestContext.Session(), debt);
        vm.DebtTab.FromDate = day; vm.DebtTab.ToDate = day;
        vm.DebtTab.SelectedCustomer = vm.DebtTab.CustomerOptions[0];
        await vm.DebtTab.LoadAsync();
        Assert.Equal("MATCH", Assert.Single(vm.DebtTab.PaymentHistory).DocumentCode);
        Assert.True(vm.DebtTab.HasCurrentDebt);
        Assert.Equal("1.000.000", vm.DebtTab.DebtCurrentText);
        Assert.Empty(vm.HistoryTab.FullPurchaseHistory);
    }

    [Fact]
    public void Preview_shortcuts_explicitly_seed_only_the_target_tab_filter()
    {
        var vm = new CustomerViewModel(new CustomerRepositoryFake(), new CustomerEditorFake(), CustomerTestContext.Session());
        vm.ViewAllPurchasesCommand.Execute(null);
        Assert.Equal(vm.SelectedCustomer!.Id, vm.HistoryTab.SelectedCustomer!.Id);
        Assert.Null(vm.DebtTab.SelectedCustomer);
        vm.ViewDebtDetailsCommand.Execute(null);
        Assert.Equal(vm.SelectedCustomer.Id, vm.DebtTab.SelectedCustomer!.Id);
        Assert.Equal(2, vm.SelectedTabIndex);
    }

}
