using Kazuko.Core.Customers;
using Xunit;

namespace Kazuko.Modules.Customers.Tests;

public sealed class WalkInCustomerTests
{
    [Fact]
    public void System_customer_filters_and_commands_are_separate_from_named_customers()
    {
        CustomerTestContext.Sta(() =>
        {
            var named = CustomerRepositoryFake.Sample();
            var system = named with { Id = 2, Code = "ARBITRARY", IsWalkIn = true };
            var repo = new CustomerRepositoryFake { Customers = [named, system] };
            var vm = new CustomerViewModel(repo, new CustomerEditorFake(), CustomerTestContext.Session());
            Assert.False(Assert.Single(vm.Customers).IsWalkIn);
            vm.SelectedKind = "Vãng lai";
            vm.SearchCommand.Execute(null);
            var row = Assert.Single(vm.Customers);
            Assert.True(row.IsWalkIn);
            Assert.Contains("Hệ thống", row.KindText);
            Assert.False(vm.EditCommand.CanExecute(row));
            Assert.False(vm.ToggleActiveCommand.CanExecute(row));
            Assert.False(vm.DeleteCommand.CanExecute(row));
            Assert.False(vm.EditSelectedCommand.CanExecute(null));
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null));
            Assert.True(vm.ViewAllPurchasesCommand.CanExecute(null));
            vm.SelectedKind = "Tất cả";
            vm.SearchCommand.Execute(null);
            Assert.Equal(2, vm.Customers.Count);
            vm.SelectedCustomer = vm.Customers.Single(x => !x.IsWalkIn);
            Assert.True(vm.EditSelectedCommand.CanExecute(null));
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
            vm.SelectedTabIndex = 3;
            Assert.Single(vm.InactiveCustomers);
            Assert.Equal(1, Assert.Single(vm.GroupDistribution).CustomerCount);
            Assert.Equal(2, vm.SalesSummary.Count);
            Assert.Equal(25570000m, vm.SalesSummary.Sum(x => x.TotalSales));
            Assert.True(vm.ExportSalesSummaryCommand.CanExecute(null));
        });
    }
}
