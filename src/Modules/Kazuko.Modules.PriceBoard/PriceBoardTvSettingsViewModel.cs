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
    private readonly PriceBoardTvService _service;
    private bool _busy;
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
    public TvMonitor? SelectedMonitor{get;set;}
    public TvGoldSelection? SelectedGold{get;set;}
    public IReadOnlyList<TvMonitor> Monitors{get;}=TvMonitor.GetAll();
    public IReadOnlyList<TvChoice> Alignments{get;}=[new("left","Căn trái"),new("center","Căn giữa"),new("right","Căn phải")];
    public IReadOnlyList<TvChoice> Units{get;}=[new("CHI","VND / chỉ"),new("LUONG","VND / lượng")];
    public IReadOnlyList<TvColor> Colors{get;}=[new("#FFFF00","Vàng"),new("#702539","Đỏ rượu"),new("#FFFFFF","Trắng"),new("#000000","Đen"),new("#FF0000","Đỏ"),new("#00FF00","Xanh lá"),new("#00FFFF","Xanh ngọc"),new("#FF8000","Cam"),new("#0000FF","Xanh dương"),new("#C0C0C0","Xám")];
    public ObservableCollection<TvGoldSelection> GoldRows{get;}=[];
    public string PreviewUrl{get=>_preview;private set=>SetProperty(ref _preview,value);}
    public string LanAddresses{get=>_addresses;private set=>SetProperty(ref _addresses,value);}
    public string Error{get=>_error;private set=>SetProperty(ref _error,value);}
    public string Message{get=>_message;private set=>SetProperty(ref _message,value);}
    public bool IsBusy{get=>_busy;private set{SetProperty(ref _busy,value);NotifyCommands();}}
    public ICommand ApplyCommand{get;}public ICommand OpenHdmiCommand{get;}public ICommand CopyUrlCommand{get;}
    public ICommand MoveUpCommand{get;}public ICommand MoveDownCommand{get;}
    public PriceBoardTvSettingsViewModel(PriceBoardTvService service)
    {
        _service=service;
        ApplyCommand=new AsyncRelayCommand(()=>Run(ApplyAsync),()=>!IsBusy);
        OpenHdmiCommand=new AsyncRelayCommand(()=>Run(OpenHdmiAsync),()=>!IsBusy);
        CopyUrlCommand=new RelayCommand(()=>{try{Clipboard.SetText(LanAddresses.Split(Environment.NewLine)[0]);Message="Đã chép địa chỉ TV.";}catch(Exception ex){Error=ex.Message;}},()=>!IsBusy&&LanAddresses.StartsWith("http",StringComparison.Ordinal));
        MoveUpCommand=new RelayCommand(()=>Move(-1),()=>!IsBusy);MoveDownCommand=new RelayCommand(()=>Move(1),()=>!IsBusy);
        _service.Changed+=ServiceChanged;
    }
    public async Task InitializeAsync()=>await Run(async()=>
    {
        await _service.InitializeAsync();var s=_service.Settings;
        Enabled=s.Enabled;AllowLan=s.AllowLan;PortText=s.Port.ToString(CultureInfo.InvariantCulture);RowCountText=s.RowsPerPage.ToString(CultureInfo.InvariantCulture);
        ShopName=s.ShopName;Ticker=s.Ticker;TitleColor=s.TitleColor;BackgroundColor=s.BackgroundColor;BorderColor=s.BorderColor;PriceColor=s.PriceColor;Alignment=s.Alignment;UnitCode=s.UnitCode;
        SelectedMonitor=Monitors.FirstOrDefault(x=>x.Device==s.MonitorDevice)??Monitors.FirstOrDefault(x=>!x.Primary)??Monitors.FirstOrDefault();
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
    private async Task OpenHdmiAsync()
    {
        Enabled=true;OnPropertyChanged(nameof(Enabled));await ApplyAsync();
        _service.OpenHdmi(SelectedMonitor??throw new InvalidOperationException("Chưa chọn màn hình TV."));
        Message="Đã mở bảng giá trên màn hình đã chọn. Nhấn Esc hoặc Alt+F4 trên bảng giá để đóng.";
    }
    private void Move(int delta){if(SelectedGold is null)return;var index=GoldRows.IndexOf(SelectedGold);var next=index+delta;if(next>=0&&next<GoldRows.Count)GoldRows.Move(index,next);}
    private void ServiceChanged(){PreviewUrl=_service.LocalUrl;LanAddresses=_service.LanAddresses;NotifyCommands();}
    private async Task Run(Func<Task> action){if(IsBusy)return;IsBusy=true;Error="";Message="";try{await action();}catch(Exception ex){Error=ex.Message;}finally{IsBusy=false;}}
    private void NotifyCommands(){(ApplyCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();(OpenHdmiCommand as AsyncRelayCommand)?.NotifyCanExecuteChanged();(CopyUrlCommand as RelayCommand)?.NotifyCanExecuteChanged();}
    public void Dispose()=>_service.Changed-=ServiceChanged;
}
