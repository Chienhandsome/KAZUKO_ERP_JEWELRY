using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Windows.Controls;
using FastReport;
using FastReport.Utils;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Warehouse;

internal static class FinishedReceiptPrintService
{
    public static Report Create(ManualFinishedReceiptResult result,string orderCode,string location)
    {
        var request=result.PostedRequest??throw new InvalidOperationException("Chỉ in phiếu đã ghi sổ.");
        var report=new Report();var page=new ReportPage{PaperWidth=210,PaperHeight=297,LeftMargin=10,RightMargin=10,TopMargin=10,BottomMargin=10};report.Pages.Add(page);
        var title=new ReportTitleBand{Height=Units.Millimeters*36};page.ReportTitle=title;
        Text(title,"PHIẾU NHẬP KHO THÀNH PHẨM",0,0,190,9,16,true);
        Text(title,$"Số phiếu: {result.DocumentCode}   •   Ngày: {request.ReceiptDate:dd/MM/yyyy}",0,11,190,7);
        Text(title,$"LSX: {orderCode}   •   Kho nhận: {location}",0,19,190,7);
        var data=new System.Data.DataTable("Items");foreach(var column in new[]{"Code","Name","Gold","Weight","Cost"})data.Columns.Add(column);
        foreach(var item in request.Items)data.Rows.Add(item.ProductCode,item.ProductName,item.GoldPurityCode,item.GoldWeight.ToString("0.####",CultureInfo.GetCultureInfo("vi-VN")),item.CostPrice?.ToString("N0",CultureInfo.GetCultureInfo("vi-VN")));
        report.RegisterData(data,"Items");report.GetDataSource("Items").Enabled=true;
        var header=new DataHeaderBand{Height=Units.Millimeters*9,RepeatOnEveryPage=true};
        var band=new DataBand{DataSource=report.GetDataSource("Items"),Height=Units.Millimeters*10};page.Bands.Add(band);band.Header=header;
        var widths=new[]{32f,65f,25f,30f,38f};var labels=new[]{"Mã món","Sản phẩm","Loại vàng","TL vàng (chỉ)","Giá vốn (VND)"};var fields=new[]{"Code","Name","Gold","Weight","Cost"};var x=0f;
        for(var i=0;i<widths.Length;i++){Text(header,labels[i],x,0,widths[i],9,10,true);Text(band,"[Items."+fields[i]+"]",x,0,widths[i],10);x+=widths[i];}
        var footer=new ReportSummaryBand{Height=Units.Millimeters*45};page.ReportSummary=footer;
        Text(footer,$"Tổng: {request.Items.Count} món • Giá vốn: {request.Items.Sum(i=>i.CostPrice??0):N0} VND",0,3,190,8,11,true);
        Text(footer,"Ghi chú: "+request.Notes,0,12,190,10);
        Text(footer,"Người giao                         Người nhận                         Người lập phiếu",0,28,190,8);
        report.Prepare();return report;
    }
    private static void Text(BandBase band,string content,float x,float y,float width,float height,float font=10,bool bold=false)
    {
        var text=new TextObject{Text=content,Left=x*Units.Millimeters,Top=y*Units.Millimeters,Width=width*Units.Millimeters,Height=height*Units.Millimeters,
            Font=new Font("Arial",font,bold?FontStyle.Bold:FontStyle.Regular),VertAlign=VertAlign.Center,WordWrap=true};band.Objects.Add(text);
    }
    public static void Print(ManualFinishedReceiptResult result,string orderCode,string location)
    {
        var picker=new PrintDialog();if(picker.ShowDialog()!=true)return;
        using var report=Create(result,orderCode,location);using var document=new PrintDocument();
        document.PrinterSettings.PrinterName=picker.PrintQueue.FullName;document.DocumentName=result.DocumentCode;
        var a4=document.PrinterSettings.PaperSizes.Cast<PaperSize>().FirstOrDefault(p=>Math.Abs(p.Width-827)<=10&&Math.Abs(p.Height-1169)<=10)
            ??throw new InvalidOperationException("Máy in đã chọn không có khổ A4. Chọn máy in A4 để in phiếu nhập.");
        document.DefaultPageSettings.PaperSize=a4;document.DefaultPageSettings.Landscape=false;document.DefaultPageSettings.Margins=new Margins(0,0,0,0);
        document.PrinterSettings.Copies=(short)Math.Clamp(picker.PrintTicket.CopyCount??1,1,short.MaxValue);
        var index=0;
        document.PrintPage+=(_,e)=>
        {
            using var prepared=report.PreparedPages.GetPage(index);
            var g=e.Graphics!;g.PageUnit=GraphicsUnit.Pixel;
            g.TranslateTransform(-e.PageSettings.HardMarginX*g.DpiX/100f,-e.PageSettings.HardMarginY*g.DpiY/100f);
            using var cache=new GraphicCache();
            var constructor=typeof(FRPaintEventArgs).GetConstructor([typeof(Graphics),typeof(float),typeof(float),typeof(GraphicCache)])!;
            prepared.Draw((FRPaintEventArgs)constructor.Invoke([g,g.DpiX/96f,g.DpiY/96f,cache]));
            e.HasMorePages=++index<report.PreparedPages.Count;
        };
        document.Print();
    }
}
