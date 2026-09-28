using Kazuko.Core.Finance;
using Kazuko.Core.Inventory;

namespace Kazuko.Core.Warehouse;

public static class MaterialReceiptValidator
{
    public static IReadOnlyList<string> Validate(MaterialReceiptDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();

        var source = (draft.SourceType ?? string.Empty).Trim().ToUpperInvariant();
        if (source == StockSourceTypes.Supplier)
        {
            if (draft.SupplierId is null or <= 0)
                errors.Add("Phải chọn nhà cung cấp.");
            if (draft.CustomerId is > 0)
                errors.Add("Không được chọn đồng thời NCC và khách hàng.");
        }
        else if (source == StockSourceTypes.OldGoldCustomer)
        {
            if (draft.CustomerId is null or <= 0)
                errors.Add("Phải chọn khách hàng bán vàng cũ.");
            if (draft.SupplierId is > 0)
                errors.Add("Không được chọn đồng thời NCC và khách hàng.");
        }
        else
        {
            errors.Add("Nguồn nhập không hợp lệ (SUPPLIER hoặc OLD_GOLD_CUSTOMER).");
        }

        if (draft.DestinationLocationId <= 0)
            errors.Add("Phải chọn kho nhận.");

        if (draft.Lines is null || draft.Lines.Count == 0)
            errors.Add("Phiếu nhập phải có ít nhất một dòng NVL.");

        decimal total = 0;
        var lineNo = 0;
        foreach (var line in draft.Lines ?? Array.Empty<MaterialReceiptLineDraft>())
        {
            lineNo++;
            if (line.MaterialId <= 0)
                errors.Add($"Dòng {lineNo}: thiếu mã NVL.");
            if (line.Quantity <= 0)
                errors.Add($"Dòng {lineNo}: số lượng phải > 0.");
            if (line.UnitPrice < 0)
                errors.Add($"Dòng {lineNo}: đơn giá không được âm.");
            if (line.ExchangeRate <= 0)
                errors.Add($"Dòng {lineNo}: tỷ giá phải > 0.");

            var amount = Math.Round(line.Quantity * line.UnitPrice * line.ExchangeRate, 2, MidpointRounding.AwayFromZero);
            total += amount;
        }

        if (draft.PaidAmountVnd < 0)
            errors.Add("Số đã thanh toán không được âm.");
        if (draft.PaidAmountVnd > total)
            errors.Add("Số đã thanh toán không được vượt tổng tiền phiếu.");

        errors.AddRange(PaymentAllocationValidator.Validate(draft.Payments ?? Array.Empty<PaymentAllocationDraft>(), draft.PaidAmountVnd));
        return errors;
    }

    public static decimal ComputeLineAmountVnd(MaterialReceiptLineDraft line) =>
        Math.Round(line.Quantity * line.UnitPrice * line.ExchangeRate, 2, MidpointRounding.AwayFromZero);

    public static decimal ComputeTotalAmountVnd(IEnumerable<MaterialReceiptLineDraft> lines) =>
        lines.Sum(ComputeLineAmountVnd);
}

public static class OutsourcedCraftIssueValidator
{
    public static IReadOnlyList<string> Validate(OutsourcedCraftIssueDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (draft.ExternalWorkerId <= 0)
            errors.Add("Phải chọn thợ ngoài.");
        if (draft.SourceLocationId <= 0)
            errors.Add("Phải chọn kho xuất.");
        if (draft.Lines is null || draft.Lines.Count == 0)
            errors.Add("Phải có ít nhất một dòng NVL.");

        var lineNo = 0;
        foreach (var line in draft.Lines ?? Array.Empty<OutsourcedCraftIssueLineDraft>())
        {
            lineNo++;
            if (line.MaterialId <= 0)
                errors.Add($"Dòng {lineNo}: thiếu NVL.");
            if (line.Quantity <= 0)
                errors.Add($"Dòng {lineNo}: số lượng phải > 0.");
        }

        return errors;
    }
}

