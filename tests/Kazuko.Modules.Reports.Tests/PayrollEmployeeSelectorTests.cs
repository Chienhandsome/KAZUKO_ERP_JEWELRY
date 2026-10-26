using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Modules.Reports;
using Xunit;

namespace Kazuko.Modules.Reports.Tests;

public sealed class PayrollEmployeeSelectorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reload_with_bound_employee_selector_keeps_selection_and_builds_preview(bool individual)
    {
        Exception? failure=null;
        var thread=new Thread(()=>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                var session=new CurrentSessionAccessor();session.Set(new(1,"test","Test","ADMIN",1,"CN","CN"));
                using var vm=new PayrollReportViewModel(new ReportService(),session,individual);
                var combo=new ComboBox { DataContext=vm };
                combo.SetBinding(ItemsControl.ItemsSourceProperty,new Binding(nameof(vm.Employees)));
                combo.SetBinding(ComboBox.SelectedItemProperty,new Binding(nameof(vm.SelectedEmployee)){Mode=BindingMode.TwoWay});
                Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
                vm.ReloadAsync().GetAwaiter().GetResult();
                if(individual)
                {
                    combo.SelectedItem=vm.Employees.Single(x=>x.Id==2);
                    Dispatcher.CurrentDispatcher.Invoke(()=>{},DispatcherPriority.DataBind);
                    Assert.Equal(2,vm.SelectedEmployee!.Id);
                    vm.ReloadAsync().GetAwaiter().GetResult();
                    Assert.Equal(2,vm.SelectedEmployee!.Id);
                }
                Assert.False(vm.IsEmpty,$"{vm.StatusMessage}; {vm.ErrorMessage}; Selected={vm.SelectedEmployee?.Id}");Assert.False(vm.HasError,vm.ErrorMessage);Assert.True(vm.HasPreviewPdf,vm.StatusMessage);
                vm.ReloadAsync().GetAwaiter().GetResult();
                Assert.True(vm.HasPreviewPdf,vm.StatusMessage);
                Assert.Equal(individual?2:0,vm.SelectedEmployee!.Id);
            }
            catch(Exception ex){failure=ex;}
        });
        thread.SetApartmentState(ApartmentState.STA);thread.Start();thread.Join();
        if(failure is not null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private sealed class ReportService:IPayrollReportService
    {
        public Task<IReadOnlyList<PayrollReportEmployee>> GetEmployeesAsync(int year,int month,bool includeDraft,AuthenticatedSession session,CancellationToken ct=default)
            => Task.FromResult<IReadOnlyList<PayrollReportEmployee>>([new(2,"NV2","Nhân viên đã chi")]);
        public Task<PayrollReportResult> GetAsync(PayrollReportFilter filter,AuthenticatedSession session,CancellationToken ct=default)
            => Task.FromResult(new PayrollReportResult(filter,1,"Doanh nghiệp",null,"CLOSED",2,DateTime.Today,
                [new(2,"NV2","Nhân viên đã chi","PER_SHIFT",26,20,100000,2000000,0,"",0,0,"",0,"",2000000,"PC01",DateTime.Today,"CASH")]));
    }
}
