using System.Drawing;
using System.Globalization;
using FastReport;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports.Shared;

namespace Kazuko.Modules.Reports;

public static class PayrollReportColumns
{
    public const string SettingsKey = "reports.hr.payroll.columns.v1";
    public static IReadOnlyList<ReportColumnDefinition> All { get; } =
    [
        new("Index","STT",.035f), new("Code","Mã NV",.075f), new("Name","Nhân viên",.15f),
        new("ActualUnits","Công tính lương",.06f), new("Salary","Lương tính được",.10f),
        new("TotalBonus","Tổng thưởng",.09f), new("TotalDeduction","Tổng khấu trừ",.09f),
        new("NetSalary","Thực nhận",.11f), new("PaidAmount","Đã chi",.10f), new("RemainingAmount","Còn phải chi",.10f),
        new("PaymentStatus","Thanh toán",.085f), new("VoucherCode","Phiếu chi",.09f,false),
        new("StandardUnits","Công chuẩn",.06f,false), new("Rate","Đơn giá ca/tháng",.10f,false), new("MethodLabel","Cách tính",.12f,false)
    ];
}

public sealed class PayrollReportPrintService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    private static float Mm(float value) => FastReportTemplateHelper.Mm(value);
    private static string Money(decimal value) => value.ToString("N0", Vi);
    private static string Units(decimal value) => value.ToString("0.##", Vi);
    private static string Status(PayrollReportResult result) => result.IsDraft ? "BẢN NHÁP - CHƯA CHỐT" : $"ĐÃ CHỐT - phiên bản {result.Revision}";
    private static TextObject Text(float x,float y,float w,float h,string text,float size=9,FontStyle style=FontStyle.Regular,HorzAlign align=HorzAlign.Left,bool expression=false)
    {
        var item=FastReportTemplateHelper.CreateText(Mm(x),Mm(y),Mm(w),Mm(h),text,size,style,align);
        item.AllowExpressions=expression;
        return item;
    }
    public Report CreateSummary(PayrollReportResult result, AuthenticatedSession session, IReadOnlyList<ReportColumnDefinition>? activeColumns = null)
    {
        if(result.Rows.Count==0)throw new InvalidOperationException("Không có dòng lương để in.");
        var columns=activeColumns is {Count:>0}?activeColumns:PayrollReportColumns.All.Where(x=>x.DefaultVisible).ToArray();
        var report=new Report { DoublePass=true };
        var page=new ReportPage { Name="PayrollSummary", Landscape=true,PaperWidth=297,PaperHeight=210,LeftMargin=10,RightMargin=10,TopMargin=10,BottomMargin=10 };
        report.Pages.Add(page);
        var title=new ReportTitleBand { Name="ReportTitle1", Height=Mm(34) };
        title.Objects.Add(Text(0,0,277,10,result.CompanyName,11,FontStyle.Bold,HorzAlign.Center));
        title.Objects.Add(Text(0,11,277,9,$"BẢNG LƯƠNG THÁNG {result.Filter.Month:00}/{result.Filter.Year}",15,FontStyle.Bold,HorzAlign.Center));
        title.Objects.Add(Text(0,21,277,6,"Phạm vi: toàn doanh nghiệp · " + (result.Filter.EmployeeId is null ? "Tất cả nhân viên trong kỳ" : $"Nhân viên: {result.Rows[0].Code} · {result.Rows[0].Name}"),9,FontStyle.Regular,HorzAlign.Center));
        title.Objects.Add(Text(0,27,277,5,$"Đơn vị: VND · Nguồn: bảng lương đã lưu · In: {DateTime.Now:dd/MM/yyyy HH:mm} · Người lập: {session.DisplayName}",8,FontStyle.Regular,HorzAlign.Center));
        var header=new PageHeaderBand { Name="PageHeader1",Height=Mm(19) };
        var status=Text(0,0,277,6,Status(result),9,FontStyle.Bold,HorzAlign.Center);
        if(result.IsDraft)status.TextColor=Color.Firebrick;
        header.Objects.Add(status);
        var data=new DataBand { Name="Data1",Height=Mm(9),CanGrow=true,CanBreak=false };
        var summary=new ReportSummaryBand { Name="ReportSummary1",Height=Mm(38),CanBreak=false };
        float left=0;var weight=columns.Sum(x=>x.WidthWeight);
        foreach(var col in columns)
        {
            var width=277*col.WidthWeight/weight;
            var head=Text(left,6,width,13,col.Header,8,FontStyle.Bold,HorzAlign.Center);head.Border.Lines=BorderLines.All;head.Fill=new SolidFill(Color.WhiteSmoke);header.Objects.Add(head);
            var isText=col.Key is "Name" or "Code" or "PaymentStatus" or "VoucherCode" or "MethodLabel";
            var cell=Text(left,0,width,9,$"[PayrollRows.{col.Key}]",8,FontStyle.Regular,isText?HorzAlign.Left:HorzAlign.Right,true);
            cell.CanGrow=true;cell.GrowToBottom=true;cell.Border.Lines=BorderLines.All;data.Objects.Add(cell);
            var total=Total(result.Rows,col.Key);
            var footer=Text(left,0,width,11,col.Key=="Name"?"TỔNG CỘNG":total,8,FontStyle.Bold,isText?HorzAlign.Left:HorzAlign.Right);
            footer.Border.Lines=BorderLines.All;footer.Fill=new SolidFill(Color.WhiteSmoke);summary.Objects.Add(footer);
            left+=width;
        }
        summary.Objects.Add(Text(0,13,277,5,$"{result.Rows.Count} nhân viên · Thực nhận: {Money(result.Rows.Sum(x=>x.NetSalary))} · Đã chi: {Money(result.Rows.Sum(x=>x.PaidAmount))} · Còn phải chi: {Money(result.Rows.Sum(x=>x.RemainingAmount))}",9,FontStyle.Bold));
        summary.Objects.Add(Text(0,21,92,6,"Người lập bảng",10,FontStyle.Bold,HorzAlign.Center));
        summary.Objects.Add(Text(92,21,92,6,"Kế toán",10,FontStyle.Bold,HorzAlign.Center));
        summary.Objects.Add(Text(184,21,93,6,"Người phê duyệt",10,FontStyle.Bold,HorzAlign.Center));
        foreach(var (x,w) in new[]{(0f,92f),(92f,92f),(184f,93f)})
            summary.Objects.Add(Text(x,28,w,6,"(Ký, ghi rõ họ tên)",8,FontStyle.Italic,HorzAlign.Center));
        FastReportTemplateHelper.AssignPageBands(page,title,header,data,summary);
        page.PageFooter=new PageFooterBand { Name="PageFooter1",Height=Mm(6) };
        page.PageFooter.Objects.Add(Text(0,0,277,6,"Trang [Page#]/[TotalPages#]",8,FontStyle.Regular,HorzAlign.Right,true));
        var rows=result.Rows.Select((x,i)=>new PayrollPrintRow(x,i+1)).ToList();
        report.RegisterData(rows,"PayrollRows"); FastReportTemplateHelper.BindDataSource(report,"PayrollRows");
        report.Prepare();return report;
    }
    private static string Total(IReadOnlyList<PayrollReportRow> rows,string key) => key switch
    {
        "Salary"=>Money(rows.Sum(x=>x.Salary)), "TotalBonus"=>Money(rows.Sum(x=>x.TotalBonus)),"TotalDeduction"=>Money(rows.Sum(x=>x.TotalDeduction)),
        "NetSalary"=>Money(rows.Sum(x=>x.NetSalary)),"PaidAmount"=>Money(rows.Sum(x=>x.PaidAmount)),"RemainingAmount"=>Money(rows.Sum(x=>x.RemainingAmount)),_=>""
    };
    public Report CreateIndividual(PayrollReportResult result, AuthenticatedSession session)
    {
        if(result.Filter.EmployeeId is null || result.Rows.Count!=1)throw new InvalidOperationException("Chọn đúng một nhân viên để in phiếu lương.");
        var row=result.Rows[0];
        var report=new Report {DoublePass=true};
        var page=new ReportPage {Name="PayrollSlip",PaperWidth=210,PaperHeight=297,LeftMargin=15,RightMargin=15,TopMargin=12,BottomMargin=12};report.Pages.Add(page);
        var title=new ReportTitleBand {Name="ReportTitle1",Height=Mm(72)};
        title.Objects.Add(Text(0,0,180,12,result.CompanyName,11,FontStyle.Bold,HorzAlign.Center));
        title.Objects.Add(Text(0,13,180,10,$"PHIẾU LƯƠNG THÁNG {result.Filter.Month:00}/{result.Filter.Year}",15,FontStyle.Bold,HorzAlign.Center));
        var status=Text(0,24,180,7,Status(result),10,FontStyle.Bold,HorzAlign.Center);if(result.IsDraft)status.TextColor=Color.Firebrick;title.Objects.Add(status);
        var employeeName=Text(0,33,180,12,$"Nhân viên: {row.Name}  ·  Mã: {row.Code}",11,FontStyle.Bold);employeeName.CanGrow=true;title.Objects.Add(employeeName);
        title.Objects.Add(Text(0,46,180,7,$"Cách tính: {row.MethodLabel}",10));
        title.Objects.Add(Text(0,54,180,7,$"Công chuẩn: {Units(row.StandardUnits)}  ·  Công tính lương: {Units(row.ActualUnits)}  ·  Đơn giá ca/tháng: {Money(row.Rate)} VND",9));
        title.Objects.Add(Text(0,63,180,7,"Số liệu theo bảng lương đã lưu; các khoản đã thu/chi riêng không cộng lại vào phiếu này.",8,FontStyle.Italic));
        var header=new PageHeaderBand{Name="PageHeader1",Height=Mm(9)};
        var data=new DataBand{Name="Data1",Height=Mm(12),CanGrow=true,CanBreak=false};
        foreach(var (x,w,label,field,align) in new[]{(0f,55f,"Khoản mục","Label",HorzAlign.Left),(55f,32f,"Số tiền (VND)","AmountText",HorzAlign.Right),(87f,93f,"Diễn giải","Detail",HorzAlign.Left)})
        {
            var head=Text(x,0,w,9,label,9,FontStyle.Bold,HorzAlign.Center);head.Border.Lines=BorderLines.All;head.Fill=new SolidFill(Color.WhiteSmoke);header.Objects.Add(head);
            var item=Text(x,0,w,12,$"[SlipRows.{field}]",9,FontStyle.Regular,align,true);item.Border.Lines=BorderLines.All;item.CanGrow=true;item.GrowToBottom=true;data.Objects.Add(item);
        }
        var summary=new ReportSummaryBand{Name="ReportSummary1",Height=Mm(85),CanBreak=false};
        summary.Objects.Add(Text(0,3,180,10,$"THỰC NHẬN: {Money(row.NetSalary)} VND",14,FontStyle.Bold,HorzAlign.Right));
        summary.Objects.Add(Text(0,15,180,8,$"Đã chi: {Money(row.PaidAmount)} VND   ·   Còn phải chi: {Money(row.RemainingAmount)} VND",10,FontStyle.Bold,HorzAlign.Right));
        summary.Objects.Add(Text(0,25,180,8,$"Thanh toán: {row.PaymentStatus} · Phiếu: {row.VoucherCode??"—"} · Ngày chi: {(row.PaymentDate is DateTime date?date.ToString("dd/MM/yyyy",Vi):"—")}",9));
        summary.Objects.Add(Text(0,34,180,7,$"Phương thức: {PaymentMethod(row.PaymentMethod)}",9));
        summary.Objects.Add(Text(0,45,90,7,"Người lập phiếu",10,FontStyle.Bold,HorzAlign.Center));
        summary.Objects.Add(Text(90,45,90,7,"Nhân viên nhận lương",10,FontStyle.Bold,HorzAlign.Center));
        summary.Objects.Add(Text(0,53,90,6,"(Ký, ghi rõ họ tên)",9,FontStyle.Italic,HorzAlign.Center));
        summary.Objects.Add(Text(90,53,90,6,"(Ký, ghi rõ họ tên)",9,FontStyle.Italic,HorzAlign.Center));
        summary.Objects.Add(Text(0,74,180,6,$"In: {DateTime.Now:dd/MM/yyyy HH:mm} · Người lập: {session.DisplayName}",8,FontStyle.Italic));
        FastReportTemplateHelper.AssignPageBands(page,title,header,data,summary);
        page.PageFooter=new PageFooterBand{Name="PageFooter1",Height=Mm(6)};
        page.PageFooter.Objects.Add(Text(0,0,180,6,Status(result)+" · Trang [Page#]/[TotalPages#]",8,FontStyle.Italic,HorzAlign.Right,true));
        var detailRows=new List<PayrollSlipPrintRow>
        {
            new("Lương theo công",Money(row.Salary),$"{Units(row.ActualUnits)} công · {row.MethodLabel}"),
            new("Thưởng đã ghi nhận","+"+Money(row.RecordedBonus),"Theo các khoản thưởng gộp vào kỳ lương."),
            new("Thưởng điều chỉnh thêm","+"+Money(row.ManualBonus),row.ManualBonusNote),
            new("Khấu trừ đã ghi nhận","-"+Money(row.RecordedDeduction),"Theo các khoản khấu trừ gộp vào kỳ lương."),
            new("Khấu trừ điều chỉnh thêm","-"+Money(row.ManualDeduction),row.ManualDeductionNote)
        };
        if(!string.IsNullOrWhiteSpace(row.RecordedNotes))
            detailRows.Add(new("Diễn giải ghi nhận","",row.RecordedNotes));
        report.RegisterData(detailRows,"SlipRows");FastReportTemplateHelper.BindDataSource(report,"SlipRows");report.Prepare();return report;
    }
    private static string PaymentMethod(string? method)=>method switch {"CASH"=>"Tiền mặt","BANK_TRANSFER"=>"Chuyển khoản","CARD"=>"Thẻ",_=>"—"};
    public sealed class PayrollPrintRow(PayrollReportRow row,int index)
    {
        public int Index=>index;public string Code=>row.Code;public string Name=>row.Name;public string MethodLabel=>row.MethodLabel;
        public string StandardUnits=>Units(row.StandardUnits);public string ActualUnits=>Units(row.ActualUnits);public string Rate=>Money(row.Rate);public string Salary=>Money(row.Salary);
        public string TotalBonus=>Money(row.TotalBonus);public string TotalDeduction=>Money(row.TotalDeduction);public string NetSalary=>Money(row.NetSalary);
        public string PaidAmount=>Money(row.PaidAmount);public string RemainingAmount=>Money(row.RemainingAmount);public string PaymentStatus=>row.PaymentStatus;public string VoucherCode=>row.VoucherCode??"—";
    }
    public sealed record PayrollSlipPrintRow(string Label,string AmountText,string Detail);
}
