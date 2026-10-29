using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.UserManagement;
using Kazuko.Modules.PriceBoard;
using Xunit;

namespace Kazuko.Modules.PriceBoard.Tests;

public sealed class TvHostLifetimeTests
{
    [Fact]
    public async Task Detaching_ERP_leaves_the_host_running_and_a_new_controller_reuses_it()
    {
        var host=new Host();var factory=new Factory(host);var session=new CurrentSessionAccessor();session.Set(Session);
        var controller=new PriceBoardTvService(new Company(),session,new Authorization(),factory,new Displays());
        try
        {
            await controller.InitializeAsync();
            Assert.Equal("http://127.0.0.1:8878/same-token/",controller.LocalUrl);
            await controller.OpenDisplayAsync(Monitor);
            session.Clear();controller.Detach();
            Assert.Equal(0,host.StopCalls);
            Assert.True(host.Status.DisplayOpen);
            session.Set(Session);
            var reopened=new PriceBoardTvService(new Company(),session,new Authorization(),factory,new Displays());
            try{await reopened.InitializeAsync();Assert.Equal(controller.LocalUrl,reopened.LocalUrl);Assert.Equal(0,host.StopCalls);}
            finally{reopened.Detach();}
        }
        finally{controller.Detach();}
    }
    [Fact]
    public async Task Late_connection_after_logout_does_not_reattach_the_old_ERP_session()
    {
        var host=new Host();var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory=new Factory(host){Wait=gate.Task};var session=new CurrentSessionAccessor();session.Set(Session);
        var controller=new PriceBoardTvService(new Company(),session,new Authorization(),factory,new Displays());
        var pending=controller.InitializeAsync();session.Clear();controller.Detach();gate.SetResult();await pending;
        Assert.Equal("",controller.LocalUrl);Assert.Equal(0,host.StopCalls);
        controller.Detach();
    }
    [Fact]
    public async Task Disabled_preview_is_a_configure_request_not_a_process_kill()
    {
        var host=new Host();var session=new CurrentSessionAccessor();session.Set(Session);
        var controller=new PriceBoardTvService(new Company(),session,new Authorization(),new Factory(host),new Displays());
        try
        {
            await controller.InitializeAsync();await controller.ApplyAsync(new(Enabled:false));
            Assert.False(host.Status.Settings.Enabled);Assert.Equal(0,host.StopCalls);
        }
        finally{controller.Detach();}
    }
    [Fact]
    public async Task User_without_edit_permission_cannot_attach_or_configure_a_background_host()
    {
        var host=new Host();var factory=new Factory(host);var session=new CurrentSessionAccessor();session.Set(Session);
        var controller=new PriceBoardTvService(new Company(),session,new Authorization(false),factory,new Displays());
        await Assert.ThrowsAsync<InvalidOperationException>(()=>controller.InitializeAsync());
        Assert.Equal(0,factory.Calls);controller.Detach();
    }
    private static readonly AuthenticatedSession Session=new(1,"admin","Admin","ADMIN",1,"CN","CN");
    private static readonly TvMonitor Monitor=new("tv","TV",1920,0,1920,1080,false);
    private sealed class Displays:ITvDisplayService
    {
        public IReadOnlyList<TvMonitor> GetMonitors()=>[Monitor];
        public void ShowMiracastDevicePicker(){}public void ShowProjectionModePicker(){}
    }
    private sealed class Company:ICompanyProfileService
    {
        public Task<CompanyProfileDto> GetAsync(AuthenticatedSession session,CancellationToken cancellationToken=default)=>Task.FromResult(new CompanyProfileDto(1,"Test",null,null,null,null));
        public Task SaveAsync(CompanyProfileUpdateRequest request,AuthenticatedSession session,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
    private sealed class Authorization(bool allowed=true):IAuthorizationService
    {
        public PermissionSnapshot Current=>allowed?new(true,[]):PermissionSnapshot.Denied;
        public event EventHandler? Changed{add{}remove{}}
        public Task RefreshAsync(CancellationToken cancellationToken=default)=>Task.CompletedTask;
        public void Clear(){}
        public Task DemandAsync(IEnumerable<PermissionRequirement> requirements,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    }
    private sealed class Factory(Host host):IPriceBoardTvHostClientFactory
    {
        public Task? Wait;public int Calls;
        public async Task<IPriceBoardTvHostClient> ConnectAsync(AuthenticatedSession session,long companyId,CancellationToken cancellationToken=default)
        {Calls++;if(Wait is not null)await Wait;return host;}
    }
    private sealed class Host:IPriceBoardTvHostClient
    {
        public int StopCalls;
        public PriceBoardHostStatus Status=new(new(Enabled:true),[],"http://127.0.0.1:8878/same-token/",[],false,false,4321);
        public Task<PriceBoardHostStatus> StatusAsync(CancellationToken cancellationToken=default)=>Task.FromResult(Status);
        public Task<PriceBoardHostStatus> ConfigureAsync(PriceBoardTvSettings settings,CancellationToken cancellationToken=default)
        {Status=Status with{Settings=settings};return Task.FromResult(Status);}
        public Task<PriceBoardHostStatus> OpenDisplayAsync(string device,CancellationToken cancellationToken=default)
        {Status=Status with{DisplayOpen=true};return Task.FromResult(Status);}
        public Task StopAsync(CancellationToken cancellationToken=default){StopCalls++;return Task.CompletedTask;}
    }
}
