using Kazuko.Core.Finance;

namespace Kazuko.Core.Sales;

public static class CraftOrderValidator
{
    public static decimal LineTotal(CraftOrderLineDraft line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var qty = Math.Max(0, line.Quantity);
        var gold = line.EstimatedWeightChi * line.GoldUnitPricePerChi;
        return (gold + line.LaborAmountPerUnitVnd + line.StoneAmountPerUnitVnd) * qty;
    }

    public static decimal QuotedTotal(IReadOnlyList<CraftOrderLineDraft> lines) =>
        lines?.Sum(LineTotal) ?? 0m;

    public static IReadOnlyList<string> Validate(CraftOrderDraft draft)
    {
        var errors = new List<string>();
        if (draft is null)
        {
            errors.Add("Thiếu dữ liệu đơn đặt.");
            return errors;
        }

        if (draft.CustomerId <= 0)
            errors.Add("Phải chọn khách hàng đặt hàng.");

        if (draft.OrderDate == default)
            errors.Add("Ngày đặt không hợp lệ.");

        if (draft.Lines is null || draft.Lines.Count == 0)
            errors.Add("Phải có ít nhất một dòng mẫu.");
        else
        {
            var lineNo = 0;
            foreach (var line in draft.Lines)
            {
                lineNo++;
                if (string.IsNullOrWhiteSpace(line.ModelName))
                    errors.Add($"Dòng {lineNo}: thiếu tên mẫu.");
                if (string.IsNullOrWhiteSpace(line.GoldPurity))
                    errors.Add($"Dòng {lineNo}: thiếu tuổi vàng.");
                if (line.Quantity <= 0)
                    errors.Add($"Dòng {lineNo}: số lượng phải > 0.");
                if (line.EstimatedWeightChi < 0)
                    errors.Add($"Dòng {lineNo}: trọng lượng ước tính không được âm.");
                if (line.GoldUnitPricePerChi < 0
                    || line.LaborAmountPerUnitVnd < 0
                    || line.StoneAmountPerUnitVnd < 0)
                    errors.Add($"Dòng {lineNo}: giá / tiền công / đá không được âm.");
            }
        }

        var quoted = QuotedTotal(draft.Lines ?? Array.Empty<CraftOrderLineDraft>());
        if (quoted <= 0)
            errors.Add("Tổng báo giá phải > 0.");

        var deposit = draft.DepositAllocations?.Sum(x => x.AmountVnd) ?? 0m;
        if (deposit <= 0)
            errors.Add("Đơn đặt bắt buộc thu đặt cọc > 0.");
        else if (deposit > quoted)
            errors.Add("Tiền cọc không được vượt tổng báo giá.");

        errors.AddRange(PaymentAllocationValidator.Validate(
            draft.DepositAllocations ?? Array.Empty<PaymentAllocationDraft>(),
            deposit));

        return errors;
    }

    public static IReadOnlyList<string> ValidateCollectRemaining(
        decimal remainingDueVnd,
        CraftOrderCollectDraft draft)
    {
        var errors = new List<string>();
        if (draft is null)
        {
            errors.Add("Thiếu dữ liệu thu phần còn.");
            return errors;
        }

        if (draft.SalesOrderId <= 0)
            errors.Add("Thiếu đơn đặt.");
        if (draft.PaymentDate == default)
            errors.Add("Ngày thanh toán không hợp lệ.");
        if (remainingDueVnd <= 0)
            errors.Add("Đơn không còn số tiền phải thu.");
        if (draft.ReceivableAmountVnd < 0)
            errors.Add("Số công nợ không được âm.");

        var collections = draft.CollectionAllocations ?? Array.Empty<PaymentAllocationDraft>();
        var collectSum = collections.Sum(x => x.AmountVnd);
        if (collectSum < 0)
            errors.Add("Số thu thêm không được âm.");

        if (collectSum + draft.ReceivableAmountVnd != remainingDueVnd)
        {
            errors.Add(
                $"Thu thêm ({collectSum:N0}) + công nợ ({draft.ReceivableAmountVnd:N0}) phải bằng còn lại ({remainingDueVnd:N0}).");
        }

        errors.AddRange(PaymentAllocationValidator.Validate(collections, collectSum));
        return errors;
    }

    public static IReadOnlyList<string> ValidateDeliver(
        CraftOrderDeliverDraft draft,
        decimal remainingDueVnd)
    {
        var errors = new List<string>();
        if (draft is null)
        {
            errors.Add("Thiếu dữ liệu giao hàng.");
            return errors;
        }

        if (draft.SalesOrderId <= 0)
            errors.Add("Thiếu đơn đặt.");
        if (draft.SourceLocationId <= 0)
            errors.Add("Phải chọn kho/vị trí xuất.");
        if (draft.FinishedProductIds is null || draft.FinishedProductIds.Count == 0)
            errors.Add("Phải chọn ít nhất một thành phẩm để giao.");
        else if (draft.FinishedProductIds.Any(id => id <= 0))
            errors.Add("Danh sách thành phẩm không hợp lệ.");

        if (remainingDueVnd < 0)
            errors.Add("Phần còn lại không hợp lệ.");
        else if (remainingDueVnd == 0)
        {
            if (draft.ReceivableAmountVnd != 0
                || (draft.CollectionAllocations?.Count ?? 0) > 0)
                errors.Add("Đơn đã thu đủ — không thu thêm khi giao.");
        }
        else
        {
            errors.AddRange(ValidateCollectRemaining(
                remainingDueVnd,
                new CraftOrderCollectDraft(
                    draft.SalesOrderId,
                    draft.DocumentDate,
                    draft.CollectionAllocations ?? Array.Empty<PaymentAllocationDraft>(),
                    draft.ReceivableAmountVnd,
                    draft.Notes)));
        }

        return errors;
    }
}
