using Kazuko.Core.Finance;

namespace Kazuko.Core.Sales;

public static class SalesCheckoutValidator
{
    public static IReadOnlyList<string> Validate(SalesCheckoutDraft draft, SalesCheckoutTotals totals, SalesValidationContext context)
    {
        var errors = new List<string>();
        if (!context.Permissions.CanPost) errors.Add("Không có quyền ghi sổ bán hàng.");
        if (draft.ClientRequestId == Guid.Empty) errors.Add("Thiếu mã yêu cầu checkout.");
        if (draft.DocumentDate == default) errors.Add("Thiếu ngày chứng từ.");
        if (draft.CustomerId <= 0) errors.Add("Phải chọn khách hàng.");
        if (draft.SalespersonId <= 0) errors.Add("Phải chọn nhân viên bán hàng.");
        if (draft.SalespersonId != context.CurrentUserId && !context.Permissions.CanChangeSalesperson)
            errors.Add("Không có quyền đổi nhân viên bán hàng.");

        var sales = draft.SaleLines ?? Array.Empty<SalesLineDraft>();
        var oldGold = draft.OldGoldLines ?? Array.Empty<OldGoldLineDraft>();
        var allocations = draft.Allocations ?? Array.Empty<PaymentAllocationDraft>();
        if (sales.Count == 0 && oldGold.Count == 0) errors.Add("Phải có ít nhất một dòng bán hoặc vàng cũ.");
        if (sales.Count > 0 && draft.SourceLocationId <= 0) errors.Add("Phải chọn kho bán hàng.");
        if (sales.Select(x => x.FinishedProductId).Distinct().Count() != sales.Count)
            errors.Add("Thành phẩm không được trùng trong checkout.");
        foreach (var line in sales)
        {
            if (line.FinishedProductId <= 0) errors.Add("Thành phẩm không hợp lệ.");
            if (line.AppliedGoldPricePerChi < 0 || line.AppliedLaborAmountVnd < 0 || line.AppliedStoneAmountVnd < 0)
                errors.Add("Giá vàng, tiền công và tiền đá không được âm.");
        }
        if (oldGold.Count > 0)
        {
            if (context.IsWalkInCustomer) errors.Add("Khách lẻ không được mua hoặc thu đổi vàng cũ.");
            if (draft.OldGoldDestinationLocationId is null or <= 0) errors.Add("Phải chọn kho nhập vàng cũ.");
        }
        foreach (var line in oldGold)
        {
            if (line.MaterialId <= 0) errors.Add("Loại vàng cũ không hợp lệ.");
            if (line.ItemQuantity < 0) errors.Add("Số lượng món không được âm.");
            if (line.TotalWeightChi < 0 || line.StoneWeightChi < 0 || line.ImpurityWeightChi < 0)
                errors.Add("Trọng lượng không được âm.");
            if (line.StoneWeightChi + line.ImpurityWeightChi > line.TotalWeightChi)
                errors.Add("Trọng lượng đá và tạp chất không được vượt tổng trọng lượng.");
            if (line.ValuePercent < 0 || line.ValuePercent > 100) errors.Add("Phần trăm giá trị phải trong khoảng 0–100.");
            if (line.AppliedBuyPricePerChi < 0) errors.Add("Giá mua vàng cũ không được âm.");
        }
        if (context.HasPriceOverride && !context.Permissions.CanOverridePrice)
            errors.Add("Không có quyền điều chỉnh giá.");
        if (draft.DiscountAmountVnd < 0 || draft.DiscountAmountVnd > totals.SaleGrossVnd)
            errors.Add("Giảm giá phải không âm và không vượt tổng bán.");
        if (draft.ReceivableAmountVnd < 0) errors.Add("Công nợ không được âm.");
        if (draft.ReceivableAmountVnd > 0)
        {
            if (context.IsWalkInCustomer) errors.Add("Khách lẻ không được phát sinh công nợ.");
            if (!context.Permissions.CanApproveDebt) errors.Add("Không có quyền phê duyệt công nợ.");
        }
        var paid = allocations.Sum(x => x.AmountVnd);
        if (totals.NetAmountVnd > 0)
        {
            if (paid + draft.ReceivableAmountVnd != totals.NetAmountVnd)
                errors.Add("Tổng thanh toán cộng công nợ phải bằng số tiền ròng.");
        }
        else
        {
            if (draft.ReceivableAmountVnd != 0) errors.Add("Không được tạo công nợ khi số tiền ròng không dương.");
            if (paid != -totals.NetAmountVnd) errors.Add("Phải phân bổ đủ số tiền chi chênh lệch.");
        }
        errors.AddRange(PaymentAllocationValidator.Validate(allocations, totals.NetAmountVnd > 0 ? totals.NetAmountVnd - draft.ReceivableAmountVnd : -totals.NetAmountVnd));
        if (totals.NetAmountVnd < 0 && allocations.Any(x => string.Equals(x.PaymentMethod?.Trim(), PaymentMethods.Card, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Không được dùng thẻ cho khoản chi.");
        return errors;
    }
}
