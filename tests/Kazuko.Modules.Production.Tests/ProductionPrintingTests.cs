using System.Printing;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Kazuko.Core.Production;
using Xunit;

namespace Kazuko.Modules.Production.Tests;

public sealed class ProductionPrintingTests
{
    [Fact]
    public void Label_media_is_rejected_and_a4_is_accepted()
    {
        Assert.False(ProductionPrintService.IsA4(new PageMediaSize(325.039,45.354)));
        Assert.True(ProductionPrintService.IsA4(new PageMediaSize(PageMediaSizeName.ISOA4)));
        Assert.True(ProductionPrintService.IsA4(new PageMediaSize(ProductionPrintService.A4Width,ProductionPrintService.A4Height)));
    }

    [Fact]
    public void Order_and_material_issue_render_ink_on_every_a4_page()=>Sta(()=>
    {
        foreach(var rows in new[]{0,1,120})
        {
            var dto=new ProductionOrderPrintDto("LSX-TEST",new DateTime(2026,10,26),null,null,"Đang làm","Chi nhánh kiểm tra",
                "Khách hàng thử","DH-TEST","Lê Thị Hương","Ghi chú sản xuất",
                Enumerable.Range(1,rows).Select(i=>new ProductionOrderPrintLine(i,"Mẫu nhẫn số "+i,1,.5m,"750",100000)).ToArray());
            var doc=ProductionPrintService.CreateOrderDocument(dto);
            Assert.Contains("LSX-TEST",new TextRange(doc.ContentStart,doc.ContentEnd).Text);
            Assert.Contains("Lê Thị Hương",new TextRange(doc.ContentStart,doc.ContentEnd).Text);
            CheckPages(doc,$"order-{rows}");
            var issue=ProductionPrintService.CreateMaterialIssueDocument(new("LSX-TEST",DateTime.Today,"Chi nhánh","Người giao","Người nhận","GX-TEST",
                Enumerable.Range(1,rows).Select(i=>new ProductionMaterialIssuePrintLine(i,"NVL-"+i,"Vàng nguyên liệu",.5m,"CHI",1000000,"Ghi chú")).ToArray()));
            CheckPages(issue,$"issue-{rows}");
        }
    });

    [Fact]
    public void Tiny_label_geometry_is_blocked_before_submitting_any_job()=>Sta(()=>
    {
        var doc=new FlowDocument(new Paragraph(new Run("Phiếu sản xuất")));
        Assert.Throws<InvalidOperationException>(()=>ProductionPrintService.PreparePaginator(doc,new Size(325.039,45.354),new Rect(0,0,325.039,45.354)));
        Assert.Throws<InvalidOperationException>(()=>ProductionPrintService.PreparePaginator(doc,new Size(ProductionPrintService.A4Width,ProductionPrintService.A4Height),new Rect(0,0,325.039,45.354)));
    });

    private static void CheckPages(FlowDocument doc,string name)
    {
        Assert.Equal(Colors.Black,((SolidColorBrush)doc.Foreground).Color);
        Assert.Equal(Colors.White,((SolidColorBrush)doc.Background).Color);
        var paginator=ProductionPrintService.PreparePaginator(doc,new Size(ProductionPrintService.A4Width,ProductionPrintService.A4Height),
            new Rect(24,24,ProductionPrintService.A4Width-48,ProductionPrintService.A4Height-48));
        Assert.True(paginator.PageCount>0);
        if(name.EndsWith("120",StringComparison.Ordinal))Assert.True(paginator.PageCount>1);
        for(var i=0;i<paginator.PageCount;i++)
        {
            using var page=paginator.GetPage(i);
            Assert.NotSame(DocumentPage.Missing,page);
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(page.Size.Width),(int)Math.Ceiling(page.Size.Height),96,96,PixelFormats.Pbgra32);
            bitmap.Render(page.Visual);
            var pixels=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];bitmap.CopyPixels(pixels,bitmap.PixelWidth*4,0);
            var ink=0;for(var p=0;p<pixels.Length;p+=4)if(pixels[p+3]>128&&pixels[p]<160&&pixels[p+1]<160&&pixels[p+2]<160)ink++;
            Assert.True(ink>100,$"{name} page {i+1} contains no printed ink");
            var capture=Environment.GetEnvironmentVariable("KAZUKO_PRODUCTION_PRINT_CAPTURE");
            if(!string.IsNullOrWhiteSpace(capture))
            {
                System.IO.Directory.CreateDirectory(capture);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output=System.IO.File.Create(System.IO.Path.Combine(capture,$"{name}-{i+1}.png"));encoder.Save(output);
            }
        }
    }
    private static void Sta(Action action)
    {
        Exception? error=null;var thread=new Thread(()=>{try{action();}catch(Exception ex){error=ex;}});
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(error is not null)ExceptionDispatchInfo.Capture(error).Throw();
    }
}
