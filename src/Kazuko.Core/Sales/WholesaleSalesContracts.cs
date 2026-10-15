using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;

namespace Kazuko.Core.Sales;

public sealed record WholesaleLocationOption(
    long Id,
    string Code,
    string Name,
    string InventoryScope,
    bool IsDefault);

public sealed record WholesaleProductOption(
    long FinishedProductId,
    string ProductCode,
    string ProductName,
    long SourceLocationId,
    string LocationName,
    string GoldPurity,
    long RowVersion,
    decimal TotalWeightChi,
    decimal StoneWeightChi,
    decimal GoldWeightChi,
    decimal SellPricePerChi,
    decimal LaborCost,
    decimal NonGoldMaterialCost,
    decimal LineTotalVnd);

public sealed record WholesaleSaleDraft(
    DateTime DocumentDate,
    long CustomerId,
    long SalespersonId,
    long SourceLocationId,
    IReadOnlyList<long> FinishedProductIds,
    IReadOnlyList<PaymentAllocationDraft> Allocations,
    decimal ReceivableAmountVnd,
    string? Notes);

public sealed record WholesaleSaleResult(
    long SalesOrderId,
    string DocumentCode,
    long IssueDocumentId,
    decimal GrandTotalVnd,
    decimal PaidAmountVnd,
    decimal ReceivableAmountVnd);

public interface IWholesaleSalesApplicationService
{
    Task<IReadOnlyList<WholesaleLocationOption>> GetSourceLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<WholesaleProductOption?> FindAvailableProductAsync(
        string productCode,
        long? sourceLocationId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WholesaleProductOption>> SearchAvailableProductsAsync(
        string searchText,
        long? sourceLocationId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    Task<WholesaleSaleResult> PostAsync(
        WholesaleSaleDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);
}

public static class WholesaleSaleValidator
{
    public static IReadOnlyList<string> Validate(
        WholesaleSaleDraft draft,
        decimal trustedGrandTotalVnd,
        bool isWalkInCustomer,
        bool canPostWholesale,
        bool canApproveDebt,
        bool canChangeSalesperson,
        long currentUserId)
    {
        var errors = new List<string>();
        if (!canPostWholesale)
            errors.Add("Không có quyền ghi sổ bán sỉ.");
        if (draft.CustomerId <= 0)
            errors.Add("Phải chọn khách hàng.");
        if (draft.SalespersonId <= 0)
            errors.Add("Phải chọn nhân viên bán hàng.");
        if (draft.SourceLocationId <= 0)
            errors.Add("Phải chọn kho xuất.");
        if (draft.FinishedProductIds is null || draft.FinishedProductIds.Count == 0)
            errors.Add("Phải chọn ít nhất một thành phẩm.");
        if (draft.FinishedProductIds is not null
            && draft.FinishedProductIds.Count != draft.FinishedProductIds.Distinct().Count())
            errors.Add("Không được chọn trùng serial trong cùng phiếu bán sỉ.");
        if (trustedGrandTotalVnd <= 0)
            errors.Add("Tổng tiền bán sỉ phải lớn hơn 0.");

        var paid = draft.Allocations?.Sum(x => x.AmountVnd) ?? 0m;
        if (paid < 0 || draft.ReceivableAmountVnd < 0)
            errors.Add("Số tiền thu và công nợ không được âm.");
        if (paid + draft.ReceivableAmountVnd != trustedGrandTotalVnd)
            errors.Add("Tổng thu + công nợ phải bằng tổng tiền phiếu.");
        if (draft.ReceivableAmountVnd > 0)
        {
            if (isWalkInCustomer)
                errors.Add("Không ghi công nợ cho khách lẻ / vãng lai.");
            if (!canApproveDebt)
                errors.Add("Không có quyền duyệt công nợ bán hàng.");
        }

        if (draft.SalespersonId != currentUserId && !canChangeSalesperson)
            errors.Add("Không có quyền đổi nhân viên bán hàng.");

        return errors;
    }
}
