using System.Text.Json;
using Kazuko.Core.HumanResources;
using Kazuko.Modules.HumanResources;
using Xunit;
namespace Kazuko.Modules.HumanResources.Tests;
public sealed class PayrollDetailViewModelTests
{
 [Theory]
 [InlineData(false,true,true)]
 [InlineData(false,false,false)]
 [InlineData(true,true,false)]
 [InlineData(true,false,false)]
 public void OnlyDraftWithEditGrantCanSave(bool closed,bool grant,bool expected)
 {
  var employee=new EmployeeDetail(1,"NV1","Nhân viên",null,"",null,"1","","","",null,"","",EmployeeStatusCodes.Working,true,SalaryMethodCodes.PerShift,150000,0,true,true);
  var snapshot=new PayrollSnapshot(1,2026,10,new(1,false),new(employee,Array.Empty<PayrollAttendance>()));
  var line=new PayrollLine(1,1,"NV1","Nhân viên",SalaryMethodCodes.PerShift,22,1,150000,150000,0,"",0,"","",JsonSerializer.Serialize(snapshot));
  var period=new PayrollPeriod(1,2026,10,closed?"CLOSED":"DRAFT",1,1,false,null,new[]{line});
  var vm=new PayrollDetailViewModel(period,line,_=>Task.CompletedTask,grant);
  Assert.Equal(expected,vm.IsEditable);Assert.Equal(expected,vm.SaveCommand.CanExecute(null));
 }
}
