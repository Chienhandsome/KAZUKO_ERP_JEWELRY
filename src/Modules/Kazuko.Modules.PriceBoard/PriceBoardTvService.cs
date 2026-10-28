using System.Windows;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.UserManagement;

namespace Kazuko.Modules.PriceBoard;

public sealed class PriceBoardTvService : IPriceBoardTvController
{
    private readonly IPriceBoardRepository _prices;
    private readonly ICompanyProfileService _company;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService _authorization;
    private readonly IPriceBoardTvSettingsStore _store;
    private readonly IPriceBoardTvServerFactory _servers;
    private readonly DispatcherTimer _timer;
    private IPriceBoardTvServer? _server;
    private PriceBoardTvSettingsWindow? _settingsWindow;
    private PriceBoardTvWindow? _tv;
    private bool _refreshing,_allowLan;
    private int _port;
    private long _userId,_branchId,_companyId;
    private IReadOnlyList<GoldPriceRecord> _rows=[];
    private PriceBoardTvSettings _settings=new();
    private PriceBoardTvSnapshot _snapshot=PriceBoardTvSnapshotBuilder.Build(new(),[],0,false);
    private DateTimeOffset _lastSuccess;
    private int _epoch;
    public event Action? Changed;
    public string LocalUrl=>_server?.LocalUrl??"";
    public string LanAddresses=>_server is null||!_allowLan?"LAN đang tắt.":_server.LanUrls.Count==0?"Chưa có địa chỉ IPv4 mạng LAN.":string.Join(Environment.NewLine,_server.LanUrls);
    public PriceBoardTvSettings Settings=>_settings;
    public IReadOnlyList<GoldPriceRecord> Prices=>_rows;

    public PriceBoardTvService(IPriceBoardRepository prices,ICompanyProfileService company,ICurrentSessionAccessor session,
        IAuthorizationService authorization,IPriceBoardTvSettingsStore store,IPriceBoardTvServerFactory servers)
    {
        _prices=prices;_company=company;_session=session;_authorization=authorization;_store=store;_servers=servers;
        _timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        _timer.Tick+=async(_,_)=>await RefreshAsync();
    }
    private AuthenticatedSession RequireSession()
    {
        if(!_authorization.Current.Allows("CAT_GOLD_PRICE",PermissionAction.Edit))throw new InvalidOperationException("Bạn cần quyền sửa bảng giá để thiết lập/phát lên TV.");
        return _session.Current??throw new InvalidOperationException("Chưa đăng nhập.");
    }
    public void ShowSettings()
    {
        try
        {
            RequireSession();
            if(_settingsWindow is not null){_settingsWindow.Activate();return;}
            var vm=new PriceBoardTvSettingsViewModel(this);
            _settingsWindow=new PriceBoardTvSettingsWindow(vm){Owner=Application.Current?.Windows.OfType<Window>().FirstOrDefault(x=>x.IsActive)};
            _settingsWindow.Closed+=(_,_)=>{_settingsWindow=null;if(!_settings.Enabled)Stop();};
            _settingsWindow.Show();
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Bảng giá TV",MessageBoxButton.OK,MessageBoxImage.Information);}
    }
    public async Task InitializeAsync()
    {
        var s=RequireSession();var company=await _company.GetAsync(s);
        if(_companyId!=company.CompanyId||_branchId!=s.BranchId||_userId!=s.UserId)
        {
            Stop();_companyId=company.CompanyId;_branchId=s.BranchId;_userId=s.UserId;
            _settings=_store.Load(_companyId,_branchId);
            if(string.IsNullOrWhiteSpace(_settings.ShopName))_settings=_settings with{ShopName=company.Name};
        }
        _rows=await _prices.GetGoldPricesAsync();_lastSuccess=DateTimeOffset.UtcNow;
        StartServer(_settings);Publish();_timer.Start();Changed?.Invoke();
    }
    private void StartServer(PriceBoardTvSettings settings)
    {
        var allow=settings.Enabled&&settings.AllowLan;
        if(_server is not null&&_port==settings.Port&&_allowLan==allow)return;
        // Binding mode may change on the same port; rollback to the previous listener if it fails.
        var previous=_server;var oldPort=_port;var oldAllow=_allowLan;
        if(previous is not null&&oldPort==settings.Port){previous.Dispose();_server=null;}
        try
        {
            var next=_servers.Start(settings.Port,allow,ReadPublicSnapshot);
            if(_server is not null)_server.Dispose();
            _server=next;_port=settings.Port;_allowLan=allow;
            _tv?.Navigate(_server.LocalUrl);
        }
        catch
        {
            if(previous is not null&&_server is null)
            {try{_server=_servers.Start(oldPort,oldAllow,ReadPublicSnapshot);_port=oldPort;_allowLan=oldAllow;}catch{_server=null;}}
            Changed?.Invoke();throw;
        }
    }
    private PriceBoardTvSnapshot ReadPublicSnapshot()
    {
        var value=Volatile.Read(ref _snapshot);
        if(_session.Current is not { } s||s.UserId!=_userId||s.BranchId!=_branchId||!_authorization.Current.Allows("CAT_GOLD_PRICE",PermissionAction.Edit))
            return value with{Active=false,Rows=[],Status="Bảng giá đang tạm dừng."};
        return value;
    }
    public async Task ApplyAsync(PriceBoardTvSettings settings)
    {
        RequireSession();settings.Validate();
        _rows=await _prices.GetGoldPricesAsync();_lastSuccess=DateTimeOffset.UtcNow;
        var old=_settings;StartServer(settings);
        try{_store.Save(_companyId,_branchId,settings);_settings=settings;}
        catch{StartServer(old);throw;}
        if(!settings.Enabled){_tv?.Close();_tv=null;}
        Publish();_timer.Start();Changed?.Invoke();
    }
    public void OpenHdmi(TvMonitor monitor)
    {
        RequireSession();if(!_settings.Enabled)throw new InvalidOperationException("Hãy bật bảng giá TV và áp dụng trước khi mở HDMI.");
        _tv?.Close();_tv=new PriceBoardTvWindow(LocalUrl,monitor);_tv.Closed+=(_,_)=>_tv=null;_tv.Show();
    }
    private void Publish(bool stale=false)
    {
        var active=!stale||DateTimeOffset.UtcNow-_lastSuccess<TimeSpan.FromSeconds(60);
        Volatile.Write(ref _snapshot,PriceBoardTvSnapshotBuilder.Build(_settings,_rows,_companyId,active,stale,
            stale?"Không cập nhật được giá — kiểm tra kết nối ERP. Giá có thể đã cũ.":""));
    }
    private async Task RefreshAsync()
    {
        if(_refreshing)return;
        if(_session.Current?.UserId!=_userId||_session.Current?.BranchId!=_branchId||!_authorization.Current.Allows("CAT_GOLD_PRICE",PermissionAction.Edit)){Stop();return;}
        _refreshing=true;
        var epoch=_epoch;
        try{var rows=await _prices.GetGoldPricesAsync();if(epoch!=_epoch)return;_rows=rows;_lastSuccess=DateTimeOffset.UtcNow;Publish();}
        catch{if(epoch==_epoch)Publish(true);}
        finally{_refreshing=false;}
    }
    public void Stop()
    {
        _epoch++;
        _timer.Stop();Volatile.Write(ref _snapshot,_snapshot with{Active=false,Rows=[],Status="Bảng giá đang tạm dừng."});
        _server?.Dispose();_server=null;_tv?.Close();_tv=null;_userId=0;Changed?.Invoke();
    }
}
