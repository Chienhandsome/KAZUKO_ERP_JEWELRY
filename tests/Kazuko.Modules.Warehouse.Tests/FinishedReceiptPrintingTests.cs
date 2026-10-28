using System.Drawing;
using FastReport;
using FastReport.Utils;
using Kazuko.Core.Production;
using Kazuko.Modules.Warehouse;
using Xunit;

namespace Kazuko.Modules.Warehouse.Tests;

public sealed class FinishedReceiptPrintingTests
{
    [Fact]
    public void Receipt_report_paginates_actual_saved_items_and_renders_nonblank()
    {
        var items=Enumerable.Range(1,60).Select(i=>new ManualFinishedProductItem("N-"+i,"N-"+i,"Nhẫn "+i,null,"750","750","Chỉ",1,.1m,.9m,1000,2000,100,100,100,null,"M1",null,null,1)).ToArray();
        var request=new PostManualFinishedReceiptRequest(DateTime.Today,1,1,items,"Đã ghi sổ",Guid.NewGuid(),[]);
        using var report=FinishedReceiptPrintService.Create(new(1,"NTP-TEST",60,request),"LSX-TEST","Kho thành phẩm");
        Assert.True(report.PreparedPages.Count>=2);
        using var first=report.PreparedPages.GetPage(0);
        var texts=first.AllObjects.OfType<TextObject>().Select(t=>t.Text).ToArray();
        Assert.Contains(texts,t=>t.Contains("NTP-TEST"));Assert.Contains(texts,t=>t.Contains("N-1"));
        using var bitmap=new Bitmap(794,1123);using var graphics=Graphics.FromImage(bitmap);graphics.Clear(Color.White);
        using var cache=new GraphicCache();var constructor=typeof(FRPaintEventArgs).GetConstructor([typeof(Graphics),typeof(float),typeof(float),typeof(GraphicCache)])!;
        first.Draw((FRPaintEventArgs)constructor.Invoke([graphics,1f,1f,cache]));
        var dark=0;for(var y=0;y<bitmap.Height;y+=2)for(var x=0;x<bitmap.Width;x+=2)if(bitmap.GetPixel(x,y).GetBrightness()<.7f)dark++;
        Assert.True(dark>500,"Receipt must contain visible content.");
    }
}
