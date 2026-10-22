using System.IO;
using System.Text.Json;
using System.Diagnostics;
using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.HumanResources;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.HumanResources;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.UserManagement;
Environment.SetEnvironmentVariable("KAZUKO_FIREBIRD_HOME",Path.GetFullPath("firebird/Firebird-5.0.4.1812-0-windows-x64"));
var original=FirebirdDatabase.Profile;
async Task Run(string exe,params string[] arguments) {
 var info=new ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var a in arguments)info.ArgumentList.Add(a);
 info.Environment["ISC_USER"]=original.UserName;info.Environment["ISC_PASSWORD"]=original.Password;info.Environment["KAZUKO_FIREBIRD_PASSWORD"]=original.Password;
 using var process=Process.Start(info)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();Console.WriteLine(await output);if(process.ExitCode!=0)throw new Exception("Database tool failed: "+await error);
}
Directory.CreateDirectory("artifacts/payroll-check");
var stamp=DateTime.Now.ToString("yyyyMMdd-HHmmss");var backup=Path.GetFullPath("artifacts/payroll-check/before-82-"+stamp+".fbk");
await Run(Path.Combine(FirebirdRuntime.HomeDirectory,"gbak.exe"),"-b","-g",FirebirdDatabase.DatabasePath,backup);
Console.WriteLine("BACKUP: "+backup);
{
 var test=Path.GetFullPath("artifacts/payroll-check/test-"+stamp+".fdb");await Run(Path.Combine(FirebirdRuntime.HomeDirectory,"gbak.exe"),"-c",backup,test);FirebirdDatabase.ApplyProfileForCurrentProcess(original with{DatabasePath=test});
}
await Run("pwsh.exe","-NoProfile","-File","database/tools/Invoke-KazukoMigration.ps1","-MigrationPath","database/migrations/0082__hr_payroll_snapshots.sql","-Database",FirebirdDatabase.DatabasePath);
await Run("pwsh.exe","-NoProfile","-File","database/tools/Invoke-KazukoMigration.ps1","-MigrationPath","database/migrations/0084__attendance_periods.sql","-Database",FirebirdDatabase.DatabasePath);
var readOnly=new PermissionSnapshot(false,[new(1,"CAT_EMPLOYEES",true,false,false,false,false)]);
if(BackendAccess.Allows("IHumanResourcesRepository","CreateAttendanceMonthAsync",[],readOnly)
 || BackendAccess.Allows("IHumanResourcesRepository","DeleteAttendanceMonthAsync",[],readOnly)) throw new Exception("Read grant permits period mutations");
var addOnly=new PermissionSnapshot(false,[new(1,"CAT_EMPLOYEES",true,true,false,false,false)]);
if(!BackendAccess.Allows("IHumanResourcesRepository","CreateAttendanceMonthAsync",[],addOnly)
 || BackendAccess.Allows("IHumanResourcesRepository","DeleteAttendanceMonthAsync",[],addOnly)) throw new Exception("Attendance period grant mapping incorrect");
var service=new FirebirdPayrollService();
await using var c=FirebirdDatabase.CreateConnection();await c.OpenAsync();
var actor=await c.QuerySingleAsync("SELECT FIRST 1 U.ID,U.USERNAME,U.DISPLAY_NAME,B.ID BRANCH_ID,B.CODE BRANCH_CODE,B.NAME BRANCH_NAME FROM USERS U JOIN ROLES R ON R.ID=U.ROLE_ID CROSS JOIN BRANCHES B WHERE R.CODE='ADMIN' AND U.IS_ACTIVE=TRUE ORDER BY U.ID,B.ID");
var session=new AuthenticatedSession((long)actor.ID,(string)actor.USERNAME,(string)actor.DISPLAY_NAME,"ADMIN",(long)actor.BRANCH_ID,(string)actor.BRANCH_CODE,(string)actor.BRANCH_NAME);
await c.ExecuteAsync("DELETE FROM HR_PAYROLL_LINES WHERE PERIOD_ID IN(SELECT ID FROM HR_PAYROLL_PERIODS WHERE PAY_YEAR=2026 AND PAY_MONTH=10)");
await c.ExecuteAsync("DELETE FROM HR_PAYROLL_PERIODS WHERE PAY_YEAR=2026 AND PAY_MONTH=10");
var repository=new FirebirdHumanResourcesRepository();var id=await c.QuerySingleAsync<long>("SELECT FIRST 1 ID FROM HR_EMPLOYEES ORDER BY ID");var e=(await repository.GetByIdAsync(id))!;
await c.ExecuteAsync("DELETE FROM HR_ATTENDANCE_RECORDS WHERE EMPLOYEE_ID=@id AND WORK_DATE>=DATE '2026-10-01' AND WORK_DATE<DATE '2026-11-01'",new{id});
await c.ExecuteAsync("UPDATE HR_EMPLOYEES SET SALARY_METHOD='MONTHLY_BY_DAY',MONTHLY_SALARY=9000000,SHIFT_SALARY=150000,REST_ON_SUNDAY=TRUE,REST_ON_SATURDAY=TRUE,HIRE_DATE=DATE '2026-01-01',IS_ACTIVE=TRUE,STATUS_CODE='WORKING' WHERE ID=@id",new{id});
var shifts=await repository.GetWorkShiftsAsync();
// A period must survive a new repository/connection even with zero attendance cells.
await repository.CreateAttendanceMonthAsync(2091,1,session);
await repository.CreateAttendanceMonthAsync(2091,1,session);
var reloadedMonths=await new FirebirdHumanResourcesRepository().GetAttendanceMonthsAsync();
if(reloadedMonths.Count(m=>m.Year==2091 && m.Month==1)!=1 || reloadedMonths.Single(m=>m.Year==2091 && m.Month==1).HasData)
 throw new Exception("Empty period was not persisted exactly once");
