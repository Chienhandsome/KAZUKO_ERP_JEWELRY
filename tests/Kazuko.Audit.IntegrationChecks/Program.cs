using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.Customers;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.HumanResources;
using Kazuko.Infrastructure.Suppliers;

if(args.Length!=1 || !Path.GetFileName(args[0]).StartsWith("test89_",StringComparison.OrdinalIgnoreCase))
    throw new ArgumentException("Fault injection is allowed ONLY on a restored test89_*.fdb; never DATA.FDB.");
var path=Path.GetFullPath(args[0]);
FirebirdDatabase.ApplyProfileForCurrentProcess(ConnectionProfileStore.Load(path) with {DatabasePath=path,UseEmbedded=false});
await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync();
var branch=await c.QueryFirstAsync<long>("SELECT B.ID FROM BRANCHES B JOIN COMPANIES C ON C.ID=B.COMPANY_ID WHERE C.CODE='KAZUKO' ORDER BY B.ID");
var user=await c.QueryFirstAsync<long>("SELECT ID FROM USERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var session=new AuthenticatedSession(user,"test","Audit test","ADMIN",branch,"TEST","Test");
var accessor=new CurrentSessionAccessor();accessor.Set(session);
var hr=new FirebirdHumanResourcesRepository();var customers=new FirebirdCustomerRepository();var suppliers=new FirebirdSupplierRepository();
var payroll=new FirebirdPayrollService(accessor);var money=new FirebirdEmployeeMoneyService();var categories=new FirebirdVoucherCategoryService();
var tag=Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
var employee=await c.QueryFirstAsync<long>("SELECT ID FROM HR_EMPLOYEES WHERE IS_ACTIVE=TRUE ORDER BY ID");
var customer=await c.QueryFirstAsync<long>("SELECT ID FROM CUSTOMERS WHERE IS_WALK_IN=FALSE AND IS_ACTIVE=TRUE ORDER BY ID");
var supplier=await c.QueryFirstAsync<long>("SELECT ID FROM SUPPLIERS WHERE IS_ACTIVE=TRUE ORDER BY ID");
var shift=await hr.SaveWorkShiftAsync(new(null,"Audit ca "+tag,TimeSpan.FromHours(8),TimeSpan.FromHours(12)),session);
var year=await c.ExecuteScalarAsync<int>("SELECT COALESCE(MAX(PAY_YEAR),2100)+1 FROM HR_PAYROLL_PERIODS");
var period=await payroll.CalculateAsync(year,3,new(1,false),null,session);
var line=period.Lines.Single(x=>x.EmployeeId==employee);
period=await payroll.AdjustAsync(new(period.Id,period.Revision,line.Id,100000,"Audit test",0,""),session);
period=await payroll.CloseAsync(period.Id,period.Revision,session);line=period.Lines.Single(x=>x.EmployeeId==employee);
var fund=await c.QueryFirstAsync<long>("SELECT ID FROM CASH_FUNDS WHERE BRANCH_ID=@branch AND IS_ACTIVE=TRUE ORDER BY ID",new{branch});
var vouchersBefore=await Count("CASH_VOUCHERS");var paymentsBefore=await Count("HR_PAYROLL_PAYMENTS");var awardsBefore=await Count("HR_EMPLOYEE_AWARDS");
var trigger="T_AUD_"+tag;var exception="E_AUD_"+tag;var triggerCreated=false;
await c.ExecuteAsync($"CREATE EXCEPTION {exception} 'Injected audit insert failure'");
try
{
    await c.ExecuteAsync($"CREATE TRIGGER {trigger} FOR AUDIT_LOG ACTIVE BEFORE INSERT POSITION 0 AS BEGIN EXCEPTION {exception}; END");triggerCreated=true;
    await Reject(()=>hr.SaveWorkShiftAsync(new(shift.Id,"Changed "+tag,null,null,false),session),"shift update rejects failed audit");
    Check((await hr.GetWorkShiftsAsync(false)).Single(x=>x.Id==shift.Id)==shift,"shift mutation rolled back");
    await Reject(()=>hr.SetActiveAsync(employee,false,session),"employee status rejects failed audit");
    Check(await c.ExecuteScalarAsync<bool>("SELECT IS_ACTIVE FROM HR_EMPLOYEES WHERE ID=@employee",new{employee}),"employee remains active");
    await Reject(()=>customers.SetActiveAsync(customer,false,session),"customer status rejects failed audit");
    Check(await c.ExecuteScalarAsync<bool>("SELECT IS_ACTIVE FROM CUSTOMERS WHERE ID=@customer",new{customer}),"customer remains active");
    await Reject(()=>suppliers.SetActiveAsync(supplier,false,session),"supplier status rejects failed audit");
    Check(await c.ExecuteScalarAsync<bool>("SELECT IS_ACTIVE FROM SUPPLIERS WHERE ID=@supplier",new{supplier}),"supplier remains active");
    var groups=await Count("CUSTOMER_GROUPS");
    await Reject(()=>customers.CreateGroupAsync("Audit group "+tag,session),"group create rejects failed audit");
    Check(await Count("CUSTOMER_GROUPS")==groups,"group creation rolled back");
    var cats=await Count("VOUCHER_CATEGORIES");
    await Reject(()=>categories.SaveAsync(new(null,"OUT","AUD_"+tag,"Audit",true),session),"category create rejects failed audit");
    Check(await Count("VOUCHER_CATEGORIES")==cats,"category creation rolled back");
    await Reject(()=>money.CreateAsync(new(Guid.NewGuid(),employee,DateTime.Today,"REWARD","NONE",0,"Audit test"),session),"award creation rejects failed audit");
    Check(await Count("HR_EMPLOYEE_AWARDS")==awardsBefore,"award rolled back");
    await Reject(()=>money.PaySalaryAsync(new(period.Id,period.Revision,employee,DateTime.Today,new("CASH",line.NetSalary,fund,null,null)),session),"salary payment rejects failed audit");
    Check(await Count("CASH_VOUCHERS")==vouchersBefore && await Count("HR_PAYROLL_PAYMENTS")==paymentsBefore,"salary and linked voucher both rolled back");
    Check((await payroll.GetAsync(year,3))!.Revision==period.Revision,"failed salary payment keeps period revision");
    await Reject(()=>new FirebirdFinanceVoucherService().CreateManualVoucherAsync(new("OUT",DateTime.Today,"CASH",1000,"Audit test","Audit test",null),session),"manual voucher rejects failed audit");
    Check(await Count("CASH_VOUCHERS")==vouchersBefore,"manual voucher rolled back");
    var day=new DateTime(year,4,10);var attendance=await Count("HR_ATTENDANCE_RECORDS");var months=await Count("HR_ATTENDANCE_PERIODS");
    await Reject(()=>hr.SetAttendanceStatusAsync([new(employee,shift.Id,day)],AttendanceStatusCodes.Work,session),"attendance rejects failed audit");
    Check(await Count("HR_ATTENDANCE_RECORDS")==attendance && await Count("HR_ATTENDANCE_PERIODS")==months,"attendance and new period both rolled back");
}
finally
{
    if(triggerCreated)await c.ExecuteAsync($"DROP TRIGGER {trigger}");
    await c.ExecuteAsync($"DROP EXCEPTION {exception}");
}
var successful=await hr.SaveWorkShiftAsync(new(shift.Id,shift.Name,shift.StartTime,shift.EndTime,false),session);
Check(!successful.IsActive,"normal save succeeds after audit restored");
Check(await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM AUDIT_LOG WHERE EVENT_TYPE='WORK_SHIFT_UPDATED' AND ENTITY_ID=@id",new{id=shift.Id.ToString()})>0,"successful mutation has audit record");
Console.WriteLine("ALL REQUIRED AUDIT CHECKS PASSED; fault trigger removed; only test89 database was written.");
async Task<int> Count(string table)=>await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table}");
static void Check(bool ok,string label){if(!ok)throw new Exception("FAILED: "+label);Console.WriteLine("PASS: "+label);}
static async Task Reject(Func<Task> action,string label)
{
    try{await action();}
    catch(InvalidOperationException ex) when(ex.Message.StartsWith("Không thể ghi nhật ký kiểm toán",StringComparison.Ordinal)){Console.WriteLine("PASS: "+label);return;}
    throw new Exception("Expected audit failure: "+label);
}
