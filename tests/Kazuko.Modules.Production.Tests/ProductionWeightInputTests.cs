using System.Globalization;
using System.Reflection;
using Kazuko.Core.Production;
using Kazuko.Modules.Production.Dialogs;
using Xunit;

namespace Kazuko.Modules.Production.Tests;

public sealed class ProductionWeightInputTests
{
    [Theory]
    [InlineData("en-US")][InlineData("vi-VN")][InlineData("de-DE")][InlineData("fr-FR")]
    public void Quote_preview_and_saved_line_read_same_weight_independent_of_os(string name)
    {
        var old=CultureInfo.CurrentCulture;CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(name);
        try
        {
            foreach(var input in new[]{"0.023","0,023"})
            {
                var vm=new ProductionLineEditDialogViewModel(DispatchProxy.Create<IProductionAttachmentStore,UnusedStore>(),1,true,[])
                {ModelName="Nhẫn",PlannedQuantityText="1",EstimatedWeightChiText=input,GoldUnitPricePerChiText="1.000.000",LaborCostPerUnitText="0"};
                Assert.DoesNotContain("Nhập TL",vm.QuotePreviewText);
                var line=new ProductionLineEditorViewModel(1);vm.ApplyTo(line);Assert.Equal(.023m,line.EstimatedWeightChi);
                var reopened=new ProductionLineEditDialogViewModel(DispatchProxy.Create<IProductionAttachmentStore,UnusedStore>(),1,true,[],line);
                Assert.Equal("0,023",reopened.EstimatedWeightChiText);
            }
        }
        finally{CultureInfo.CurrentCulture=old;}
    }
    public class UnusedStore:DispatchProxy {protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new NotSupportedException();}
}
