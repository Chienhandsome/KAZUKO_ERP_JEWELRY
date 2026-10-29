using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Kazuko.Core.Authentication;
using Kazuko.Core.Company;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.PriceBoard;
using Kazuko.Modules.PriceBoard;
using Forms = System.Windows.Forms;

namespace Kazuko.App.PriceBoardHost;

/// <summary>Standalone desktop display lifetime; it never opens the ERP shell or writes business data.</summary>
internal sealed class PriceBoardHostRuntime : IDisposable
{
    private readonly string _scope;
    private readonly IPriceBoardTvPriceSource _prices;
    private readonly ICompanyProfileService _companies;
    private readonly IPriceBoardTvSettingsStore _store;
    private readonly IPriceBoardTvServerFactory _servers;
    private readonly ITvDisplayService _displays;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService _authorization;
    private readonly Action _shutdown;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private readonly Forms.NotifyIcon _tray;
    private readonly PriceBoardHostPipeServer _pipe;
    private IPriceBoardTvServer? _server;
    private PriceBoardTvWindow? _window;
    private PriceBoardTvSettings _settings = new();
    private IReadOnlyList<GoldPriceRecord> _rows = [];
    private PriceBoardTvSnapshot _snapshot = PriceBoardTvSnapshotBuilder.Build(new(), [], 0, false);
    private long _companyId, _branchId;
    private long _lastSuccessTicks;
    private int _boundPort;
    private bool _boundLan;
    private bool _paused, _disposed, _permissionAllowed = true;
    private DateTimeOffset _lastContact = DateTimeOffset.UtcNow, _lastSuccess, _permissionChecked;
    private string _error = "";

