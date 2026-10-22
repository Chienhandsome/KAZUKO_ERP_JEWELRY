using Kazuko.Core.HumanResources;
using Xunit;
namespace Kazuko.Modules.HumanResources.Tests;
public sealed class PayrollCalculatorTests
{
 private static EmployeeDetail Employee(string method) => new(1,"NV1","Nhân viên",null,"",null,"1","","","",null,"","",EmployeeStatusCodes.Working,true,method,150000,9000000,true,true);
 private static PayrollInput Input(string method, params PayrollAttendance[] records) => new(Employee(method),records);
 private static PayrollAttendance Day(int day,int shift=1,string status=AttendanceStatusCodes.Work) => new(shift,"Ca",new DateTime(2026,10,day),status);
 [Fact] public void FullMonthByDay_PaysFullMonthlySalary()
 {
  var records=Enumerable.Range(1,31).Select(d=>new DateTime(2026,10,d)).Where(d=>d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday).Select(d=>Day(d.Day)).ToArray();
  var result=PayrollCalculator.Calculate(2026,10,new(2,false),Input(SalaryMethodCodes.MonthlyByDay,records));
  Assert.Equal(22,result.StandardDays);Assert.Equal(22m,result.ActualUnits);Assert.Equal(9000000m,result.Salary);
 }
 [Fact] public void DailySalary_CountsMultipleShiftsOnce()
 {
  var result=PayrollCalculator.Calculate(2026,10,new(2,false),Input(SalaryMethodCodes.MonthlyByDay,Day(1),Day(1,2)));
  Assert.Equal(1m,result.ActualUnits);Assert.Equal(409091m,result.Salary);
 }
 [Fact] public void MonthlyByShift_UsesConfiguredShiftsPerDay()
 {
  var result=PayrollCalculator.Calculate(2026,10,new(2,false),Input(SalaryMethodCodes.MonthlyByShift,Day(1),Day(1,2)));
  Assert.Equal(44m,result.StandardUnits);Assert.Equal(2m,result.ActualUnits);Assert.Equal(409091m,result.Salary);
 }
 [Fact] public void PerShift_MultipliesWorkedShifts()
 {
  var result=PayrollCalculator.Calculate(2026,10,new(2,false),Input(SalaryMethodCodes.PerShift,Day(1),Day(1,2)));
  Assert.Equal(300000m,result.Salary);
 }
 [Theory][InlineData(false,1)][InlineData(true,2)]
 public void PaidLeave_IsExplicit(bool include,int units)
 {
  var result=PayrollCalculator.Calculate(2026,10,new(1,include),Input(SalaryMethodCodes.PerShift,Day(1),Day(2,status:AttendanceStatusCodes.PaidLeave),Day(3,status:AttendanceStatusCodes.UnpaidLeave)));
  Assert.Equal((decimal)units,result.ActualUnits);Assert.Equal(1,result.PaidLeaveShifts);Assert.Equal(1,result.UnpaidLeaveShifts);
 }
 [Fact] public void OnlyCountsCurrentMonthAfterHireDate()
 {
  var input=Input(SalaryMethodCodes.PerShift,Day(1),Day(15),new(1,"Ca",new DateTime(2026,11,1),AttendanceStatusCodes.Work));
  input=input with{Employee=input.Employee with{HireDate=new DateTime(2026,10,10)}};
  var result=PayrollCalculator.Calculate(2026,10,new(1,false),input);Assert.Equal(1m,result.ActualUnits);Assert.Equal(22m,result.StandardUnits);
 }
 [Fact] public void EmptyAttendance_IsZeroWithWarning()
 {
  var result=PayrollCalculator.Calculate(2026,10,new(1,false),Input(SalaryMethodCodes.PerShift));Assert.Equal(0,result.Salary);Assert.Contains("Chưa có",result.Warning);
 }
 [Fact] public void WeekendAttendance_IsNotSilentlyDiscarded()
 {
  var result=PayrollCalculator.Calculate(2026,10,new(1,false),Input(SalaryMethodCodes.PerShift,Day(3)));Assert.Equal(150000m,result.Salary);
 }
 [Theory][InlineData(0)][InlineData(25)] public void InvalidShifts_Throws(int shifts) => Assert.Throws<InvalidOperationException>(()=>PayrollCalculator.Calculate(2026,10,new(shifts,false),Input(SalaryMethodCodes.PerShift)));
 [Fact] public void RoundsVndAwayFromZero()
 {
  var input=Input(SalaryMethodCodes.PerShift,Day(1));input=input with{Employee=input.Employee with{ShiftSalary=1.5m}};Assert.Equal(2m,PayrollCalculator.Calculate(2026,10,new(1,false),input).Salary);
 }
 [Theory][InlineData(-1,0,"x","")][InlineData(0,-1,"","x")][InlineData(1,0,"","")][InlineData(0,1,"","")][InlineData(0,101,"","x")]
 public void InvalidAdjustment_Throws(int bonus,int deduction,string bonusNote,string deductionNote) => Assert.Throws<InvalidOperationException>(()=>PayrollCalculator.ValidateAdjustment(100,new(1,1,1,bonus,bonusNote,deduction,deductionNote)));
 [Fact] public void ValidAdjustment_Passes() => PayrollCalculator.ValidateAdjustment(100,new(1,1,1,20,"Thưởng",10,"Khấu trừ"));
}
