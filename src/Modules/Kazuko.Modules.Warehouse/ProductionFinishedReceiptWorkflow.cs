using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.PriceBoard;
using Kazuko.Core.Production;
using Kazuko.Core.Warehouse;

namespace Kazuko.Modules.Warehouse;

public sealed partial class ProductionFinishedReceiptDialogViewModel
{
    private readonly IGoldTypeCatalogService? _goldTypes;
    private readonly IFinishedReceiptDraftStore? _draftStore;
    private readonly IFinishedReceiptDocuments? _documents;
    private readonly long? _preferredOrderId;
    private int _detailRevision;
    private bool _detailLoading,_posted,_reconcileLoaded,_closingConfirmed;
    private Guid _requestId=Guid.NewGuid();
    private PostManualFinishedReceiptRequest? _pendingPost;
    private ProductionOrderLineDto? _selectedLine;
    private ManualFinishedReceiptResult? _postedResult;
    private string _receiptNotes="";
    public ObservableCollection<ProductionOrderLineDto> OrderLines{get;}=[];
    public ProductionOrderLineDto? SelectedLine
    {
        get=>_selectedLine;
        set{if(SetProperty(ref _selectedLine,value)&&value is not null){ProductName=value.ModelName;ModelCode=value.ModelCode??"";GoldPurityCode=value.GoldTypeCode;}}
    }
    public string ReceiptNotes{get=>_receiptNotes;set=>SetProperty(ref _receiptNotes,value);}
    public bool IsDetailLoading{get=>_detailLoading;private set{SetProperty(ref _detailLoading,value);NotifyReceiptCommands();}}
    public bool IsPosted{get=>_posted;private set{SetProperty(ref _posted,value);NotifyReceiptCommands();}}
    public bool CanEdit=>!IsBusy&&!IsDetailLoading&&!IsPosted&&_pendingPost is null;
    public bool HasPendingEditor=>IsEditing||!string.IsNullOrWhiteSpace(ProductName)||!string.IsNullOrWhiteSpace(TotalWeightText)||!string.IsNullOrWhiteSpace(CostPriceText);
    public string ReceiptState=>IsPosted?$"Đã ghi sổ {_postedResult?.DocumentCode} — có thể in lại, không tạo thêm phiếu.":
        _pendingPost is not null?"Chưa rõ kết quả lần lưu trước. Bấm Lưu để xác minh/thử lại cùng yêu cầu; không nhập lại phiếu khác.":
        IsDetailLoading?"Đang tải LSX…":$"Bản nháp • Đã nhập {Products.Count}/{OrderLines.Sum(l=>l.PlannedQuantity)} món • Đối chiếu {(DebtRecorded?"đã xác nhận":"chưa xác nhận")}";
    public ICommand SaveDraftCommand{get;}

