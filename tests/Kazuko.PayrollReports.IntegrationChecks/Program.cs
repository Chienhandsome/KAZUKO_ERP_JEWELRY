using System.IO;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Core.Reports;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.HumanResources;
using Kazuko.Infrastructure.Reports;
using Kazuko.Infrastructure.UserManagement;

if(args.Length!=2 || !Path.GetFileName(args[0]).StartsWith("test89_",StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Use a restored test89_*.fdb and artifact output folder; never DATA.FDB.");
var path=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with {DatabasePath=path,UseEmbedded=false});
await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync();
var branch=await c.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B JOIN COMPANIES C ON C.ID=B.COMPANY_ID WHERE C.CODE='KAZUKO' ORDER BY B.ID");
var user=await c.QueryFirstAsync<long>("SELECT ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var session=new AuthenticatedSession(user,"test","Người kiểm tra","ADMIN",branch,"TEST","Test");
var accessor=new CurrentSessionAccessor();accessor.Set(session);
var payroll=new FirebirdPayrollService(accessor);var reports=new FirebirdPayrollReportService();var money=new FirebirdEmployeeMoneyService();
var year=await c.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(PAY_YEAR),2100)+1 FROM HR_PAYROLL_PERIODS");
var period=await payroll.CalculateAsync(year,2,new(1,false),null,session);
var line=period.Lines.First();
period=await payroll.AdjustAsync(new(period.Id,period.Revision,line.Id,500000,"Thưởng hoàn thành [công việc]",100000,"Điều chỉnh theo kỳ"),session);
Check((await reports.GetAsync(new(year,2),session)).Rows.Count==0,"draft excluded by default");
Check((await reports.GetEmployeesAsync(year,2,false,session)).Count==0,"draft employees excluded by default");
var draft=await reports.GetAsync(new(year,2,IncludeDraft:true),session);
Check(draft.IsDraft && draft.Rows.Count==period.Lines.Count,"explicit draft preview");
var frozen=draft.Rows.Single(x=>x.EmployeeId==line.EmployeeId);
Check(frozen.ManualBonus==500000 && frozen.ManualDeduction==100000,"manual breakdown retained");
period=await payroll.CloseAsync(period.Id,period.Revision,session);
var result=await reports.GetAsync(new(year,2,line.EmployeeId),session);
Check(result.Rows.Count==1 && result.Rows[0].NetSalary==frozen.NetSalary && result.Rows[0].PaidAmount==0,"closed snapshot and employee filter");
var fund=await c.QueryFirstAsync<long>("SELECT ID FROM CASH_FUNDS WHERE BRANCH_ID=@branch AND IS_ACTIVE=TRUE ORDER BY ID",new{branch});
var pay=await money.PaySalaryAsync(new(period.Id,period.Revision,line.EmployeeId,DateTime.Today,new("CASH",frozen.NetSalary,fund,null,null)),session);
var paid=await reports.GetAsync(new(year,2,line.EmployeeId),session);
Check(paid.Rows[0].PaidAmount==frozen.NetSalary && paid.Rows[0].RemainingAmount==0 && paid.Rows[0].VoucherCode==pay.VoucherCode,"actual posted payment reflected");
await money.CancelSalaryPaymentAsync(period.Id,line.EmployeeId,pay.VoucherId,"Report verification",session);
var cancelled=await reports.GetAsync(new(year,2,line.EmployeeId),session);
Check(cancelled.Rows[0].PaidAmount==0 && cancelled.Rows[0].VoucherCode is null && cancelled.Rows[0].RemainingAmount==frozen.NetSalary,"cancelled voucher excluded, salary snapshot retained");
Check((await reports.GetEmployeesAsync(year,2,false,session)).Any(x=>x.Id==line.EmployeeId && x.Name==frozen.Name),"employee choices from payroll snapshots");
var otherBranch=await c.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B WHERE B.COMPANY_ID<>(SELECT COMPANY_ID FROM BRANCHES WHERE ID=@branch) ORDER BY B.ID",new{branch});
Check((await reports.GetAsync(new(year,2),session with{BranchId=otherBranch})).Rows.Count==0,"company isolation");
await Reject(()=>reports.GetAsync(new(year,13),session),"invalid month");
await Reject(()=>reports.GetAsync(new(year,2,0),session),"invalid employee");
foreach(var method in new[]{"GetAsync","GetEmployeesAsync"})
{
    Check(!BackendAccess.Allows("IPayrollReportService",method,[],Rights("REP_VIEW")),"report grant alone cannot read payroll");
    Check(!BackendAccess.Allows("IPayrollReportService",method,[],Rights("HR_PAYROLL")),"payroll grant alone cannot read report");
    Check(BackendAccess.Allows("IPayrollReportService",method,[],Rights("REP_VIEW","HR_PAYROLL")),"both grants allow payroll reports");
}
var guarded=AuthorizedServiceProxy<IPayrollReportService>.Create(reports,new TestAuth(Rights("REP_VIEW")),accessor);
await Reject(()=>guarded.GetAsync(new(year,2),session),"protected service rejects missing payroll grant");
guarded=AuthorizedServiceProxy<IPayrollReportService>.Create(reports,new TestAuth(Rights("REP_VIEW","HR_PAYROLL")),accessor);
await Reject(()=>guarded.GetAsync(new(year,2),session with {BranchId=otherBranch}),"protected service rejects forged session");
Check((await guarded.GetAsync(new(year,2),session)).Rows.Count==period.Lines.Count,"protected service works for actual session");
UiChecks.Run(session,paid,output);
Console.WriteLine("ALL PAYROLL REPORT CHECKS PASSED: draft/closed filters, snapshot breakdown, posted/cancelled payments, company scope, permissions, UI and A4 PDF generation.");
static void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);Console.WriteLine("PASS: "+label);}
static async Task Reject(Func<Task> action,string label){try{await action();}catch(InvalidOperationException){return;}catch(UnauthorizedAccessException){return;}throw new Exception("Expected rejection: "+label);}
static PermissionSnapshot Rights(params string[] codes)=>new(false,codes.Select(x=>new GroupPermissionRow(1,x,true,false,false,false,false)));
internal sealed class TestAuth(PermissionSnapshot rights):IAuthorizationService
{
 public PermissionSnapshot Current{get;private set;}=rights;public event EventHandler? Changed;
 public Task RefreshAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;
 public void Clear(){Current=PermissionSnapshot.Denied;Changed?.Invoke(this,EventArgs.Empty);}
 public Task DemandAsync(IEnumerable<PermissionRequirement> requirements,CancellationToken cancellationToken=default)
 {if(requirements.Any(x=>!Current.Allows(x.Function,x.Action)))throw new UnauthorizedAccessException("Denied");return Task.CompletedTask;}
}
