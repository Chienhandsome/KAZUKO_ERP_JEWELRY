using System.Reflection;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.HumanResources;
using Xunit;

namespace Kazuko.Modules.HumanResources.Tests;

public sealed class PayrollPaymentStateTests
{
    private static PayrollLine Line(long employee,decimal salary) => new(employee,employee,$"NV{employee}",$"Nhân viên {employee}","PER_SHIFT",26,20,100000,salary,0,"",0,"","","");
    private static PayrollPeriod Period(string status="DRAFT") => new(1,2026,10,status,1,1,false,null,[Line(1,0),Line(2,2000000)]);
    private static (PayrollTabViewModel Vm, PayrollFake Payroll, Auth Auth) Setup(string status="DRAFT")
    {
        var payroll=new PayrollFake(Period(status)); var session=new CurrentSessionAccessor(); session.Set(new(1,"test","Test","ADMIN",1,"CN","Chi nhánh"));
        var auth=new Auth();
        return (new(payroll,session,auth,Unused<IEmployeeMoneyService>(),Unused<ICashFundRepository>(),Unused<IBankAccountRepository>(),Unused<IVoucherCategoryService>()),payroll,auth);
    }
    private static T Unused<T>() where T:class => DispatchProxy.Create<T,UnusedService>();

    [Fact]
    public async Task Closing_loaded_draft_enables_payment_and_does_not_reopen_period()
    {
        var (vm,payroll,_)=Setup(); await vm.EnsureLoadedAsync();
        Assert.False(vm.PayCommand.CanExecute(null)); Assert.Contains("bản nháp",vm.PaymentHint);
        Assert.Equal(2,vm.SelectedLine!.EmployeeId);
        vm.ConfirmClose=_=>true; vm.CloseCommand.Execute(null);
        Assert.True(vm.IsClosed); Assert.True(vm.PayCommand.CanExecute(null)); Assert.Contains("Sẵn sàng chi",vm.PaymentHint);
        Assert.Equal(1,payroll.CloseCalls); Assert.Equal(0,payroll.ReopenCalls);
        vm.ReopenReason="Sửa công"; vm.ConfirmReopen=_=>false; vm.ReopenCommand.Execute(null);
        Assert.True(vm.IsClosed); Assert.True(vm.PayCommand.CanExecute(null)); Assert.Equal(0,payroll.ReopenCalls);
        vm.ConfirmReopen=_=>true; vm.ReopenCommand.Execute(null);
        Assert.False(vm.IsClosed); Assert.False(vm.PayCommand.CanExecute(null)); Assert.Equal(1,payroll.ReopenCalls);
    }

    [Fact]
    public async Task Zero_salary_selection_and_revoked_finance_permission_have_explicit_reasons()
    {
        var (vm,_,auth)=Setup("CLOSED"); await vm.EnsureLoadedAsync();
        Assert.True(vm.PayCommand.CanExecute(null));
        vm.SelectedLine=vm.Rows.First(); Assert.False(vm.PayCommand.CanExecute(null)); Assert.Contains("bằng 0",vm.PaymentHint);
        vm.SelectedLine=vm.Rows.Last(); auth.CanPay=false; auth.RaiseChanged();
        Assert.False(vm.PayCommand.CanExecute(null)); Assert.Contains("Phiếu chi / Thêm",vm.PaymentHint);
        auth.CanPay=true; auth.RaiseChanged(); Assert.True(vm.PayCommand.CanExecute(null));
    }

    public class UnusedService:DispatchProxy
    { protected override object? Invoke(MethodInfo? targetMethod,object?[]? args)=>throw new NotSupportedException(); }
    private sealed class PayrollFake(PayrollPeriod period):IPayrollService
    {
        public int CloseCalls,ReopenCalls;
        public Task<PayrollPeriod?> GetAsync(int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<PayrollPeriod?>(period);
        public Task<PayrollPeriod> CalculateAsync(int year,int month,PayrollSettings settings,int? revision,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<PayrollPeriod> AdjustAsync(PayrollAdjustment request,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<PayrollPeriod> CloseAsync(long periodId,int revision,AuthenticatedSession session,CancellationToken cancellationToken=default)
        {CloseCalls++;period=period with{Status="CLOSED",ClosedAt=DateTime.Now,Revision=period.Revision+1};return Task.FromResult(period);}
        public Task<PayrollPeriod> ReopenAsync(long periodId,int revision,string reason,AuthenticatedSession session,CancellationToken cancellationToken=default)
        {ReopenCalls++;period=period with{Status="DRAFT",ClosedAt=null,Revision=period.Revision+1};return Task.FromResult(period);}
    }
    private sealed class Auth:IAuthorizationService
    {
        public bool CanPay=true;
        public PermissionSnapshot Current=>new(false,[new(1,"HR_PAYROLL",true,true,true,false,false),new(1,"FIN_CASH_OUT",true,CanPay,false,false,false)]);
        public event EventHandler? Changed;
        public void RaiseChanged()=>Changed?.Invoke(this,EventArgs.Empty);
        public Task RefreshAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public void Clear(){}
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
}
