using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.UserManagement;
using Xunit;
namespace Kazuko.Infrastructure.Tests.HumanResources;
public sealed class PayrollAccessTests
{
 [Fact] public void DefaultDenied() { foreach(var method in new[]{"GetAsync","CalculateAsync","AdjustAsync","CloseAsync","ReopenAsync","Unknown"}) Assert.False(BackendAccess.Allows("IPayrollService",method,[],PermissionSnapshot.Denied)); }
 [Fact] public void ReopenRequiresItsOwnEditGrant()
 {
  var view=new GroupPermissionRow(1,"HR_PAYROLL",true,true,true,false,false);
  Assert.False(BackendAccess.Allows("IPayrollService","ReopenAsync",[],new(false,[view])));
  Assert.True(BackendAccess.Allows("IPayrollService","ReopenAsync",[],new(false,[view,new(1,"HR_PAYROLL_REOPEN",true,false,true,false,false)])));
  Assert.False(BackendAccess.Allows("IPayrollService","ReopenAsync",[],new(false,[new(1,"HR_PAYROLL_REOPEN",true,false,true,false,false)])));
 }
 [Fact] public void ViewDoesNotGrantCalculateOrClose()
 {
  var rights=new PermissionSnapshot(false,[new(1,"HR_PAYROLL",true,false,false,false,false)]);
  Assert.True(BackendAccess.Allows("IPayrollService","GetAsync",[],rights));
  Assert.False(BackendAccess.Allows("IPayrollService","CalculateAsync",[],rights));Assert.False(BackendAccess.Allows("IPayrollService","CloseAsync",[],rights));
 }
 [Fact] public void LockedPermissionDeniedEvenWithEdit()
 {
  var rights=new PermissionSnapshot(false,[new(1,"HR_PAYROLL",true,true,true,true,true)]);Assert.False(BackendAccess.Allows("IPayrollService","AdjustAsync",[],rights));
 }
}
