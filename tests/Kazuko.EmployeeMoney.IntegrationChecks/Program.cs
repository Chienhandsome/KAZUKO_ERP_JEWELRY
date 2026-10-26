using System.IO;
using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.HumanResources;
using Kazuko.Infrastructure.UserManagement;

if (args.Length != 1 || !Path.GetFileName(args[0]).StartsWith("test89_", StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Use only a restored and migrated test89_*.fdb, never DATA.FDB.");
var path = Path.GetFullPath(args[0]);
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with { DatabasePath = path, UseEmbedded = false });
await using var c = FirebirdDatabase.CreateConnection(); await c.OpenAsync();
var branch = await c.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B JOIN COMPANIES C ON C.ID=B.COMPANY_ID WHERE C.CODE='KAZUKO' AND EXISTS(SELECT 1 FROM CASH_FUNDS F WHERE F.BRANCH_ID=B.ID AND F.IS_ACTIVE=TRUE) ORDER BY B.ID");
var user = await c.QueryFirstAsync<long>("SELECT ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var company = await c.QuerySingleAsync<long>("SELECT COMPANY_ID FROM BRANCHES WHERE ID=@branch", new { branch });
var employee = await c.QueryFirstAsync<long>("SELECT ID FROM HR_EMPLOYEES WHERE COMPANY_ID=@company ORDER BY ID", new { company });
await c.ExecuteAsync("UPDATE HR_EMPLOYEES SET IS_ACTIVE=TRUE,STATUS_CODE='WORKING',HIRE_DATE=DATE '2020-01-01',SALARY_METHOD='PER_SHIFT',SHIFT_SALARY=150000 WHERE ID=@employee", new { employee });
var fund = await c.QueryFirstAsync<long>("SELECT ID FROM CASH_FUNDS WHERE COMPANY_ID=@company AND BRANCH_ID=@branch AND IS_ACTIVE=TRUE ORDER BY ID", new { company, branch });
var bonusCategory = await c.QuerySingleAsync<long>("SELECT ID FROM VOUCHER_CATEGORIES WHERE COMPANY_ID=@company AND VOUCHER_TYPE='OUT' AND CODE='BONUS'", new { company });
var incomeCategory = await c.QuerySingleAsync<long>("SELECT ID FROM VOUCHER_CATEGORIES WHERE COMPANY_ID=@company AND VOUCHER_TYPE='IN' AND CODE='OTHER'", new { company });
var session = new AuthenticatedSession(user,"test","Test","ADMIN",branch,"TEST","Test");
var accessor = new CurrentSessionAccessor(); accessor.Set(session);
var service = new FirebirdEmployeeMoneyService(); var payroll = new FirebirdPayrollService(accessor);
var year = 2100 + Random.Shared.Next(1,500); const int month=1; var day = new DateTime(year,month,15);
PaymentAllocationDraft Cash(decimal amount) => new("CASH",amount,fund,null,null);
EmployeeAwardRequest Award(string kind,string handling,decimal amount) => new(Guid.NewGuid(),employee,day,kind,handling,amount,"Kiểm tra thưởng/lương",
    handling=="PAYROLL"?year:null,handling=="PAYROLL"?month:null,handling=="CASH"?Cash(amount):null,handling=="CASH"?(kind=="REWARD"?bonusCategory:incomeCategory):null);
var rewardRequest = Award("REWARD","PAYROLL",500000);
var reward = await service.CreateAsync(rewardRequest,session);
Check((await service.CreateAsync(rewardRequest,session)).Id==reward.Id,"award request idempotency");
await Reject(()=>service.CreateAsync(rewardRequest with { Amount=600000 },session),"same request id different payload");
var deduction = await service.CreateAsync(Award("DISCIPLINE","PAYROLL",100000),session);
var note = await service.CreateAsync(Award("DISCIPLINE","NONE",0),session);
var beforeVouchers = await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS");
var cashReward = await service.CreateAsync(Award("REWARD","CASH",200000),session);
var cashDeduction = await service.CreateAsync(Award("DISCIPLINE","CASH",50000),session);
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS")==beforeVouchers+2,"only cash awards create vouchers");
await Reject(()=>service.CreateAsync(Award("REWARD","CASH",1000) with { CategoryId=incomeCategory },session),"wrong category rolls back award and voucher");
var p = await payroll.CalculateAsync(year,month,new(1,false),null,session);
var line = p.Lines.Single(x=>x.EmployeeId==employee);
Check(line.RecordedBonus==500000 && line.RecordedDeduction==100000 && line.NetSalary==400000,"payroll includes recorded awards, excludes separate cash and notes");
p=await payroll.AdjustAsync(new(p.Id,p.Revision,line.Id,25000,"Điều chỉnh thêm",5000,"Điều chỉnh thêm"),session);
p=await payroll.CalculateAsync(year,month,new(1,false),p.Revision,session); line=p.Lines.Single(x=>x.EmployeeId==employee);
Check(line.Bonus==25000 && line.Deduction==5000 && line.NetSalary==420000,"recalculate preserves manual amounts without double import");
var late=await service.CreateAsync(Award("REWARD","PAYROLL",10000),session);
await Reject(()=>payroll.CloseAsync(p.Id,p.Revision,session),"new award invalidates close hash");
p=await payroll.CalculateAsync(year,month,new(1,false),p.Revision,session);
p=await payroll.CloseAsync(p.Id,p.Revision,session); line=p.Lines.Single(x=>x.EmployeeId==employee);
Check(line.NetSalary==430000,"closed total correct");
await Reject(()=>service.CreateAsync(Award("REWARD","PAYROLL",1000),session),"cannot add into closed period");
await Reject(()=>service.CancelAsync(reward.Id,"Test",session),"cannot cancel applied award in closed period");
await Reject(()=>payroll.AdjustAsync(new(p.Id,p.Revision,line.Id,1,"X",0,""),session),"closed payroll cannot adjust");
var voucherBeforePay=await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS");
await Reject(()=>service.PaySalaryAsync(new(p.Id,p.Revision,employee,DateTime.Today,Cash(1)),session),"payment must equal entire salary");
await Reject(()=>service.PaySalaryAsync(new(p.Id,p.Revision,employee,DateTime.Today,new("CASH",line.NetSalary,long.MaxValue,null,null)),session),"invalid fund rolls back pending payment");
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS")==voucherBeforePay,"invalid payment writes no voucher");
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM HR_PAYROLL_PAYMENTS WHERE STATUS='PENDING'")==0,"no committed pending payment");
var payRequest = new PayrollPayRequest(p.Id,p.Revision,employee,DateTime.Today,Cash(line.NetSalary));
var payment=await service.PaySalaryAsync(payRequest,session);
Check((await service.PaySalaryAsync(payRequest,session)).VoucherId==payment.VoucherId,"duplicate pay returns same voucher");
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM CASH_VOUCHERS")==voucherBeforePay+1,"one voucher per salary");
p=(await payroll.GetAsync(year,month))!; line=p.Lines.Single(x=>x.EmployeeId==employee);
Check(line.IsPaid && line.PaymentVoucherId==payment.VoucherId,"reload shows paid and voucher link");
await Reject(()=>payroll.ReopenAsync(p.Id,p.Revision,"Test",session),"paid payroll cannot reopen");
await Reject(()=>new FirebirdFinanceVoucherService().CancelVoucherAsync(payment.VoucherId,"Test",session),"generic finance cancellation cannot desync payroll");
await Reject(()=>service.CancelSalaryPaymentAsync(p.Id,employee,payment.VoucherId+1,"Test",session),"stale payment cannot cancel newer voucher");
await service.CancelSalaryPaymentAsync(p.Id,employee,payment.VoucherId,"Test",session);
p=(await payroll.GetAsync(year,month))!;
Check(!p.Lines.Single(x=>x.EmployeeId==employee).IsPaid,"cancel releases payroll payment state");
// Simultaneous pay attempts may serialize or reject with a Firebird update conflict; only one may commit.
var concurrentRequest = payRequest with { Revision=p.Revision };
var results=await Task.WhenAll(Attempt(()=>service.PaySalaryAsync(concurrentRequest,session)),Attempt(()=>service.PaySalaryAsync(concurrentRequest,session)));
Check(results.Any(x=>x is not null),"one concurrent pay succeeds");
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM HR_PAYROLL_PAYMENTS WHERE PERIOD_ID=@id AND EMPLOYEE_ID=@employee AND STATUS='POSTED'",new{id=p.Id,employee})==1,"concurrent pay never duplicates");
var current=(await payroll.GetAsync(year,month))!; var active=current.Lines.Single(x=>x.EmployeeId==employee);
await service.CancelSalaryPaymentAsync(p.Id,employee,active.PaymentVoucherId!.Value,"Test concurrency cleanup",session);
p=(await payroll.GetAsync(year,month))!; p=await payroll.ReopenAsync(p.Id,p.Revision,"Test reopen",session);
await service.CancelAsync(deduction.Id,"Test cancel deduction",session);
p=await payroll.CalculateAsync(year,month,new(1,false),p.Revision,session); line=p.Lines.Single(x=>x.EmployeeId==employee);
Check(line.RecordedDeduction==0 && line.RecordedBonus==510000 && line.NetSalary==530000,"reopen/recalculate after cancelled payment preserves historical payment records");
await service.CancelAsync(cashReward.Id,"Test cash award reversal",session);
Check(await c.QuerySingleAsync<string>("SELECT STATUS_CODE FROM CASH_VOUCHERS WHERE ID=@id",new{id=cashReward.VoucherId})=="CANCELLED","cash award cancellation reverses linked voucher");
var rows=await service.ListAsync(day.AddDays(-1),day.AddDays(1),session);
Check(rows.Single(x=>x.Id==late.Id).AppliedPeriodId==p.Id && rows.Single(x=>x.Id==deduction.Id).Status=="CANCELLED","award list keeps allocation/status links");
var onlyHr = new PermissionSnapshot(false,[new(1,"CAT_EMPLOYEES",true,true,true,true,false),new(1,"HR_PAYROLL",true,true,true,false,false)]);
Check(!BackendAccess.Allows("IEmployeeMoneyService","CreateAsync",[Award("REWARD","CASH",1000),session],onlyHr),"cash award needs finance grant");
Check(BackendAccess.Allows("IEmployeeMoneyService","CreateAsync",[Award("REWARD","PAYROLL",1000),session],onlyHr),"payroll award does not need finance grant");
Check(!BackendAccess.Allows("IEmployeeMoneyService","PaySalaryAsync",[payRequest,session],onlyHr),"salary pay needs finance grant");
var guarded=AuthorizedServiceProxy<IEmployeeMoneyService>.Create(service,new Auth(onlyHr),accessor);
await Reject(()=>guarded.CancelAsync(cashDeduction.Id,"Test",session),"actual cash cancel requires reverse permission");
var company2=await c.QuerySingleAsync<long>("INSERT INTO COMPANIES(CODE,NAME) VALUES(@code,'Other company') RETURNING ID",new{code="HR_"+Guid.NewGuid().ToString("N")[..10]});
var branch2=await c.QuerySingleAsync<long>("INSERT INTO BRANCHES(COMPANY_ID,CODE,NAME) VALUES(@company2,@code,'Other branch') RETURNING ID",new{company2,code="HR_"+Guid.NewGuid().ToString("N")[..10]});
var other=session with { BranchId=branch2 };
await Reject(()=>service.CreateAsync(Award("REWARD","NONE",0),other),"cross company employee");
await Reject(()=>service.PaySalaryAsync(payRequest,other),"cross company payroll");
Check(!(await service.ListAsync(day.AddDays(-1),day.AddDays(1),other)).Any(x=>x.EmployeeId==employee),"award list company isolation");
var accessor2=new CurrentSessionAccessor();accessor2.Set(other);
Check(await new FirebirdPayrollService(accessor2).GetAsync(year,month) is null,"payroll reads company from actual session");
Console.WriteLine("PASS: recorded/manual amounts, repeat calculation, stale-source/closed guards, cash separation, idempotency/concurrency, atomic reversal, company scope and permission guards.");
await UiChecks.RunAsync(session,p);
Console.WriteLine("ALL EMPLOYEE MONEY CHECKS PASSED; only test database was written.");
static void Check(bool result,string label){if(!result)throw new Exception("FAILED: "+label);}
static async Task Reject(Func<Task> action,string label)
{try{await action();}catch(InvalidOperationException){return;}catch(UnauthorizedAccessException){return;}throw new Exception("Expected rejection: "+label);}
static async Task<EmployeeMoneyResult?> Attempt(Func<Task<EmployeeMoneyResult>> action)
{try{return await action();}catch(FbException){return null;}}
sealed class Auth(PermissionSnapshot rights) : IAuthorizationService
{
 public PermissionSnapshot Current=>rights; public event EventHandler? Changed {add{} remove{}}
 public Task RefreshAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask; public void Clear(){}
 public Task DemandAsync(IEnumerable<PermissionRequirement> requirements,CancellationToken cancellationToken=default)=>Task.CompletedTask;
}