    private void NotifyReceiptCommands()
    {
        foreach(var command in new[]{AddCommand,CancelEditCommand,CancelCommand,OpenReconcileCommand,SaveDraftCommand,PrintReceiptCommand})
            (command as RelayCommand)?.NotifyCanExecuteChanged();
        foreach(var command in new[]{SaveCommand,RecordDebtCommand,PrintSelectedLabelsCommand,PrintAllLabelsCommand})
            (command as AsyncRelayCommand)?.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanEdit));OnPropertyChanged(nameof(ReceiptState));
    }
    private async Task LoadDetailAsync(ProductionFinishedReceiptOrderOption? item)
    {
        var revision=++_detailRevision;IsDetailLoading=true;_reconcileLoaded=false;_detail=null;
        try
        {
            ErrorMessage="";ImageItems.Clear();WorkerOptions.Clear();GoldReconcileRows.Clear();OrderLines.Clear();SelectedLine=null;
            SelectedImage=null;SelectedWorker=null;DebtRecorded=false;ReconcileMessage="";
            if(item is null)return;
            var detail=await _queries.GetOrderDetailAsync(item.Id,_session.BranchId);
            if(revision!=_detailRevision)return;
            if(detail is null)throw new InvalidOperationException("LSX không còn tồn tại.");
            _detail=detail;
            foreach(var line in detail.Lines)OrderLines.Add(line);
            SelectedLocation=Locations.FirstOrDefault(l=>l.Id==detail.FinishedLocationId)??Locations.FirstOrDefault();
            foreach(var image in detail.Attachments.Where(IsImageAttachment))ImageItems.Add(new(image.OriginalFileName,image.StoredPath));
            SelectedImage=ImageItems.FirstOrDefault();
            foreach(var worker in detail.Workers)WorkerOptions.Add(worker);
            _externalWorkerOption=null;
            if(detail.ExecutionType==ProductionExecutionTypes.Outsourced&&detail.ExternalWorkerId is >0)
            {
                _externalWorkerOption=new(detail.ExternalWorkerId.Value,"Thợ ngoài",detail.ExternalWorkerName??"");
                WorkerOptions.Clear();WorkerOptions.Add(_externalWorkerOption);
            }
            SelectedWorker=WorkerOptions.FirstOrDefault();
            var posted=_documents is null?null:await _documents.GetByOrderAsync(item.Id,_session);
            if(revision!=_detailRevision)return;
            if(posted is not null){SetPosted(posted);return;}
            if(detail.FinishedReceiptDocumentId is >0)throw new InvalidOperationException("LSX đã nhập kho trước phiên bản này. Xem phiếu tại danh sách Nhập kho.");
            var issued=await _queries.GetIssuedGoldByPurityAsync(item.Id,_session.BranchId);
            if(revision!=_detailRevision)return;
            var stones=await _queries.GetIssuedStoneByMaterialAsync(item.Id,_session.BranchId);
            if(revision!=_detailRevision)return;
            var draft=_draftStore?.Load(_session.BranchId,_session.UserId,item.Id);
            if(draft is not null)
            {
                if(draft.Request.ProductionOrderId!=item.Id)throw new InvalidOperationException("Bản nháp không khớp LSX.");
                _requestId=draft.Request.RequestId==Guid.Empty?Guid.NewGuid():draft.Request.RequestId;
                _pendingPost=draft.PostingStarted?draft.Request:null;
                ReceiptDate=draft.Request.ReceiptDate;ReceiptNotes=draft.Request.Notes??"";
                SelectedLocation=Locations.FirstOrDefault(l=>l.Id==draft.Request.LocationId)??SelectedLocation;
                foreach(var product in draft.Request.Items)Products.Add(product);
            }
            RebuildReconcileRows(issued);
            if(draft?.Request.GoldSettlements is {} settlements)
                foreach(var row in GoldReconcileRows){var saved=settlements.FirstOrDefault(s=>s.GoldPurityCode==row.PurityCode);if(saved is not null)row.LossWeightText=WeightDecimalInput.Format(saved.LossWeightChi);}
            IssuedStoneSummaryText="Đá đã giao: "+string.Join(", ",stones.Select(s=>$"{s.MaterialName}: {s.QuantityIssued:0.####} {s.UnitCode}"));
            _reconcileLoaded=true;
        }
        catch(Exception ex){if(revision==_detailRevision)ErrorMessage=ex.Message;}
        finally{if(revision==_detailRevision){IsDetailLoading=false;NotifyDetailChanged();OnPropertyChanged(nameof(HasImages));NotifyReceiptCommands();}}
    }

    private IReadOnlyList<ReceiptGoldSettlement> ReadSettlements()=>GoldReconcileRows.Select(r=>new ReceiptGoldSettlement(r.PurityCode??"",r.LossWeight,
        SelectedWorker?.EmployeeId,ReferenceEquals(SelectedWorker,_externalWorkerOption)?WorkerGoldDebtWorkerTypes.ExternalWorker:WorkerGoldDebtWorkerTypes.Employee)).ToArray();

    private void PrepareSettlement()
    {
        ReconcileMessage="";
        try
        {
            if(!_reconcileLoaded||SelectedOrder is null)throw new InvalidOperationException("Chưa tải đủ dữ liệu đối chiếu. Đóng và mở lại LSX để thử lại.");
            foreach(var row in GoldReconcileRows)
            {
                if(!string.IsNullOrWhiteSpace(row.LossWeightText)&&(!WeightDecimalInput.TryParse(row.LossWeightText,out var loss)||loss<0))
                    throw new InvalidOperationException("Hao hụt phải là số không âm.");
                if(row.LossWeight>Math.Max(row.IssuedWeight-row.ReceivedWeight,0))throw new InvalidOperationException("Hao hụt vượt chênh lệch vàng giao/nhận.");
            }
            if(GoldReconcileRows.Any(r=>r.HasDebt)&&SelectedWorker is null)throw new InvalidOperationException("Chọn thợ chịu nợ vàng.");
            DebtRecorded=true;
            ReconcileMessage="Đã xác nhận đối chiếu. Công nợ chỉ được ghi khi lưu phiếu nhập kho thành công.";
            if(GoldReconcileRows.Any(r=>r.ReceivedWeight-r.IssuedWeight>.0001m))
                ReconcileMessage+=" Cảnh báo: vàng trong thành phẩm vượt lượng đã giao; cần kiểm tra chênh lệch. Phần vượt không tự nhập vào kho nguyên liệu.";
        }
        catch(Exception ex){DebtRecorded=false;ReconcileMessage=ex.Message;}
        NotifyReceiptCommands();
    }
    private PostManualFinishedReceiptRequest BuildRequest()=>new(ReceiptDate.Date,SelectedLocation?.Id??0,SelectedOrder?.Id,Products.ToArray(),
        ReceiptNotes,_requestId,ReadSettlements());

    private void SaveDraft()
    {
        if(_draftStore is null)throw new InvalidOperationException("Chưa cấu hình lưu nháp.");
        if(SelectedOrder is null)throw new InvalidOperationException("Chọn LSX trước khi lưu nháp.");
        if(HasPendingEditor)throw new InvalidOperationException("Thêm/cập nhật hoặc hủy món đang gõ trước khi lưu nháp.");
        _draftStore.Save(_session.BranchId,_session.UserId,SelectedOrder.Id,new(_pendingPost??BuildRequest(),SelectedLine?.Id,_pendingPost is not null));
        ErrorMessage="Đã lưu nháp trên máy này. Chưa thay đổi tồn kho hoặc nợ vàng.";
    }
    public bool CanClose()
    {
        if(IsBusy||IsDetailLoading)return false;
        if(_closingConfirmed||IsPosted)return true;
        if(HasPendingEditor){ErrorMessage="Thêm/cập nhật hoặc hủy món đang nhập trước khi đóng.";return false;}
        if(Products.Count>0)
        {
            var answer=MessageBox.Show("Lưu nháp nhập kho trên máy này trước khi đóng?","Đóng phiếu nhập",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
            if(answer==MessageBoxResult.Cancel)return false;
            try
            {
                if(answer==MessageBoxResult.Yes)SaveDraft();
                else if(_pendingPost is not null){ErrorMessage="Lần lưu trước chưa rõ kết quả: cần giữ nháp để xác minh.";return false;}
                else if(SelectedOrder is not null)_draftStore?.Delete(_session.BranchId,_session.UserId,SelectedOrder.Id);
            }
            catch(Exception ex){ErrorMessage=ex.Message;return false;}
        }
        _closingConfirmed=true;return true;
    }
    private async Task PostReceiptAsync()
    {
        ErrorMessage="";
        try
        {
            if(_pendingPost is null)
            {
                if(HasPendingEditor)throw new InvalidOperationException("Thêm/cập nhật hoặc hủy món đang gõ trước khi ghi sổ.");
                if(!_reconcileLoaded||!DebtRecorded)throw new InvalidOperationException("Mở đối chiếu vàng và xác nhận trước khi lưu.");
                if(SelectedOrder is null||_detail is null||SelectedLocation is null)throw new InvalidOperationException("Chọn LSX và kho nhập.");
                foreach(var item in Products)FinishedReceiptValidation.ValidateItem(item);
                FinishedReceiptValidation.ValidateQuantities(_detail.Lines.ToDictionary(l=>l.Id,l=>l.PlannedQuantity),Products);
                if(MessageBox.Show($"Ghi sổ toàn bộ {Products.Count} món của {SelectedOrder.Code}? Tồn kho và nợ vàng sẽ được lưu cùng nhau.","Xác nhận nhập kho",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
                _pendingPost=BuildRequest();
                SaveDraft(); // Durable request ID exists before network/commit.
            }
            IsBusy=true;
            var result=await _application.PostManualFinishedReceiptAsync(_pendingPost,_session);
            SetPosted(result);
            try{_draftStore?.Delete(_session.BranchId,_session.UserId,SelectedOrder!.Id);}catch(Exception ex){ErrorMessage="Đã ghi sổ; chưa xóa được nháp cục bộ: "+ex.Message;}
        }
        catch(InvalidOperationException ex){_pendingPost=null;ErrorMessage=ex.Message;}
        catch(Exception ex){ErrorMessage="Chưa xác minh được kết quả. Giữ nguyên phiếu và bấm Lưu để thử lại an toàn. "+ex.Message;}
        finally{IsBusy=false;NotifyReceiptCommands();}
    }
    private void SetPosted(ManualFinishedReceiptResult result)
    {
        if(result.PostedRequest is null)throw new InvalidOperationException("Phiếu nhập thiếu dữ liệu đã lưu.");
        _postedResult=result;_pendingPost=null;Products.Clear();
        foreach(var product in result.PostedRequest.Items)Products.Add(product);
        ReceiptDate=result.PostedRequest.ReceiptDate;ReceiptNotes=result.PostedRequest.Notes??"";IsPosted=true;
        ErrorMessage="Đã nhập kho. Có thể in phiếu/tem hoặc đóng cửa sổ.";
    }
    private void PrintPostedReceipt()
    {
        try{if(_postedResult is not null)FinishedReceiptPrintService.Print(_postedResult,SelectedOrder?.Code??"",SelectedLocation?.DisplayName??"");}
        catch(Exception ex){ErrorMessage="In phiếu lỗi; dữ liệu đã lưu, có thể in lại: "+ex.Message;}
    }
}
