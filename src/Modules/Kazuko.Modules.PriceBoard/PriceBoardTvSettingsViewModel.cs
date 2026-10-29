using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;

namespace Kazuko.Modules.PriceBoard;

public sealed class TvGoldSelection(long id,string name,bool visible) : ObservableObject
{
    private bool _visible=visible;
    public long Id{get;}=id;public string Name{get;}=name;
    public bool Visible{get=>_visible;set=>SetProperty(ref _visible,value);}
}
public sealed record TvChoice(string Value,string Name);
public sealed record TvColor(string Value,string Name);

public sealed class PriceBoardTvSettingsViewModel : ObservableObject,IDisposable
{
    private readonly IPriceBoardTvSettingsService _service;
    private readonly ITvDisplayService _displays;
    private bool _busy;
    private bool _refreshingMonitors;
    private bool _disposed;
    private TvMonitor? _selectedMonitor;
    private string _displayMessage="Chọn TV qua Miracast hoặc kết nối HDMI, rồi chọn màn hình đích bên dưới.";
    private string _error="",_message="",_preview="",_addresses="";
    public bool Enabled{get;set;}public bool AllowLan{get;set;}
    public string PortText{get;set;}="8878";public string RowCountText{get;set;}="8";
    public string ShopName{get;set;}="";public string Ticker{get;set;}="";
    private string _titleColor="#FFFF00",_backgroundColor="#702539",_borderColor="#FFFFFF",_priceColor="#FFFF00";
    public string TitleColor{get=>_titleColor;set=>SetProperty(ref _titleColor,value);}
    public string BackgroundColor{get=>_backgroundColor;set=>SetProperty(ref _backgroundColor,value);}
    public string BorderColor{get=>_borderColor;set=>SetProperty(ref _borderColor,value);}
    public string PriceColor{get=>_priceColor;set=>SetProperty(ref _priceColor,value);}
    public string Alignment{get;set;}="center";public string UnitCode{get;set;}="CHI";
    public TvMonitor? SelectedMonitor
    {
        get=>_selectedMonitor;
        set
        {
            if(_refreshingMonitors)return;
            if(SetProperty(ref _selectedMonitor,value))NotifyCommands();
        }
    }
    public string DisplayConnectionMessage{get=>_displayMessage;private set=>SetProperty(ref _displayMessage,value);}
    public TvGoldSelection? SelectedGold{get;set;}
    public ObservableCollection<TvMonitor> Monitors{get;}=[];
    public IReadOnlyList<TvChoice> Alignments{get;}=[new("left","Căn trái"),new("center","Căn giữa"),new("right","Căn phải")];
    public IReadOnlyList<TvChoice> Units{get;}=[new("CHI","VND / chỉ"),new("LUONG","VND / lượng")];
    public IReadOnlyList<TvColor> Colors{get;}=[new("#FFFF00","Vàng"),new("#702539","Đỏ rượu"),new("#FFFFFF","Trắng"),new("#000000","Đen"),new("#FF0000","Đỏ"),new("#00FF00","Xanh lá"),new("#00FFFF","Xanh ngọc"),new("#FF8000","Cam"),new("#0000FF","Xanh dương"),new("#C0C0C0","Xám")];
    public ObservableCollection<TvGoldSelection> GoldRows{get;}=[];
    public string PreviewUrl{get=>_preview;private set=>SetProperty(ref _preview,value);}
    public string LanAddresses{get=>_addresses;private set=>SetProperty(ref _addresses,value);}
    public string BackgroundStatus=>_service.BackgroundStatus;
    public string Error{get=>_error;private set=>SetProperty(ref _error,value);}
    public string Message{get=>_message;private set=>SetProperty(ref _message,value);}
    public bool IsBusy{get=>_busy;private set{SetProperty(ref _busy,value);NotifyCommands();}}
    public ICommand ApplyCommand{get;}public ICommand OpenDisplayCommand{get;}public ICommand CopyUrlCommand{get;}
    public ICommand ConnectMiracastCommand{get;}public ICommand ProjectionModeCommand{get;}public ICommand RefreshMonitorsCommand{get;}
    public ICommand MoveUpCommand{get;}public ICommand MoveDownCommand{get;}
    public PriceBoardTvSettingsViewModel(IPriceBoardTvSettingsService service,ITvDisplayService displays)
    {
        _service=service;_displays=displays;
        ApplyCommand=new AsyncRelayCommand(()=>Run(ApplyAsync),()=>!IsBusy);
        OpenDisplayCommand=new AsyncRelayCommand(()=>Run(OpenDisplayAsync),()=>!IsBusy&&SelectedMonitor is not null);
        ConnectMiracastCommand=new RelayCommand(()=>RunDisplayAction(()=>
        {
            _displays.ShowMiracastDevicePicker();
            SelectedMonitor=null;
            DisplayConnectionMessage="Chọn đúng TV trong danh sách Cast của Windows và chờ kết nối. Sau đó chọn màn hình đích bên dưới.";
        }),()=>!IsBusy);
        ProjectionModeCommand=new RelayCommand(()=>RunDisplayAction(()=>
        {
            _displays.ShowProjectionModePicker();
            DisplayConnectionMessage="Chọn Mở rộng (Extend) trong Windows để TV hiển thị riêng, rồi chọn màn hình TV bên dưới.";
        }),()=>!IsBusy);
        RefreshMonitorsCommand=new RelayCommand(()=>RunDisplayAction(RefreshMonitors),()=>!IsBusy);
        CopyUrlCommand=new RelayCommand(()=>{try{Clipboard.SetText(LanAddresses.Split(Environment.NewLine)[0]);Message="Đã chép địa chỉ TV.";}catch(Exception ex){Error=ex.Message;}},()=>!IsBusy&&LanAddresses.StartsWith("http",StringComparison.Ordinal));
        MoveUpCommand=new RelayCommand(()=>Move(-1),()=>!IsBusy);MoveDownCommand=new RelayCommand(()=>Move(1),()=>!IsBusy);
        _service.Changed+=ServiceChanged;
    }
    public async Task InitializeAsync()=>await Run(async()=>
    {
        await _service.InitializeAsync();var s=_service.Settings;
        Enabled=s.Enabled;AllowLan=s.AllowLan;PortText=s.Port.ToString(CultureInfo.InvariantCulture);RowCountText=s.RowsPerPage.ToString(CultureInfo.InvariantCulture);
        ShopName=s.ShopName;Ticker=s.Ticker;TitleColor=s.TitleColor;BackgroundColor=s.BackgroundColor;BorderColor=s.BorderColor;PriceColor=s.PriceColor;Alignment=s.Alignment;UnitCode=s.UnitCode;
        RefreshMonitors();
        SelectedMonitor=Monitors.FirstOrDefault(x=>x.Device==s.MonitorDevice);
        var choices=(s.GoldOptions??[]).ToDictionary(x=>x.Id);
        GoldRows.Clear();foreach(var row in _service.Prices.Where(x=>x.IsActive).OrderBy(x=>choices.TryGetValue(x.Id,out var option)?option.Order:x.DisplayOrder).ThenBy(x=>x.Id))
            GoldRows.Add(new(row.Id,row.Name,!choices.TryGetValue(row.Id,out var option)||option.Visible));
        foreach(var name in new[]{nameof(Enabled),nameof(AllowLan),nameof(PortText),nameof(RowCountText),nameof(ShopName),nameof(Ticker),nameof(TitleColor),nameof(BackgroundColor),nameof(BorderColor),nameof(PriceColor),nameof(Alignment),nameof(UnitCode),nameof(SelectedMonitor)})OnPropertyChanged(name);
        ServiceChanged();
    });
    private PriceBoardTvSettings ReadSettings()
    {
        if(!int.TryParse(PortText,NumberStyles.None,CultureInfo.InvariantCulture,out var port)||!int.TryParse(RowCountText,NumberStyles.None,CultureInfo.InvariantCulture,out var rows))
            throw new InvalidOperationException("Cổng và số dòng mỗi trang phải là số nguyên.");
        return new(Enabled,AllowLan,port,ShopName.Trim(),Ticker,TitleColor,BackgroundColor,BorderColor,PriceColor,Alignment,UnitCode,rows,
            GoldRows.Select((x,i)=>new TvGoldOption(x.Id,x.Visible,i)).ToArray(),SelectedMonitor?.Device??"");
    }
    private async Task ApplyAsync(){await _service.ApplyAsync(ReadSettings());Message="Đã áp dụng. TV cập nhật trong khoảng 3 giây.";ServiceChanged();}
    private TvMonitor RequireConnectedMonitor()
    {
        var selected=SelectedMonitor??throw new InvalidOperationException("Hãy chọn màn hình TV trước khi chiếu.");
        var connected=_displays.GetMonitors().FirstOrDefault(x=>x.Device==selected.Device);
        if(connected is null)
        {
            RefreshMonitors();
            throw new InvalidOperationException("Màn hình đã ngắt kết nối. Hãy kết nối lại và chọn màn hình TV.");
        }
        return connected;
    }
    private async Task OpenDisplayAsync()
    {
        RequireConnectedMonitor();
        Enabled=true;OnPropertyChanged(nameof(Enabled));await ApplyAsync();
        await _service.OpenDisplayAsync(RequireConnectedMonitor());
        Message="Đã mở bảng giá trên màn hình đã chọn. Nhấn Esc hoặc Alt+F4 trên bảng giá để đóng.";
    }
    public void RefreshMonitors()
    {
        if(_disposed)return;
        var monitors=_displays.GetMonitors();
        if(Monitors.SequenceEqual(monitors))return;
        var selected=SelectedMonitor?.Device;
        _refreshingMonitors=true;
        try{Monitors.Clear();foreach(var monitor in monitors)Monitors.Add(monitor);}
        finally{_refreshingMonitors=false;}
        SelectedMonitor=Monitors.FirstOrDefault(x=>x.Device==selected);
        DisplayConnectionMessage=selected is not null&&SelectedMonitor is null
            ?"Màn hình đã chọn vừa ngắt kết nối. Hãy chọn lại TV trước khi chiếu."
            :"Danh sách màn hình đã cập nhật. Chọn đúng màn hình TV để chiếu; nếu chưa thấy TV, kiểm tra kết nối và chế độ Mở rộng (Extend).";
    }
    private void RunDisplayAction(Action action)
    {
        if(_disposed)return;
        Error="";
        Message="";
        try{action();}catch(Exception ex){Error=ex.Message;}
    }
    private void Move(int delta){if(SelectedGold is null)return;var index=GoldRows.IndexOf(SelectedGold);var next=index+delta;if(next>=0&&next<GoldRows.Count)GoldRows.Move(index,next);}
    private void ServiceChanged(){PreviewUrl=_service.LocalUrl;LanAddresses=_service.LanAddresses;OnPropertyChanged(nameof(BackgroundStatus));NotifyCommands();}
    private async Task Run(Func<Task> action){if(IsBusy)return;IsBusy=true;Error="";Message="";try{await action();}catch(Exception ex){Error=ex.Message;}finally{IsBusy=false;}}
    private void NotifyCommands()
    {
        (ApplyCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();(OpenDisplayCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();(CopyUrlCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ConnectMiracastCommand as RelayCommand)?.NotifyCanExecuteChanged();(ProjectionModeCommand as RelayCommand)?.NotifyCanExecuteChanged();(RefreshMonitorsCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }
    public void Dispose(){_disposed=true;_service.Changed-=ServiceChanged;}
}
