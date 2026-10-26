using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Modules.HumanResources;

internal static class UiChecks
{
    public static Task RunAsync(AuthenticatedSession session, PayrollPeriod period)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { Run(session, period); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return Task.CompletedTask;
    }
    private static void Run(AuthenticatedSession session, PayrollPeriod period)
    {
        var accessor=new CurrentSessionAccessor(); accessor.Set(session); var money=new Money(); var funds=new Funds(); var banks=new Banks(); var categories=new Categories();
        EmployeeMoneyDialogViewModel Award() => new(money,accessor,funds,banks,categories);
        var vm=Award(); vm.InitializeAsync().GetAwaiter().GetResult(); vm.SelectedEmployee=vm.Employees[0]; vm.AmountText="500.000"; vm.Description="Thưởng thử";
        vm.EventDate=null; vm.SaveCommand.Execute(null); Check(money.LastAward is null && vm.Error.Length>0,"cleared date cannot save an old hidden value"); vm.EventDate=DateTime.Today;
        vm.SaveCommand.Execute(null); Check(money.LastAward is { Handling:"PAYROLL", Amount:500000, Payment:null },"payroll dialog sends no voucher payment");
        vm=Award(); vm.InitializeAsync().GetAwaiter().GetResult(); vm.SelectedEmployee=vm.Employees[0]; vm.SelectedKind=vm.Kinds[1]; vm.SelectedHandling=vm.Handlings.Single(x=>x.Code=="NONE");
        vm.AmountText="500.000"; vm.Description="Chỉ ghi nhận"; vm.SaveCommand.Execute(null); Check(money.LastAward is { Handling:"NONE",Amount:0,PayYear:null,Payment:null },"note-only ignores hidden money and period inputs");
        vm=Award(); vm.InitializeAsync().GetAwaiter().GetResult(); vm.SelectedEmployee=vm.Employees[0]; vm.SelectedHandling=vm.Handlings.Single(x=>x.Code=="CASH"); vm.AmountText="100.000"; vm.Description="Chi thử";
        vm.SaveCommand.Execute(null); Check(money.LastAward is { Handling:"CASH",PayYear:null,Payment:{ AmountVnd:100000 } },"cash dialog sends payment without salary period");
        var closed=period with { Status="CLOSED" }; var line=closed.Lines.First(x=>x.NetSalary>0);
        var payVm=new EmployeeMoneyDialogViewModel(money,accessor,funds,banks,categories,closed,line); payVm.InitializeAsync().GetAwaiter().GetResult(); payVm.SaveCommand.Execute(null);
        Check(money.LastPay?.Payment.AmountVnd==line.NetSalary && money.LastPay.EmployeeId==line.EmployeeId,"salary dialog uses full net amount");
        var payroll=new Payroll(closed); var tab=new PayrollTabViewModel(payroll,accessor,null,money,funds,banks,categories);
        tab.EnsureLoadedAsync().GetAwaiter().GetResult(); tab.SelectedLine=tab.Rows.First(x=>x.EmployeeId==line.EmployeeId);
        Check(tab.PayCommand.CanExecute(null),"closed unpaid salary can be paid");
        var paidPeriod=closed with { Lines=closed.Lines.Select(x=>x.EmployeeId==line.EmployeeId?x with { PaymentVoucherId=100,PaymentVoucherCode="PC100" }:x).ToArray() };
        var paidTab=new PayrollTabViewModel(new Payroll(paidPeriod),accessor,null,money,funds,banks,categories); paidTab.EnsureLoadedAsync().GetAwaiter().GetResult(); paidTab.SelectedLine=paidTab.Rows.First(x=>x.EmployeeId==line.EmployeeId); paidTab.ReopenReason="Test";
        Check(!paidTab.PayCommand.CanExecute(null) && !paidTab.ReopenCommand.CanExecute(null) && paidTab.CancelPaymentCommand.CanExecute(null),"paid UI blocks duplicate/reopen and allows explicit reversal");
        foreach(var (theme,scale) in new[]{("Light",.9),("Light",1d),("Dark",1.25),("Dark",1.5)})
        {
            var awardVm=Award();awardVm.InitializeAsync().GetAwaiter().GetResult();awardVm.SelectedEmployee=awardVm.Employees[0];awardVm.Description="Khen thưởng hoàn thành công việc tháng này";awardVm.AmountText="500.000";
            Render(new EmployeeMoneyDialog(awardVm),theme,scale,620,740,"award-payroll");
            awardVm=Award();awardVm.InitializeAsync().GetAwaiter().GetResult();awardVm.SelectedHandling=awardVm.Handlings.Single(x=>x.Code=="CASH");
            Render(new EmployeeMoneyDialog(awardVm),theme,scale,500,550,"award-cash");
            var salaryVm = new EmployeeMoneyDialogViewModel(money,accessor,funds,banks,categories,closed,line); salaryVm.InitializeAsync().GetAwaiter().GetResult();
            Render(new EmployeeMoneyDialog(salaryVm),theme,scale,620,740,"salary-payment");
            var awardsTab=new EmployeeAwardsTabViewModel(money,accessor,funds,banks,categories,null);awardsTab.EnsureLoadedAsync().GetAwaiter().GetResult();
            Render(new Window{Content=new EmployeeAwardsTabView{DataContext=awardsTab}},theme,scale,1100,700,"awards-tab");
            Render(new Window{Content=new PayrollTabView{DataContext=paidTab}},theme,scale,1100,700,"payroll-tab");
        }
        Console.WriteLine("PASS UI: payroll/cash/note-only inputs, full salary payment, paid guards and Light/Dark render at 90–150% fonts.");
    }
    private static void Check(bool result,string label){if(!result)throw new Exception("FAILED UI: "+label);}
    private static void Render(Window window,string theme,double scale,int width,int height,string name)
    {
        foreach(var resource in new[]{theme,"Typography","Controls"})window.Resources.MergedDictionaries.Insert(0,new ResourceDictionary{Source=new Uri($"/Kazuko.Shared.UI;component/Themes/{resource}.xaml",UriKind.Relative)});
        window.SetResourceReference(Window.BackgroundProperty,"AppBackgroundBrush");window.SetResourceReference(Window.ForegroundProperty,"TextPrimaryBrush");window.SetResourceReference(Window.FontSizeProperty,"FontSize.Body");
        foreach(var key in new[]{"FontSize.Caption","FontSize.Body","FontSize.Subtitle","FontSize.Heading"})window.Resources[key]=(double)window.FindResource(key)*scale;
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
        var content=(FrameworkElement)window.Content;content.Measure(new Size(width-24,height-48));content.Arrange(new Rect(0,0,width-24,height-48));content.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind); content.UpdateLayout();
        if (content is PayrollTabView payrollView && payrollView.FindName("PayrollGrid") is System.Windows.Controls.DataGrid grid)
            Check(grid.Items.Count>0,"payroll grid receives rows before render");
        var visual=new DrawingVisual();using(var dc=visual.RenderOpen()){dc.DrawRectangle((Brush)window.FindResource("AppBackgroundBrush"),null,new Rect(0,0,width-24,height-48));dc.DrawRectangle(new VisualBrush(content),null,new Rect(0,0,width-24,height-48));}
        var target=new RenderTargetBitmap(width-24,height-48,96,96,PixelFormats.Pbgra32);target.Render(visual);
        if(Environment.GetEnvironmentVariable("KAZUKO_HR_MONEY_SCREENSHOTS") is {Length:>0} folder)
        {Directory.CreateDirectory(folder);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(target));using var output=File.Create(Path.Combine(folder,$"{name}-{theme}-{scale}.png"));encoder.Save(output);}
        window.Close();
    }
    private sealed class Funds:ICashFundRepository {public Task<IReadOnlyList<CashFundOption>> GetActiveFundsAsync(AuthenticatedSession session,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<CashFundOption>>([new(1,"Q","Quỹ tiền mặt",true)]);}
    private sealed class Banks:IBankAccountRepository {public Task<IReadOnlyList<BankAccountOption>> GetActiveAccountsAsync(AuthenticatedSession session,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<BankAccountOption>>([new(1,"NH","Ngân hàng","0123456789")]);}
    private sealed class Categories:IVoucherCategoryService
    {public Task DeleteAsync(long id,string voucherType,AuthenticatedSession session,CancellationToken ct=default)=>throw new NotSupportedException();
     public Task<IReadOnlyList<VoucherCategory>> ListAsync(string voucherType,bool includeInactive,AuthenticatedSession session,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<VoucherCategory>>([new(1,voucherType,voucherType=="OUT"?"BONUS":"OTHER",voucherType=="OUT"?"Chi thưởng":"Thu khác",true)]);
     public Task<VoucherCategory> SaveAsync(VoucherCategorySaveRequest request,AuthenticatedSession session,CancellationToken ct=default)=>throw new NotSupportedException();}
    private sealed class Money:IEmployeeMoneyService
    {
        public EmployeeAwardRequest? LastAward; public PayrollPayRequest? LastPay;
        public Task<IReadOnlyList<EmployeeMoneyOption>> GetEmployeesAsync(AuthenticatedSession session,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<EmployeeMoneyOption>>([new(1,"NV001","Nguyễn Văn A")]);
        public Task<IReadOnlyList<EmployeeAward>> ListAsync(DateTime from,DateTime to,AuthenticatedSession session,CancellationToken ct=default)=>Task.FromResult<IReadOnlyList<EmployeeAward>>([new(1,1,"NV001","Nguyễn Văn A",DateTime.Today,"REWARD","PAYROLL",500000,"Hoàn thành tốt công việc",2026,10,"ACTIVE",null,null,1,"DRAFT")]);
        public Task<EmployeeAward> CreateAsync(EmployeeAwardRequest request,AuthenticatedSession session,CancellationToken ct=default){LastAward=request;return Task.FromResult(new EmployeeAward(1,request.EmployeeId,"NV001","Nguyễn Văn A",request.EventDate,request.Kind,request.Handling,request.Amount,request.Description,request.PayYear,request.PayMonth,"ACTIVE",null,null,null,null));}
        public Task CancelAsync(long id,string reason,AuthenticatedSession session,CancellationToken ct=default)=>Task.CompletedTask;
        public Task<EmployeeMoneyResult> PaySalaryAsync(PayrollPayRequest request,AuthenticatedSession session,CancellationToken ct=default){LastPay=request;return Task.FromResult(new EmployeeMoneyResult(1,"PC001"));}
        public Task CancelSalaryPaymentAsync(long periodId,long employeeId,long voucherId,string reason,AuthenticatedSession session,CancellationToken ct=default)=>Task.CompletedTask;
    }
    private sealed class Payroll(PayrollPeriod period):IPayrollService
    {
        public Task<PayrollPeriod?> GetAsync(int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<PayrollPeriod?>(period);
        public Task<PayrollPeriod> CalculateAsync(int year,int month,PayrollSettings settings,int? revision,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<PayrollPeriod> AdjustAsync(PayrollAdjustment request,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<PayrollPeriod> CloseAsync(long periodId,int revision,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<PayrollPeriod> ReopenAsync(long periodId,int revision,string reason,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
}
