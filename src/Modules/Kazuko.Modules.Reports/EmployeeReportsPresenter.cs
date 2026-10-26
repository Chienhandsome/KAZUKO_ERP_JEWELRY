using System.Windows;
using Kazuko.Core.Authentication;
using Kazuko.Core.Reports;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.Reports;

public sealed class EmployeeReportsPresenter(IPayrollReportService service, ICurrentSessionAccessor session, IAuthorizationService permissions) : IEmployeeReportsPresenter
{
    public async Task ShowPayrollAsync(bool individual)
    {
        await permissions.DemandAsync([new("REP_VIEW"),new("HR_PAYROLL")]);
        var vm=new PayrollReportViewModel(service,session,individual,permissions);
        var window=new Window
        {
            Title=vm.Title,Width=1200,Height=800,MinWidth=900,MinHeight=550,WindowStartupLocation=WindowStartupLocation.CenterOwner,
            ShowInTaskbar=false,Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(x=>x.IsActive),Content=new PayrollReportView {DataContext=vm}
        };
        window.SetResourceReference(Window.BackgroundProperty,"AppBackgroundBrush");
        window.Loaded+=async(_,_)=>await vm.EnsureLoadedAsync();
        window.Closed+=(_,_)=>vm.Dispose();
        window.ShowDialog();
    }
}
