using System.Collections.ObjectModel;
using System.Windows.Input;
using Kazuko.Core.Mvvm;
using Kazuko.Core.Sales;
using Kazuko.Core.Production;

namespace Kazuko.Modules.Warehouse;

public sealed record ReceiptLineProgress(string Model,string Gold,int? Ordered,int Planned,int Received)
{
    public int Missing=>Math.Max(0,Planned-Received);
    public string Status=>Ordered is {} ordered&&ordered!=Planned?"Lệch đơn đặt":Received>Planned?"Vượt kế hoạch":Missing>0?"Còn thiếu":"Đủ";
}

public sealed partial class ProductionFinishedReceiptDialogViewModel
{
    private readonly ICraftOrderApplicationService? _craftOrders;
    private readonly Func<long,Task>? _openDelivery;
    private IReadOnlyList<CraftOrderLineItem> _salesLines=[];
    public ObservableCollection<ReceiptLineProgress> LineProgress{get;}=[];
    public ICommand CheckResultCommand{get;}
    public ICommand OpenDeliveryCommand{get;}

    private void RefreshLineProgress()
    {
        LineProgress.Clear();
        var remaining=_salesLines.ToList();
        foreach(var line in OrderLines)
        {
            var source=remaining.FirstOrDefault(s=>s.LineNumber==line.LineNumber&&MatchesSource(line,s))??remaining.FirstOrDefault(s=>MatchesSource(line,s));
            if(source is not null)remaining.Remove(source);
            int? ordered=_detail?.SalesOrderId is >0&&_craftOrders is not null?source?.Quantity??0:null;
            LineProgress.Add(new(line.ModelName,line.GoldTypeCode,ordered,line.PlannedQuantity,Products.Count(p=>p.OrderLineId==line.Id)));
        }
        foreach(var source in remaining)
            LineProgress.Add(new(source.ProductName,source.GoldPurity,source.Quantity,0,0));
        OnPropertyChanged(nameof(ReceiptState));
    }

    private static bool MatchesSource(ProductionOrderLineDto line,CraftOrderLineItem source)=>
        string.Equals(source.GoldPurity,line.GoldTypeCode,StringComparison.OrdinalIgnoreCase)&&
        (!string.IsNullOrWhiteSpace(source.ModelCode)&&!string.IsNullOrWhiteSpace(line.ModelCode)
            ?string.Equals(source.ModelCode,line.ModelCode,StringComparison.OrdinalIgnoreCase):source.LineNumber==line.LineNumber);

    public string BuildPostingSummary()
    {
        var lines=new List<string>{$"LSX: {SelectedOrder?.Code} • Kho: {SelectedLocation?.DisplayName}",$"Ghi sổ {Products.Count} món; tổng giá vốn {Products.Sum(p=>p.CostPrice??0):N0} VND."};
        lines.AddRange(LineProgress.Select(p=>$"{p.Model} ({p.Gold}): đơn {p.Ordered?.ToString()??"—"} / SX {p.Planned} / nhập {p.Received} — {p.Status}"));
        lines.AddRange(GoldReconcileRows.Select(r=>$"Vàng {r.PurityDisplay}: giao {r.IssuedWeight:0.####}; nhận {r.ReceivedWeight:0.####}; hao hụt {r.LossWeight:0.####}; nợ {Math.Max(0,r.DebtWeight):0.####} chỉ"));
        if(GoldReconcileRows.Any(r=>r.HasDebt))lines.Add($"Thợ chịu nợ: {SelectedWorker?.FullName??"Chưa chọn"}");
        if(GoldReconcileRows.Any(r=>r.ReceivedWeight-r.IssuedWeight>.0001m))lines.Add("CẢNH BÁO: vàng nhận vượt giao; không tự tăng tồn nguyên liệu.");
        lines.Add("Tồn thành phẩm và nợ vàng sẽ ghi cùng nhau. Phiếu đã ghi sổ chưa hỗ trợ sửa/đảo trực tiếp. Tiếp tục?");
        return string.Join(Environment.NewLine,lines);
    }

    private async Task CheckResultAsync()
    {
        if(SelectedOrder is null||_documents is null)return;
        IsBusy=true;
        try
        {
            var result=await _documents.GetByOrderAsync(SelectedOrder.Id,_session);
            if(result is not null)SetPosted(result);
            else ErrorMessage=_pendingPost is null?"Chưa tìm thấy phiếu đã ghi sổ cho LSX này.":"Chưa tìm thấy phiếu đã ghi sổ. Giữ nguyên nháp; bấm Ghi sổ nhập kho để thử lại cùng yêu cầu. Không lập phiếu khác.";
        }
        catch(Exception ex){ErrorMessage="Chưa kiểm tra được kết quả. Giữ nguyên phiếu và thử kiểm tra lại. "+ex.Message;}
        finally{IsBusy=false;NotifyReceiptCommands();}
    }

    private async Task OpenDeliveryAsync()
    {
        if(!IsPosted||_detail?.SalesOrderId is not >0||_openDelivery is null)return;
        IsBusy=true;
        try{await _openDelivery(_detail.SalesOrderId.Value);}
        catch(Exception ex){ErrorMessage="Không mở được giao hàng: "+ex.Message;}
        finally{IsBusy=false;NotifyReceiptCommands();}
    }
}
