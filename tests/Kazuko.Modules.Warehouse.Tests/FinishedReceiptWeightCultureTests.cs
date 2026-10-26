using System.Globalization;
using System.Reflection;
using Kazuko.Core.Company;
using Kazuko.Core.Printing;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;
using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class FinishedReceiptWeightCultureTests
{
    [Theory]
    [InlineData("en-US","0.5","0.023")]
    [InlineData("vi-VN","0.5","0.023")]
    [InlineData("de-DE","0.5","0.023")]
    [InlineData("fr-FR","0.5","0.023")]
    [InlineData("en-US","0,5","0,023")]
    [InlineData("vi-VN","0,5","0,023")]
    [InlineData("de-DE","0,5","0,023")]
    [InlineData("fr-FR","0,5","0,023")]
    public void Recalculation_and_product_values_are_independent_of_windows_culture(string culture,string total,string stone)
    {
        using var scope=new CultureScope(culture);
        var vm=Create();vm.ProductName="Nhẫn thử";vm.TotalWeightText=total;vm.StoneWeightText=stone;
        Assert.Equal("0,477",vm.GoldWeightText);
        vm.AddCommand.Execute(null);
        var item=Assert.Single(vm.Products);
        Assert.Equal(.5m,item.TotalWeight);Assert.Equal(.023m,item.StoneWeight);Assert.Equal(.477m,item.GoldWeight);
        Assert.Equal("",vm.ErrorMessage);
    }

    [Theory]
    [InlineData("en-US")][InlineData("vi-VN")][InlineData("de-DE")]
    public void Editing_preserves_four_decimal_places_and_loss_uses_the_same_rule(string culture)
    {
        using var scope=new CultureScope(culture);
        var vm=Create();vm.ProductName="Nhẫn thử";vm.TotalWeightText="0.5001";vm.StoneWeightText="0.125";
        Assert.Equal("0,3751",vm.GoldWeightText);vm.AddCommand.Execute(null);
        vm.SelectedProduct=Assert.Single(vm.Products);vm.EditCommand.Execute(null);
        Assert.Equal("0,5001",vm.TotalWeightText);Assert.Equal("0,3751",vm.GoldWeightText);
        var reconcile=new GoldReconcileRowViewModel("750"){IssuedWeight=1,ReceivedWeight=.5m,LossWeightText="0.023"};
        Assert.Equal(.023m,reconcile.LossWeight);Assert.Equal(.477m,reconcile.DebtWeight);
        reconcile.LossWeightText="0,023";Assert.Equal(.023m,reconcile.LossWeight);
    }

    [Theory]
    [InlineData("1.250,5")][InlineData("1,250.5")][InlineData("0..5")][InlineData("0,,5")]
    [InlineData("1 250")][InlineData("1e3")][InlineData("")][InlineData("invalid")]
    public void Ambiguous_or_invalid_input_is_not_accepted_as_a_different_number(string input)
    {
        Assert.False(WeightDecimalInput.TryParse(input,out _));
    }

    [Fact]
    public void Invalid_total_clears_auto_result_and_cannot_be_added_even_with_manual_gold()
    {
        var vm=Create();vm.ProductName="Nhẫn thử";vm.TotalWeightText="0.5";vm.StoneWeightText="0.023";
        vm.TotalWeightText="0..5";Assert.Equal("",vm.GoldWeightText);
        vm.GoldWeightText="0.477";vm.AddCommand.Execute(null);
        Assert.Empty(vm.Products);Assert.NotEmpty(vm.ErrorMessage);
    }

    private static ProductionFinishedReceiptDialogViewModel Create()=>new(Stub<IWarehouseRepository>(),Stub<IProductionQueryRepository>(),
        Stub<IProductionApplicationService>(),new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"),Stub<IPrintTemplateStore>(),Stub<ICompanyProfileService>());
    private static T Stub<T>() where T:class=>DispatchProxy.Create<T,UnusedService>();
    public class UnusedService:DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new NotSupportedException("No database access in weight tests.");
    }
    private sealed class CultureScope:IDisposable
    {
        private readonly CultureInfo _old=CultureInfo.CurrentCulture;
        public CultureScope(string culture)=>CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture);
        public void Dispose()=>CultureInfo.CurrentCulture=_old;
    }
}