    public PriceBoardHostRuntime(string scope, IPriceBoardTvPriceSource prices, ICompanyProfileService companies,
        IPriceBoardTvSettingsStore store, IPriceBoardTvServerFactory servers, ITvDisplayService displays,
        ICurrentSessionAccessor session, IAuthorizationService authorization, Action shutdown)
    {
        _scope=scope; _prices=prices; _companies=companies; _store=store; _servers=servers; _displays=displays;
        _session=session; _authorization=authorization; _shutdown=shutdown; _dispatcher=Dispatcher.CurrentDispatcher;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Trạng thái / địa chỉ TV", null, (_, _) => _dispatcher.InvokeAsync(ShowStatus));
        menu.Items.Add("Chép địa chỉ TV qua LAN", null, (_, _) => _dispatcher.InvokeAsync(() => RunTray(CopyLanAddress)));
        menu.Items.Add("Hiện bảng giá trên màn hình đã chọn", null, (_, _) => _dispatcher.InvokeAsync(() => RunTray(OpenDisplay)));
        menu.Items.Add("Tạm dừng / tiếp tục phát", null, (_, _) => _dispatcher.InvokeAsync(() =>
        { _paused=!_paused; Publish(); ShowStatus(); }));
        menu.Items.Add("Mở ERP để thiết lập", null, (_, _) => _dispatcher.InvokeAsync(() => RunTray(OpenErp)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Dừng phát và thoát", null, (_, _) => _dispatcher.InvokeAsync(shutdown));
        _tray = new Forms.NotifyIcon { Text="KAZUKO · Bảng giá TV", Icon=System.Drawing.SystemIcons.Information,
            ContextMenuStrip=menu, Visible=true };
        _tray.DoubleClick += (_, _) => _dispatcher.InvokeAsync(ShowStatus);
        _pipe = new PriceBoardHostPipeServer(scope, async request =>
            await (await _dispatcher.InvokeAsync(() => HandleAsync(request))));
        _pipe.StopRequested += () => _dispatcher.InvokeAsync(shutdown);
        _pipe.Start();
        _timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
    }

    private PriceBoardHostStatus Status() => new(_settings, _rows, _server?.LocalUrl ?? "",
        _settings.Enabled && _settings.AllowLan ? _server?.LanUrls ?? [] : [],
        _window is not null, _paused, Environment.ProcessId, _error);

    private async Task<PriceBoardHostReply> HandleAsync(PriceBoardHostRequest request)
    {
        await _gate.WaitAsync();
        try
        {
            if (request.Action is not ("status" or "configure" or "display" or "stop"))
                throw new InvalidOperationException("Lệnh bảng giá TV không hợp lệ.");
            if (request.Session is null || request.Session.UserId <= 0 || request.CompanyId <= 0 ||
                PriceBoardHostProtocol.Scope(FirebirdDatabase.Profile, request.Session.BranchId) != _scope)
                throw new InvalidOperationException("Phiên bảng giá TV không đúng cơ sở dữ liệu/chi nhánh.");
            var changedUser = _session.Current?.UserId != request.Session.UserId;
            if (_companyId == 0 || changedUser || !_permissionAllowed || DateTimeOffset.UtcNow-_permissionChecked > TimeSpan.FromSeconds(15))
            {
                var previous = _session.Current;
                _session.Set(request.Session);
                try
                {
                    await _authorization.DemandAsync([new("CAT_GOLD_PRICE", PermissionAction.Edit)]);
                    var company = await _companies.GetAsync(request.Session);
                    if (company.CompanyId != request.CompanyId || (_companyId != 0 && company.CompanyId != _companyId))
                        throw new InvalidOperationException("Công ty bảng giá TV không khớp phiên đăng nhập.");
                    _permissionChecked=DateTimeOffset.UtcNow; _permissionAllowed=true;
                }
                catch { if (previous is not null) _session.Set(previous); else _session.Clear(); throw; }
            }
            if (_companyId != 0 && (_branchId != request.Session.BranchId || _companyId != request.CompanyId))
                throw new InvalidOperationException("Không thể thay phạm vi của tiến trình bảng giá đang chạy.");
            _lastContact=DateTimeOffset.UtcNow;
            if (_companyId == 0)
            {
                _companyId=request.CompanyId; _branchId=request.Session.BranchId;
                _settings=_store.Load(_companyId,_branchId);
                if (string.IsNullOrWhiteSpace(_settings.ShopName))
                    _settings=_settings with { ShopName=(await _companies.GetAsync(request.Session)).Name };
                try { await RefreshPricesAsync(); StartServer(_settings); }
                catch(Exception ex){Trace.TraceWarning("TV host first refresh failed: {0}",ex.Message);_error="Chưa đọc/phát được bảng giá. Kiểm tra DB hoặc đổi cổng TV rồi áp dụng.";}
                Publish();
            }
            switch (request.Action)
            {
                case "configure":
                    var settings=request.Settings ?? throw new InvalidOperationException("Thiếu cấu hình bảng giá.");
                    settings.Validate();
                    await RefreshPricesAsync();
                    var old=_settings;
                    StartServer(settings);
                    try { _store.Save(_companyId,_branchId,settings); _settings=settings; }
                    catch { StartServer(old); throw; }
                    if (!settings.Enabled) { _window?.Close(); _window=null; }
                    _paused=false; Publish();
                    break;
                case "display":
                    if (string.IsNullOrWhiteSpace(request.MonitorDevice)) throw new InvalidOperationException("Chưa chọn màn hình TV.");
                    OpenDisplay(request.MonitorDevice);
                    break;
                case "stop":
                    _window?.Close(); _window=null; _server?.Dispose(); _server=null;
                    break;
            }
            return new(Status());
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("TV host command failed: {0}",ex.Message);
            return new(null, ex is InvalidOperationException or UnauthorizedAccessException ? ex.Message : "Không thực hiện được lệnh bảng giá TV. Kiểm tra kết nối DB và thử lại.");
        }
        finally { _gate.Release(); }
    }

    private void StartServer(PriceBoardTvSettings settings)
    {
        var allow=settings.Enabled && settings.AllowLan;
        if (_server is not null && _boundPort==settings.Port && _boundLan==allow) return;
        var old=_server; var oldPort=_boundPort; var oldAllow=_boundLan;
        if (old is not null && oldPort==settings.Port) { old.Dispose(); _server=null; }
        try
        {
            var next=_servers.Start(settings.Port,allow,ReadSnapshot);
            _server?.Dispose(); _server=next; _boundPort=settings.Port; _boundLan=allow;
            _window?.Navigate(next.LocalUrl);
        }
        catch
        {
            if (old is not null && _server is null)
                try { _server=_servers.Start(oldPort,oldAllow,ReadSnapshot);_boundPort=oldPort;_boundLan=oldAllow;_window?.Navigate(_server.LocalUrl); }
                catch { _server=null; }
            throw;
        }
    }
    private void OpenDisplay() => OpenDisplay(_settings.MonitorDevice);
    private PriceBoardTvSnapshot ReadSnapshot()
    {
        var value=Volatile.Read(ref _snapshot);
        if(DateTime.UtcNow.Ticks-Interlocked.Read(ref _lastSuccessTicks)>TimeSpan.FromSeconds(60).Ticks)
            return value with {Active=false,Rows=[],Stale=true,Status="Chưa nhận được giá mới từ DB. Kiểm tra kết nối."};
        return value;
    }
    private void OpenDisplay(string device)
    {
        if (!_settings.Enabled) throw new InvalidOperationException("Hãy bật bảng giá TV trong ERP trước khi chiếu.");
        var monitor=_displays.GetMonitors().FirstOrDefault(x=>x.Device==device)
            ?? throw new InvalidOperationException("Màn hình TV đã ngắt kết nối. Mở ERP, kết nối và chọn lại TV.");
        if (_server is null) throw new InvalidOperationException("Bảng giá TV chưa khởi động.");
        _window?.Close();
        _window=new PriceBoardTvWindow(_server.LocalUrl,monitor,_displays);
        _window.Closed+=(_,_)=>_window=null;
        _window.Show();
    }
    private async Task TickAsync()
    {
        if (_disposed || !await _gate.WaitAsync(0)) return;
        try
        {
            if (!_settings.Enabled && DateTimeOffset.UtcNow-_lastContact>TimeSpan.FromSeconds(30))
            { _shutdown(); return; }
            if (_companyId==0) return;
            if (DateTimeOffset.UtcNow-_permissionChecked>TimeSpan.FromSeconds(15))
            {
                await _authorization.RefreshAsync(); _permissionChecked=DateTimeOffset.UtcNow;
                _permissionAllowed=_authorization.Current.Allows("CAT_GOLD_PRICE",PermissionAction.Edit);
            }
            if (!_permissionAllowed)
            { _error="Quyền phát bảng giá đã bị thu hồi. Mở ERP để kiểm tra tài khoản."; Publish(); return; }
            await RefreshPricesAsync(); if(_server is null)StartServer(_settings); Publish();
        }
        catch (Exception ex)
        { Trace.TraceWarning("TV host refresh failed: {0}",ex.Message); _error="Không cập nhật được giá từ DB. Giá có thể đã cũ."; Publish(); }
        finally { _gate.Release(); }
    }
    private async Task RefreshPricesAsync()
    {
        _rows=await _prices.LoadAsync(_companyId); _lastSuccess=DateTimeOffset.UtcNow;
        Interlocked.Exchange(ref _lastSuccessTicks,_lastSuccess.UtcDateTime.Ticks); _error="";
    }
    private void Publish()
    {
        var stale=!string.IsNullOrWhiteSpace(_error);
        var active=!_paused && _permissionAllowed && (!stale || DateTimeOffset.UtcNow-_lastSuccess<TimeSpan.FromSeconds(60));
        var status=_paused ? "Bảng giá đang tạm dừng." : _error;
        Volatile.Write(ref _snapshot,PriceBoardTvSnapshotBuilder.Build(_settings,_rows,_companyId,active,stale,status));
        _tray.Text=_paused ? "KAZUKO · Bảng giá TV tạm dừng" : "KAZUKO · Bảng giá TV đang chạy";
    }
    private void ShowStatus()
    {
        var urls=_server is null ? "Chưa khởi động." : _settings.Enabled&&_settings.AllowLan
            ? string.Join(Environment.NewLine,_server.LanUrls) : "LAN đang tắt.";
        var message=(_paused?"Đang tạm dừng.":"Tiến trình bảng giá đang chạy độc lập với ERP.");
        if(!string.IsNullOrWhiteSpace(_error))message+=Environment.NewLine+_error;
        _tray.ShowBalloonTip(5000,"Bảng giá TV",message+Environment.NewLine+urls,Forms.ToolTipIcon.Info);
    }
    private void CopyLanAddress()
    {
        if(!_settings.Enabled||!_settings.AllowLan||_server is null||_server.LanUrls.Count==0)
            throw new InvalidOperationException("LAN đang tắt hoặc chưa có địa chỉ mạng. Mở ERP để thiết lập.");
        Clipboard.SetText(string.Join(Environment.NewLine,_server.LanUrls));
        _tray.ShowBalloonTip(3000,"Bảng giá TV","Đã chép địa chỉ LAN.",Forms.ToolTipIcon.Info);
    }
    private void RunTray(Action action)
    {
        try { action(); }
        catch (Exception ex) { _tray.ShowBalloonTip(5000,"Bảng giá TV",ex.Message,Forms.ToolTipIcon.Warning); }
    }
    private static void OpenErp()
    {
        var start=new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute=false, CreateNoWindow=true };
        if (string.Equals(System.IO.Path.GetFileNameWithoutExtension(start.FileName),"dotnet",StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(App).Assembly.Location);
        Process.Start(start)?.Dispose();
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed=true; _timer.Stop();
        _window?.Close(); _server?.Dispose(); _tray.Visible=false;
        _tray.ContextMenuStrip?.Dispose(); _tray.Dispose();
        // An in-flight IPC request may be awaiting this dispatcher: never block UI shutdown on it.
        _ = _pipe.DisposeAsync().AsTask();
    }
}
