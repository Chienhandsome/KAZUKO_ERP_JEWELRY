using System.Windows;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.PriceBoard;

/// <summary>ERP-side controller only. The detached host owns HTTP, polling and fullscreen windows.</summary>
public sealed class PriceBoardTvService : IPriceBoardTvController, IPriceBoardTvSettingsService
{
    private readonly ICompanyProfileService _company;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService _authorization;
    private readonly IPriceBoardTvHostClientFactory _hosts;
    private readonly ITvDisplayService _displays;
    private readonly DispatcherTimer _timer;
    private IPriceBoardTvHostClient? _host;
    private PriceBoardTvSettingsWindow? _settingsWindow;
    private PriceBoardTvSettings _settings=new();
    private IReadOnlyList<GoldPriceRecord> _rows=[];
    private string _localUrl="",_lan="LAN đang tắt.",_background="Chưa khởi động tiến trình bảng giá.";
    private long _companyId,_branchId,_userId;
    private bool _refreshing;
    private int _epoch;
    public event Action? Changed;
    public string LocalUrl=>_localUrl;
    public string LanAddresses=>_lan;
    public string BackgroundStatus=>_background;
    public PriceBoardTvSettings Settings=>_settings;
    public IReadOnlyList<GoldPriceRecord> Prices=>_rows;

    public PriceBoardTvService(ICompanyProfileService company,ICurrentSessionAccessor session,
        IAuthorizationService authorization,IPriceBoardTvHostClientFactory hosts,ITvDisplayService displays)
    {
        _company=company;_session=session;_authorization=authorization;_hosts=hosts;_displays=displays;
        _timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        _timer.Tick+=async(_,_)=>await RefreshAsync();
    }
    private AuthenticatedSession RequireSession()
    {
        if(!_authorization.Current.Allows("CAT_GOLD_PRICE",PermissionAction.Edit))
            throw new InvalidOperationException("Bạn cần quyền sửa bảng giá để thiết lập/phát lên TV.");
        return _session.Current??throw new InvalidOperationException("Chưa đăng nhập.");
    }
    public void ShowSettings()
    {
        try
        {
            RequireSession();
            if(_settingsWindow is not null){_settingsWindow.Activate();return;}
            var vm=new PriceBoardTvSettingsViewModel(this,_displays);
            _settingsWindow=new PriceBoardTvSettingsWindow(vm){Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(x=>x.IsActive)};
            _settingsWindow.Closed+=(_,_)=>{_settingsWindow=null;_timer.Stop();};
            _settingsWindow.Show();
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Bảng giá TV",MessageBoxButton.OK,MessageBoxImage.Information);}
    }
    public async Task InitializeAsync()
    {
        var session=RequireSession();
        var company=await _company.GetAsync(session);
        var epoch=++_epoch;
        var host=await _hosts.ConnectAsync(session,company.CompanyId);
        var status=await host.StatusAsync();
        if(epoch!=_epoch||_session.Current?.UserId!=session.UserId||_session.Current?.BranchId!=session.BranchId)return;
        _host=host;_companyId=company.CompanyId;_branchId=session.BranchId;_userId=session.UserId;
        Update(status);_timer.Start();
    }
    public async Task ApplyAsync(PriceBoardTvSettings settings)
    {
        RequireSession();settings.Validate();
        var host=_host??throw new InvalidOperationException("Chưa nối được tiến trình bảng giá. Đóng và mở lại thiết lập TV.");
        Update(await host.ConfigureAsync(settings));
    }
    public async Task OpenDisplayAsync(TvMonitor monitor)
    {
        RequireSession();
        if(!_displays.GetMonitors().Any(x=>x.Device==monitor.Device))
            throw new InvalidOperationException("Màn hình đã ngắt kết nối. Hãy kết nối lại và chọn màn hình TV.");
        var host=_host??throw new InvalidOperationException("Chưa khởi động tiến trình bảng giá TV.");
        Update(await host.OpenDisplayAsync(monitor.Device));
    }
    private void Update(PriceBoardHostStatus status)
    {
        _settings=status.Settings;_rows=status.Prices;_localUrl=status.LocalUrl;
        _lan=status.LanUrls.Count==0?"LAN đang tắt.":string.Join(Environment.NewLine,status.LanUrls);
        _background=!string.IsNullOrEmpty(status.Error)?status.Error:status.Paused?"Bảng giá đang tạm dừng ở khay hệ thống.":
            status.Settings.Enabled?"Bảng giá chạy nền độc lập. Đóng ERP vẫn tiếp tục phát; dừng tại biểu tượng Bảng giá TV ở khay hệ thống.":
            "Đang xem thử. Bật hiển thị TV để tiếp tục phát sau khi đóng ERP.";
        Changed?.Invoke();
    }
    private async Task RefreshAsync()
    {
        if(_refreshing||_host is null)return;
        if(_session.Current?.UserId!=_userId||_session.Current?.BranchId!=_branchId||
            !_authorization.Current.Allows("CAT_GOLD_PRICE",PermissionAction.Edit)){Detach();return;}
        _refreshing=true;var epoch=_epoch;
        try{var status=await _host.StatusAsync();if(epoch==_epoch)Update(status);}
        catch{if(epoch==_epoch){_background="Không nối được tiến trình bảng giá. Kiểm tra biểu tượng khay hệ thống hoặc mở lại thiết lập TV.";Changed?.Invoke();}}
        finally{_refreshing=false;}
    }
    public void Detach()
    {
        _epoch++;_timer.Stop();_host=null;_userId=0;
        // Closing ERP or logging out must never send a stop command to the host.
    }
}