public static class CraftSettlementValidator
{
    public static IReadOnlyList<string> Validate(
        CraftReturnReceiptDraft draft,
        IReadOnlyDictionary<long, decimal> outstandingByHoldingId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();

        if (draft.SourceIssueDocumentId <= 0)
            errors.Add("Phải liên kết phiếu xuất thợ.");
        if (draft.DestinationLocationId <= 0)
            errors.Add("Phải chọn kho nhận.");
        if (draft.LaborAmountVnd < 0)
            errors.Add("Tiền công không được âm.");
        if (draft.PaidLaborAmountVnd < 0 || draft.PaidLaborAmountVnd > draft.LaborAmountVnd)
            errors.Add("Số tiền công đã trả không hợp lệ.");

        errors.AddRange(PaymentAllocationValidator.Validate(
            draft.LaborPayments ?? Array.Empty<PaymentAllocationDraft>(),
            draft.PaidLaborAmountVnd));

        if (draft.SettlementLines is null || draft.SettlementLines.Count == 0)
            errors.Add("Phải có dòng quyết toán.");

        var lineNo = 0;
        decimal totalInProduct = 0;
        foreach (var line in draft.SettlementLines ?? Array.Empty<CraftSettlementLineDraft>())
        {
            lineNo++;
            if (line.HoldingId <= 0)
                errors.Add($"Dòng {lineNo}: thiếu holding.");
            if (line.QuantityInProduct < 0 || line.QuantityReturned < 0 || line.QuantityScrap < 0 || line.AllowedLoss < 0)
                errors.Add($"Dòng {lineNo}: số lượng không được âm.");

            totalInProduct += line.QuantityInProduct;
            var consumed = line.QuantityInProduct + line.QuantityReturned + line.QuantityScrap;
            if (!outstandingByHoldingId.TryGetValue(line.HoldingId, out var outstanding))
            {
                errors.Add($"Dòng {lineNo}: holding không thuộc phiếu xuất.");
                continue;
            }

            if (consumed > outstanding && !draft.AllowOverReturn)
                errors.Add($"Dòng {lineNo}: thu hồi vượt lượng đang giao ({outstanding}).");
        }

        var outputs = draft.OutputLines ?? Array.Empty<CraftOutputLineDraft>();
        if (totalInProduct > 0 && outputs.Count == 0)
            errors.Add("Có NVL cấu thành sản phẩm nhưng thiếu dòng thành phẩm đầu ra.");

        var outNo = 0;
        foreach (var output in outputs)
        {
            outNo++;
            if (output.ProductionOrderId is null or <= 0 && output.SalesOrderId is null or <= 0)
                errors.Add($"Thành phẩm dòng {outNo}: bắt buộc có lệnh chế tác hoặc đơn hàng nguồn.");
            if (output.FinishedProductId is null or <= 0
                && string.IsNullOrWhiteSpace(output.ProductCode)
                && string.IsNullOrWhiteSpace(output.ProductName))
                errors.Add($"Thành phẩm dòng {outNo}: thiếu mã/tên khi tạo mới.");
            if (output.Quantity <= 0)
                errors.Add($"Thành phẩm dòng {outNo}: số lượng phải > 0.");
            if (output.DestinationLocationId <= 0)
                errors.Add($"Thành phẩm dòng {outNo}: thiếu kho nhận.");
            if (output.MaterialValueVnd < 0 || output.AllocatedLaborCostVnd < 0 || output.TotalCostVnd < 0)
                errors.Add($"Thành phẩm dòng {outNo}: giá trị không được âm.");
            var expected = Math.Round(output.MaterialValueVnd + output.AllocatedLaborCostVnd, 2, MidpointRounding.AwayFromZero);
            if (output.TotalCostVnd != expected)
                errors.Add($"Thành phẩm dòng {outNo}: tổng giá vốn phải = NVL + tiền công ({expected:N0}).");
        }

        return errors;
    }

    public static decimal ComputeLoss(decimal issuedOutstanding, decimal inProduct, decimal returned, decimal scrap) =>
        issuedOutstanding - inProduct - returned - scrap;

    public static decimal ComputeVariance(decimal loss, decimal allowedLoss) =>
        loss - allowedLoss;
}

public static class FinishedGoodsIssueValidator
{
    public static IReadOnlyList<string> Validate(FinishedGoodsIssueDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();
        if (draft.SalesOrderId <= 0)
            errors.Add("Phải chọn đơn đặt chế tác.");
        if (draft.SourceLocationId <= 0)
            errors.Add("Phải chọn kho/vị trí xuất.");
        if (draft.Lines is null || draft.Lines.Count == 0)
            errors.Add("Phải chọn ít nhất một thành phẩm đã hoàn thành.");

        var lineNo = 0;
        foreach (var line in draft.Lines ?? Array.Empty<FinishedGoodsIssueLineDraft>())
        {
            lineNo++;
            if (line.FinishedProductId <= 0)
                errors.Add($"Dòng {lineNo}: thiếu thành phẩm.");
        }

        return errors;
    }

    public static bool IsConfirmedSalesOrderStatus(string? statusCode)
    {
        var status = (statusCode ?? string.Empty).Trim().ToUpperInvariant();
        return status is not ("DRAFT" or "CANCELLED") && status.Length > 0;
    }

    public static bool RejectManualFinishedReceipt(string documentType, string? purposeCode, long? productionOrderId)
    {
        var type = (documentType ?? string.Empty).Trim().ToUpperInvariant();
        var purpose = (purposeCode ?? string.Empty).Trim().ToUpperInvariant();
        if (type == StockDocumentTypes.Receipt && purpose == "FINISHED_RECEIPT" && productionOrderId is null or <= 0)
            return true;
        if (type == StockDocumentTypes.Receipt && purpose.Length == 0 && productionOrderId is null)
            return false;
        return false;
    }

    public static void EnsureNotManualFinishedGoodsReceipt(
        string documentType,
        string? purposeCode,
        long? productionOrderId,
        bool isMaterialOnlyReceipt)
    {
        if (!isMaterialOnlyReceipt)
            throw new InvalidOperationException("Không được nhập thành phẩm thủ công. Thành phẩm chỉ tăng từ lệnh chế tác hoặc phiếu nhập chế tác.");

        if (RejectManualFinishedReceipt(documentType, purposeCode, productionOrderId))
            throw new InvalidOperationException("Không được tạo phiếu nhập thành phẩm khi thiếu lệnh chế tác hợp lệ.");
    }

    public static void EnsureMaterialsAreRaw(IEnumerable<string> materialTypes)
    {
        foreach (var type in materialTypes)
        {
            if (!MaterialTypes.IsRawMaterial(type))
                throw new InvalidOperationException($"Mặt hàng loại '{type}' không phải NVL — không nhập bằng phiếu nhập NVL.");
        }
    }
}