var futureEntries=new[]{new AttendanceEntry(id,shifts[0].Id,new DateTime(2091,1,1))};
await repository.SetAttendanceStatusAsync(futureEntries,AttendanceStatusCodes.Work,session);
if(!(await repository.GetAttendanceMonthsAsync()).Single(m=>m.Year==2091 && m.Month==1).HasData) throw new Exception("Stored attendance not reflected");
await Reject(()=>repository.DeleteAttendanceMonthAsync(2091,1,session),"delete attendance period containing records");
if((await repository.GetAttendanceRecordsAsync(2091,1)).Count!=1) throw new Exception("Delete lost attendance data");
await repository.SetAttendanceStatusAsync(futureEntries,AttendanceStatusCodes.None,session);
if((await repository.GetAttendanceMonthsAsync()).Single(m=>m.Year==2091 && m.Month==1).HasData) throw new Exception("Cleared period still marked nonempty");
await repository.DeleteAttendanceMonthAsync(2091,1,session);
if((await new FirebirdHumanResourcesRepository().GetAttendanceMonthsAsync()).Any(m=>m.Year==2091 && m.Month==1)) throw new Exception("Deleted empty period returned after reload");
await repository.SetAttendanceStatusAsync(futureEntries,AttendanceStatusCodes.Work,session);
if(!(await repository.GetAttendanceMonthsAsync()).Any(m=>m.Year==2091 && m.Month==1)) throw new Exception("Direct attendance did not create period");
await repository.SetAttendanceStatusAsync(futureEntries,AttendanceStatusCodes.None,session);
await repository.DeleteAttendanceMonthAsync(2091,1,session);
Console.WriteLine("PASS: attendance empty period persistence, idempotency, record/delete guards and reload");
await repository.SetAttendanceStatusAsync(new[]{new AttendanceEntry(id,shifts[0].Id,new DateTime(2026,10,1)),new AttendanceEntry(id,shifts[1].Id,new DateTime(2026,10,1))},AttendanceStatusCodes.Work,session);
var p=await service.CalculateAsync(2026,10,new(2,false),null,session);var line=p.Lines.Single(l=>l.EmployeeId==id);if(line.Salary!=409091)throw new Exception("Daily salary incorrect: "+line.Salary);
async Task Reject(Func<Task> call,string label){try{await call();throw new Exception("Expected rejection: "+label);}catch(InvalidOperationException){Console.WriteLine("REJECTED: "+label);}}
await Reject(()=>service.AdjustAsync(new(p.Id,p.Revision,line.Id,-.1m,"invalid",0,""),session),"negative fractional bonus");
p=await service.AdjustAsync(new(p.Id,p.Revision,line.Id,50000,"Thưởng thử",10000,"Khấu trừ thử"),session);
await Reject(()=>service.CloseAsync(p.Id,p.Revision-1,session),"stale revision");
p=await service.CalculateAsync(2026,10,new(2,false),p.Revision,session);if(p.Lines.Single(l=>l.EmployeeId==id).Bonus!=50000)throw new Exception("Adjustment lost");
await repository.SetAttendanceStatusAsync(new[]{new AttendanceEntry(id,shifts[0].Id,new DateTime(2026,10,2))},AttendanceStatusCodes.Work,session);
await Reject(()=>service.CloseAsync(p.Id,p.Revision,session),"changed attendance");
p=await service.CalculateAsync(2026,10,new(2,false),p.Revision,session);p=await service.CloseAsync(p.Id,p.Revision,session);var frozen=JsonSerializer.Serialize(p);
await Reject(()=>service.CalculateAsync(2026,10,new(1,false),p.Revision,session),"calculate closed payroll");
await Reject(()=>service.AdjustAsync(new(p.Id,p.Revision,p.Lines[0].Id,0,"",0,""),session),"adjust closed payroll");
await c.ExecuteAsync("UPDATE HR_EMPLOYEES SET MONTHLY_SALARY=12000000 WHERE ID=@id",new{id});
if(JsonSerializer.Serialize(await service.GetAsync(2026,10))!=frozen)throw new Exception("Closed payroll changed with live profile");
await File.WriteAllTextAsync("artifacts/payroll-check/preview-period.json",JsonSerializer.Serialize(p));
await Reject(()=>service.ReopenAsync(p.Id,p.Revision,"",session),"missing reopen reason");
p=await service.ReopenAsync(p.Id,p.Revision,"Kiểm tra mở chốt",session);p=await service.CalculateAsync(2026,10,new(2,false),p.Revision,session);
if(p.Lines.Single(l=>l.EmployeeId==id).Rate!=12000000)throw new Exception("Recalculation did not refresh rate");
var audit=await c.QuerySingleAsync<int>("SELECT COUNT(*) FROM AUDIT_LOG WHERE ENTITY_TYPE='HR_PAYROLL_PERIODS' AND ENTITY_ID=@id",new{id=p.Id.ToString()});if(audit<7)throw new Exception("Missing audit");
Console.WriteLine("PASS: migration, calculation, adjustment preservation, revision conflicts, source staleness, freeze, reopen, audit. Test DB: "+FirebirdDatabase.DatabasePath);
