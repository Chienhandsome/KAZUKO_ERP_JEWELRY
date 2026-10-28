using System.Globalization;

namespace Kazuko.Core.Production;

public static class FinishedReceiptValidation
{
    public static void ValidateItem(ManualFinishedProductItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.ProductName) || item.ProductName.Length > 200)
            throw new InvalidOperationException("Tên sản phẩm bắt buộc, tối đa 200 ký tự.");
        if (item.TotalWeight <= 0 || item.GoldWeight <= 0 || item.StoneWeight < 0 ||
            Math.Abs(item.TotalWeight-item.GoldWeight-item.StoneWeight) > .0001m)
            throw new InvalidOperationException($"{item.ProductName}: tổng trọng lượng phải bằng vàng + đá (sai số tối đa 0,0001 chỉ).");
        if (string.IsNullOrWhiteSpace(item.GoldPurityCode))
            throw new InvalidOperationException("Chọn loại vàng từ danh mục.");
        if(item.WeightUnitCode is not ("Chỉ" or "CHI"))throw new InvalidOperationException("Phiếu nhập thành phẩm dùng đơn vị chỉ.");
        if(new[]{item.TotalWeight,item.GoldWeight,item.StoneWeight}.Any(x=>decimal.Round(x,4)!=x))
            throw new InvalidOperationException("Trọng lượng tối đa 4 chữ số thập phân.");
        if (item.CostPrice is null or <= 0)
            throw new InvalidOperationException($"{item.ProductName}: nhập giá vốn lớn hơn 0; chưa áp dụng tự phân bổ giá vốn LSX.");
        if (new[]{item.SalePrice,item.CraftLaborCost,item.SaleLaborCost,item.PurchaseLaborCost}.Any(x=>x<0))
            throw new InvalidOperationException("Giá và tiền công không được âm.");
        if(new[]{item.CostPrice,item.SalePrice,item.CraftLaborCost,item.SaleLaborCost,item.PurchaseLaborCost}.Any(x=>x is {} n&&(decimal.Truncate(n)!=n||n>9999999999999999m)))
            throw new InvalidOperationException("Tiền VND phải là số nguyên trong giới hạn lưu trữ.");
        if ((item.PurchaseLaborCost??item.CraftLaborCost??0)>item.CostPrice)
            throw new InvalidOperationException("Công nhập không được vượt tổng giá vốn.");
    }

    public static bool TryMoney(string? text,out decimal value)
    {
        value=0;
        if(string.IsNullOrWhiteSpace(text))return false;
        var raw=text.Trim();
        if(raw.All(char.IsAsciiDigit))return decimal.TryParse(raw,NumberStyles.None,CultureInfo.InvariantCulture,out value);
        var parts=raw.Split('.');
        if(parts.Length<2||parts[0].Length is <1 or >3||parts.Skip(1).Any(p=>p.Length!=3)||parts.Any(p=>!p.All(char.IsAsciiDigit)))return false;
        return decimal.TryParse(string.Concat(parts),NumberStyles.None,CultureInfo.InvariantCulture,out value);
    }

    public static void ValidateQuantities(IReadOnlyDictionary<long,int> planned,IReadOnlyList<ManualFinishedProductItem> items)
    {
        if(items.Any(i=>i.OrderLineId is null||!planned.ContainsKey(i.OrderLineId.Value)))
            throw new InvalidOperationException("Mỗi món phải chọn đúng dòng mẫu của LSX.");
        var counts=items.GroupBy(i=>i.OrderLineId!.Value).ToDictionary(g=>g.Key,g=>g.Count());
        if(planned.Any(p=>counts.GetValueOrDefault(p.Key)!=p.Value))
            throw new InvalidOperationException("Phải nhập đủ số món của từng mẫu trong một phiếu. Kiểm tra kế hoạch và số đã nhập.");
    }
}

public interface IProductionFinishedReceiptPresenter { bool Show(long? orderId = null); }
public interface IFinishedReceiptDocuments
{
    Task<ManualFinishedReceiptResult?> GetByOrderAsync(long orderId,Kazuko.Core.Authentication.AuthenticatedSession session,CancellationToken cancellationToken=default);
}
public sealed record FinishedReceiptDraft(PostManualFinishedReceiptRequest Request, long? SelectedLineId, bool PostingStarted=false);
public interface IFinishedReceiptDraftStore
{
    FinishedReceiptDraft? Load(long branchId,long userId,long orderId);
    void Save(long branchId,long userId,long orderId,FinishedReceiptDraft draft);
    void Delete(long branchId,long userId,long orderId);
}
