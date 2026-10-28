using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Authentication;
using Kazuko.Core.Mvvm;
using Kazuko.Core.UserManagement;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed class StocktakeTabViewModel : ObservableObject
{
    private readonly IStocktakeService _service;
    private readonly ICurrentSessionAccessor _session;
    private readonly IAuthorizationService? _permissions;
    private readonly Func<string,string,bool> _confirm;
    private StocktakeDetail? _detail;
    private StocktakeSummary? _selected;
    private StocktakeLineRow? _selectedLine;
    private WarehouseLocationOption? _location;
    private bool _busy,_dirty,_suppressSelection,_editorDirty,_syncEditor;
    private string _notes="",_message="",_error="",_scan="",_quantity="",_lineNotes="",_search="";
    private DateTime _date=DateTime.Today;
    private Guid _requestId=Guid.NewGuid();

    public StocktakeTabViewModel(IStocktakeService service,ICurrentSessionAccessor session,IAuthorizationService? permissions=null,
        Func<string,string,bool>? confirm=null)
    {
        _service=service;_session=session;_permissions=permissions;
        _confirm=confirm??((message,title)=>MessageBox.Show(message,title,MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes);
        RefreshCommand=new AsyncRelayCommand(()=>Run(RefreshAsync),()=>!IsBusy);
        NewCommand=new RelayCommand(New,()=>!IsBusy&&Allowed(PermissionAction.Add));
        CreateCommand=new AsyncRelayCommand(()=>Run(CreateAsync),()=>!IsBusy&&_detail is null&&SelectedLocation is not null&&Allowed(PermissionAction.Add));
        SaveCommand=new AsyncRelayCommand(()=>Run(SaveAsync),()=>CanEdit);
        ScanCommand=new AsyncRelayCommand(()=>Run(ScanAsync),()=>CanEdit&&IsSerial);
        ReviewCommand=new AsyncRelayCommand(()=>Run(ReviewAsync),()=>CanEdit);
        CloseCommand=new AsyncRelayCommand(()=>Run(CloseAsync),()=>CanEdit);
        ApplyLineCommand=new RelayCommand(ApplyLine,()=>CanEdit&&SelectedLine is not null);
        FoundCommand=new RelayCommand(()=>Mark(1),()=>CanEdit&&IsSerial&&SelectedLine is not null);
        MissingCommand=new RelayCommand(()=>Mark(0),()=>CanEdit&&IsSerial&&SelectedLine is not null);
        ZeroUnenteredCommand=new RelayCommand(ZeroUnentered,()=>CanEdit&&!IsSerial);
    }

    public ObservableCollection<StocktakeSummary> Counts {get;}=[];
    public ObservableCollection<WarehouseLocationOption> Locations {get;}=[];
    public ObservableCollection<StocktakeLineRow> Lines {get;}=[];
    public ObservableCollection<StocktakeLineRow> VisibleLines {get;}=[];
    public ICommand RefreshCommand{get;} public ICommand NewCommand{get;} public ICommand CreateCommand{get;}
    public ICommand SaveCommand{get;} public ICommand ScanCommand{get;} public ICommand ReviewCommand{get;} public ICommand CloseCommand{get;}
    public ICommand ApplyLineCommand{get;} public ICommand FoundCommand{get;} public ICommand MissingCommand{get;} public ICommand ZeroUnenteredCommand{get;}
    public bool IsBusy {get=>_busy;private set{SetProperty(ref _busy,value);Notify();}}
    public bool CanEdit=>!IsBusy&&_detail?.Status=="DRAFT"&&Allowed(PermissionAction.Edit);
    public bool IsNew=>_detail is null;
    public bool IsSerial=>_detail?.Scope=="FINISHED";
    public string Title=>_detail is null?"Lập phiếu kiểm kê":$"{_detail.Code} · {(_detail.Status=="DRAFT"?"Bản nháp":"Đã chốt kết quả")} · {_detail.LocationName}";
    public string HeaderInfo=>_detail is null?"Chọn kho và ngày để chụp tồn hệ thống.":$"Ngày {_detail.CountDate:dd/MM/yyyy} · Người lập: {_detail.CreatedByName} · {(IsSerial?"Đếm từng serial":"Đếm nguyên vật liệu theo đơn vị chuẩn")}";
    public string Summary=>$"{Lines.Count} dòng · {Lines.Count(x=>x.Model.NeedsRecount)} cần kiểm lại · {Lines.Count(x=>x.Model.Difference is not null && x.Model.Difference!=0)} chênh lệch{(_dirty||_editorDirty?" · Có thay đổi chưa lưu":"")}";
    public string Notes {get=>_notes;set{if(SetProperty(ref _notes,value)&&_detail is not null)_dirty=true;OnPropertyChanged(nameof(Summary));}}
    public string Message{get=>_message;private set=>SetProperty(ref _message,value);}
    public string Error{get=>_error;private set=>SetProperty(ref _error,value);}
    public string ScanText{get=>_scan;set=>SetProperty(ref _scan,value);}
    public string QuantityText{get=>_quantity;set{if(SetProperty(ref _quantity,value)&&!_syncEditor&&SelectedLine is not null)_editorDirty=true;OnPropertyChanged(nameof(Summary));}}
    public string LineNotes{get=>_lineNotes;set{if(SetProperty(ref _lineNotes,value)&&!_syncEditor&&SelectedLine is not null)_editorDirty=true;OnPropertyChanged(nameof(Summary));}}
    public string SearchText{get=>_search;set{SetProperty(ref _search,value);Filter();}}
    public DateTime CountDate{get=>_date;set=>SetProperty(ref _date,value);}
    public WarehouseLocationOption? SelectedLocation{get=>_location;set{SetProperty(ref _location,value);Notify();}}
    public StocktakeSummary? SelectedCount
    {
        get=>_selected;
        set
        {
            if(_suppressSelection||ReferenceEquals(_selected,value))return;
            if(IsBusy||!CanDiscard()){OnPropertyChanged();return;}
            SetProperty(ref _selected,value);
            if(value is not null)_=Run(async()=>SetDetail(await _service.GetAsync(value.Id,Session())));
        }
    }
    public StocktakeLineRow? SelectedLine
    {
        get=>_selectedLine;
        set
        {
            if(_editorDirty)
            {
                try{CommitLine();}catch(Exception ex){Error=ex.Message;OnPropertyChanged();return;}
            }
            SetProperty(ref _selectedLine,value);
            _syncEditor=true;
            QuantityText=value?.Model.CountedQuantity?.ToString("0.####",CultureInfo.GetCultureInfo("vi-VN"))??"";
            LineNotes=value?.Model.Notes??"";_syncEditor=false;Notify();
        }
    }
    private bool Allowed(PermissionAction action)=>_permissions?.Current.Allows("INV_STOCKTAKE",action)??true;
    private AuthenticatedSession Session()=>_session.Current??throw new InvalidOperationException("Chưa đăng nhập.");
    private bool CanDiscard()=>!(_dirty||_editorDirty)||_confirm("Phiếu có thay đổi chưa lưu. Bỏ các thay đổi này?","Kiểm kê");

    public async Task InitializeAsync()
    {
        if(Locations.Count==0&&!IsBusy)await Run(RefreshAsync);
    }
    private async Task Run(Func<Task> action)
    {
        if(IsBusy)return;
        IsBusy=true;Error="";Message="";
        try{await action();}catch(Exception ex){Error=ex.Message;}finally{IsBusy=false;}
    }
    private async Task RefreshAsync()
    {
        if(!CanDiscard())return;
        var locations=await _service.GetLocationsAsync(Session());
        Locations.Clear();foreach(var l in locations)Locations.Add(l);
        SelectedLocation=Locations.FirstOrDefault(x=>x.Id==_detail?.LocationId)??Locations.FirstOrDefault();
        await RefreshList();
        if(_detail is not null)SetDetail(await _service.GetAsync(_detail.Id,Session()));
    }
    private async Task RefreshList()
    {
        var rows=await _service.ListAsync(Session());
        _suppressSelection=true;
        Counts.Clear();foreach(var row in rows)Counts.Add(row);
        _selected=Counts.FirstOrDefault(x=>x.Id==_detail?.Id);OnPropertyChanged(nameof(SelectedCount));
        _suppressSelection=false;
    }
    private void New()
    {
        if(!CanDiscard())return;
        _detail=null;_dirty=false;_editorDirty=false;_notes="";_requestId=Guid.NewGuid();_selected=null;
        Lines.Clear();VisibleLines.Clear();SelectedLine=null;Error="";Message="";CountDate=DateTime.Today;
        OnPropertyChanged(nameof(Notes));OnPropertyChanged(nameof(SelectedCount));Notify();
    }
    private async Task CreateAsync()
    {
        if(SelectedLocation is null)throw new InvalidOperationException("Hãy chọn kho.");
        SetDetail(await _service.CreateAsync(new(_requestId,CountDate,SelectedLocation.Id,Notes),Session()));
        await RefreshList();Message="Đã chụp tồn hệ thống. Nhập số thực tế hoặc quét serial; lưu nháp để tiếp tục sau.";
    }
    private async Task SaveAsync()
    {
        if(_detail is null)return;
        if(_editorDirty)CommitLine();
        var selected=SelectedLine?.Model.ItemKey;
        SetDetail(await _service.SaveAsync(new(_detail.Id,_detail.Revision,Notes,
            Lines.Select(x=>new StocktakeCountEntry(x.Model.Id,x.Model.CountedQuantity,x.Model.Notes,x.Confirmed)).ToArray()),Session()));
        SelectedLine=Lines.FirstOrDefault(x=>x.Model.ItemKey==selected);
        await RefreshList();Message="Đã lưu bản nháp.";
    }
    private async Task ScanAsync()
    {
        var scan=ScanText.Trim();if(scan.Length==0)throw new InvalidOperationException("Chưa nhập mã serial.");
        if(_dirty||_editorDirty)await SaveAsync();
        SetDetail(await _service.ScanAsync(_detail!.Id,_detail.Revision,scan,Session()));
        ScanText="";Message=$"Đã ghi nhận {scan}. Quét lặp không tăng số món.";
        await RefreshList();
    }
    private async Task ReviewAsync()
    {
        if(!_confirm("Đối chiếu với tồn hiện tại? Dòng có biến động sẽ bị xóa số đếm cũ và cần kiểm lại. Các dòng khác giữ kết quả.","Đối chiếu tồn"))return;
        if(_dirty||_editorDirty)await SaveAsync();
        SetDetail(await _service.ReviewAsync(_detail!.Id,_detail.Revision,Session()));
        await RefreshList();Message="Đã đối chiếu. Hãy kiểm lại các dòng Cần kiểm lại trước khi chốt.";
    }
    private async Task CloseAsync()
    {
        if(!_confirm("Chốt kết quả kiểm kê? Serial chưa thấy sẽ ghi nhận là thiếu. Phiếu đã chốt không sửa được; thao tác này chưa điều chỉnh tồn kho.","Chốt kiểm kê"))return;
        if(_dirty||_editorDirty)await SaveAsync();
        SetDetail(await _service.CloseAsync(_detail!.Id,_detail.Revision,Session()));
        await RefreshList();Message="Đã chốt kết quả kiểm kê. Tồn kho chưa thay đổi.";
    }
    private void ApplyLine()
    {
        try{CommitLine();}catch(Exception ex){Error=ex.Message;}
    }
    private void CommitLine()
    {
            if(SelectedLine is null)return;
            decimal? quantity=string.IsNullOrWhiteSpace(QuantityText)?null:
                QuantityDecimalInput.TryParse(QuantityText,out var parsed)?parsed:
                    throw new InvalidOperationException("Số thực tế không hợp lệ (ví dụ 1.250,5).");
            StocktakeValidation.ValidateQuantity(SelectedLine.Model.Kind,quantity);
            if(LineNotes.Length>500)throw new InvalidOperationException("Ghi chú dòng tối đa 500 ký tự.");
            SelectedLine.Change(quantity,LineNotes);_dirty=true;_editorDirty=false;Error="";OnPropertyChanged(nameof(Summary));
    }
    private void Mark(decimal quantity)
    {
        if(SelectedLine is null)return;
        QuantityText=quantity.ToString(CultureInfo.GetCultureInfo("vi-VN"));ApplyLine();
    }
    private void ZeroUnentered()
    {
        if(!_confirm("Ghi nhận số thực tế bằng 0 cho tất cả ô NVL chưa nhập?","Xác nhận số đếm"))return;
        foreach(var line in Lines.Where(x=>x.Model.CountedQuantity is null))line.Change(0,line.Model.Notes);
        _dirty=true;OnPropertyChanged(nameof(Summary));
    }
    private void SetDetail(StocktakeDetail detail)
    {
        _detail=detail;_notes=detail.Notes;_dirty=false;_editorDirty=false;
        Lines.Clear();foreach(var line in detail.Lines)Lines.Add(new(line));
        Filter();SelectedLine=null;OnPropertyChanged(nameof(Notes));Notify();
    }
    private void Filter()
    {
        VisibleLines.Clear();foreach(var line in Lines.Where(x=>string.IsNullOrWhiteSpace(SearchText)||
            (x.Model.Code+" "+x.Model.Name+" "+x.Model.Finding).Contains(SearchText,StringComparison.CurrentCultureIgnoreCase)))VisibleLines.Add(line);
    }
    private void Notify()
    {
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(IsNew));OnPropertyChanged(nameof(IsSerial));OnPropertyChanged(nameof(Title));OnPropertyChanged(nameof(HeaderInfo));OnPropertyChanged(nameof(Summary));
        foreach(var command in new[]{RefreshCommand,CreateCommand,SaveCommand,ScanCommand,ReviewCommand,CloseCommand})
            (command as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        foreach(var command in new[]{NewCommand,ApplyLineCommand,FoundCommand,MissingCommand,ZeroUnenteredCommand})
            (command as RelayCommand)?.NotifyCanExecuteChanged();
    }
}

public sealed class StocktakeLineRow(StocktakeLine model) : ObservableObject
{
    public StocktakeLine Model{get;private set;}=model;
    public bool Confirmed{get;private set;}
    public void Change(decimal? quantity,string notes)
    {
        Confirmed=quantity is not null;
        Model=Model with {CountedQuantity=quantity,Notes=notes,NeedsRecount=quantity is null&&Model.NeedsRecount};
        OnPropertyChanged(nameof(Model));
    }
}
