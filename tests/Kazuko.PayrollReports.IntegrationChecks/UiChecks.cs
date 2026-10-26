using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FastReport;
using FastReport.Export.PdfSimple;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.Reports;

internal static class UiChecks
{
 public static void Run(AuthenticatedSession session,PayrollReportResult result,string output)
 {
  Exception? error=null;
  var thread=new Thread(()=>{try{Checks(session,result,output);}catch(Exception ex){error=ex;}});
  thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
  if(error is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
 }
 private static void Checks(AuthenticatedSession session,PayrollReportResult result,string output)
 {
  var printer=new PayrollReportPrintService();
  var single=result with {Rows=[result.Rows[0] with {Salary=8000000,RecordedBonus=500000,ManualBonus=0,ManualDeduction=0,RecordedDeduction=100000,PaidAmount=8400000,
    RecordedNotes="Thưởng: hoàn thành chỉ tiêu tháng 500.000 VND.\nKhấu trừ: điều chỉnh đã ghi nhận 100.000 VND.",Name="Nguyễn Thị Hồng Ngọc",Code="NV001"}]};
  Save(printer.CreateIndividual(single,session),Path.Combine(output,"phieu-luong.pdf"));
  var many=single with {Filter=single.Filter with {EmployeeId=null},Rows=Enumerable.Range(1,45).Select(i=>single.Rows[0] with {EmployeeId=i,Code=$"NV{i:000}",Name=$"Nguyễn Thị Hồng Ngọc - Nhân viên {i:00}",PaidAmount=i%2==0?8400000:0}).ToArray()};
  Save(printer.CreateSummary(many,session),Path.Combine(output,"bang-luong.pdf"));
  Save(printer.CreateSummary(many with {Status="DRAFT"},session),Path.Combine(output,"bang-luong-nhap.pdf"));
  try{using var ignored=printer.CreateIndividual(many,session);throw new Exception("Individual report accepted all employees");}catch(InvalidOperationException){}
  var accessor=new CurrentSessionAccessor();accessor.Set(session);var fake=new Fake(single);
  var auth=new TestAuth(new PermissionSnapshot(true,[]));
  using(var vm=new PayrollReportViewModel(fake,accessor,false,auth))
  {
   vm.ReloadAsync().GetAwaiter().GetResult();
   Check(vm.Rows.Count==1 && vm.HasPreviewPdf && vm.SavePdfCommand.CanExecute(null),"preview and export enabled after loading");
   auth.Clear();Check(vm.Rows.Count==0 && !vm.HasPreviewPdf && !vm.SavePdfCommand.CanExecute(null),"revoked payroll permission clears cached data/PDF");
  }
  using(var vm=new PayrollReportViewModel(fake,accessor,true))
  {
   vm.ReloadAsync().GetAwaiter().GetResult();Check(vm.Rows.Count==0 && !vm.SavePdfCommand.CanExecute(null),"individual requires employee selection");
   vm.SelectedEmployee=vm.Employees.Single(x=>x.Id>0);vm.ReloadAsync().GetAwaiter().GetResult();
   Check(vm.Rows.Count==1 && vm.HasPreviewPdf,"individual selected employee preview");
   vm.Month=vm.Month==12?1:vm.Month+1;
   Check(vm.Rows.Count==0 && !vm.HasPreviewPdf && !vm.SavePdfCommand.CanExecute(null),"changing period invalidates preview/export immediately");
  }
  var deferred=new Deferred();
  using(var vm=new PayrollReportViewModel(deferred,accessor))
  {
   var pending=vm.ReloadAsync();vm.Month=vm.Month==12?1:vm.Month+1;
   deferred.People.SetResult([]);pending.GetAwaiter().GetResult();
   Check(vm.Rows.Count==0 && !vm.HasPreviewPdf && deferred.ResultCalls==0,"late response from old filter is discarded before fetching rows");
  }
  foreach(var (theme,scale) in new[]{("Light",1d),("Dark",1.5)})
  {
   using var vm=new PayrollReportViewModel(fake,accessor);
   var window=new Window{Content=new PayrollReportView{DataContext=vm}};
   foreach(var resource in new[]{theme,"Typography","Controls"})window.Resources.MergedDictionaries.Insert(0,new ResourceDictionary{Source=new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml",UriKind.Relative)});
   foreach(var key in new[]{"FontSize.Caption","FontSize.Body","FontSize.Subtitle","FontSize.Heading"})window.Resources[key]=(double)window.FindResource(key)*scale;
   var content=(FrameworkElement)window.Content;
   content.Measure(new Size(1150,750));content.Arrange(new Rect(0,0,1150,750));
   Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);content.UpdateLayout();
   var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle((Brush)window.FindResource("AppBackgroundBrush"),null,new Rect(0,0,1150,750));dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,1150,750));}
   var target=new RenderTargetBitmap(1150,750,96,96,PixelFormats.Pbgra32);target.Render(visual);
   var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using(var stream=File.Create(Path.Combine(output,$"payroll-ui-{theme}.png")))encoder.Save(stream);
   window.Close();
  }
  Check(PayrollReportViewModel.ExcelColumns.Any(x=>x.Key=="NetSalary"),"typed Excel includes net salary");
  Console.WriteLine("PASS: A4 landscape multi-page, portrait payslip, draft PDF and Light/Dark UI generated.");
 }
 private static void Save(Report report,string path){using(report){using var export=new PDFSimpleExport();report.Export(export,path);}}
 private static void Check(bool ok,string label){if(!ok)throw new Exception("FAILED UI: "+label);Console.WriteLine("PASS UI: "+label);}
 private sealed class Fake(PayrollReportResult result):IPayrollReportService
 {
  public Task<IReadOnlyList<PayrollReportEmployee>> GetEmployeesAsync(int year,int month,bool includeDraft,AuthenticatedSession session,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<PayrollReportEmployee>>([new(result.Rows[0].EmployeeId,result.Rows[0].Code,result.Rows[0].Name)]);
  public Task<PayrollReportResult> GetAsync(PayrollReportFilter filter,AuthenticatedSession session,CancellationToken ct=default)=>Task.FromResult(result with {Filter=filter});
 }
 private sealed class Deferred:IPayrollReportService
 {
  public TaskCompletionSource<IReadOnlyList<PayrollReportEmployee>> People{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
  public int ResultCalls;
  public Task<IReadOnlyList<PayrollReportEmployee>> GetEmployeesAsync(int year,int month,bool includeDraft,AuthenticatedSession session,CancellationToken ct=default)=>People.Task;
  public Task<PayrollReportResult> GetAsync(PayrollReportFilter filter,AuthenticatedSession session,CancellationToken ct=default){ResultCalls++;throw new Exception("Old filter should not reach the rows query");}
 }
}
