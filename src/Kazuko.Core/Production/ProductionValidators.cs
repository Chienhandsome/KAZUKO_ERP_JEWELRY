namespace Kazuko.Core.Production;

public static class ProductionOrderDraftValidator
{
    public static IReadOnlyList<string> Validate(ProductionOrderDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();

        var source = Normalize(draft.OrderSource);
        if (source == ProductionOrderSources.Stock)
        {
            if (draft.SalesOrderId is > 0)
                errors.Add("Lệnh nhập kho không được gắn đơn bán.");
        }
        else if (source == ProductionOrderSources.SalesOrder)
        {
            if (draft.SalesOrderId is null or <= 0)
                errors.Add("Lệnh theo đơn bán phải chọn đúng một đơn bán đã xác nhận.");
        }
        else
        {
            errors.Add("Nguồn lệnh không hợp lệ (STOCK hoặc SALES_ORDER).");
        }

        var execution = Normalize(draft.ExecutionType);
        if (execution == ProductionExecutionTypes.InHouse)
        {
            if (draft.ExternalWorkerId is > 0)
                errors.Add("Lệnh nội bộ không được chọn thợ ngoài.");
            if (draft.EmployeeIds is null || draft.EmployeeIds.Count == 0)
                errors.Add("Lệnh nội bộ phải chọn ít nhất một nhân viên đang làm việc.");
            else if (draft.EmployeeIds.Any(id => id <= 0))
                errors.Add("Danh sách nhân viên không hợp lệ.");
            else if (draft.EmployeeIds.Distinct().Count() != draft.EmployeeIds.Count)
                errors.Add("Không được chọn trùng nhân viên.");
        }
        else if (execution == ProductionExecutionTypes.Outsourced)
        {
            if (draft.ExternalWorkerId is null or <= 0)
                errors.Add("Lệnh thợ ngoài phải chọn đúng một thợ đang hoạt động.");
            if (draft.EmployeeIds is { Count: > 0 })
                errors.Add("Lệnh thợ ngoài không được gắn nhân viên nội bộ.");
        }
        else
        {
            errors.Add("Hình thức thực hiện không hợp lệ (IN_HOUSE hoặc OUTSOURCED).");
        }

        if (draft.SourceLocationId <= 0)
            errors.Add("Phải chọn kho xuất nguyên liệu.");
        if (draft.FinishedLocationId <= 0)
            errors.Add("Phải chọn kho thành phẩm nhận hàng.");
        if (draft.SourceLocationId > 0 && draft.SourceLocationId == draft.FinishedLocationId)
            errors.Add("Kho xuất NVL và kho thành phẩm phải khác nhau.");

        if (draft.Lines is null || draft.Lines.Count == 0)
            errors.Add("Lệnh phải có ít nhất một dòng mẫu.");

        var lineNos = new HashSet<int>();
        var lineIndex = 0;
        foreach (var line in draft.Lines ?? Array.Empty<ProductionOrderLineDraft>())
        {
            lineIndex++;
            if (line.LineNumber <= 0)
                errors.Add($"Dòng mẫu {lineIndex}: số thứ tự phải > 0.");
            else if (!lineNos.Add(line.LineNumber))
                errors.Add($"Trùng số thứ tự dòng mẫu {line.LineNumber}.");

            if (string.IsNullOrWhiteSpace(line.ModelName))
                errors.Add($"Dòng mẫu {lineIndex}: thiếu tên mẫu.");
            if (string.IsNullOrWhiteSpace(line.GoldTypeCode) || string.IsNullOrWhiteSpace(line.GoldTypeName))
                errors.Add($"Dòng mẫu {lineIndex}: thiếu loại vàng.");
            if (line.PlannedQuantity < 1)
                errors.Add($"Dòng mẫu {lineIndex}: số lượng phải ≥ 1.");
            if (line.LaborCostPerUnit < 0)
                errors.Add($"Dòng mẫu {lineIndex}: tiền công mỗi món không được âm.");
        }

        var attachmentIndex = 0;
        foreach (var attachment in draft.Attachments ?? Array.Empty<ProductionAttachmentDraft>())
        {
            attachmentIndex++;
            if (attachment.MarkDeleted)
                continue;

            var kind = Normalize(attachment.AttachmentKind);
            if (kind is not (ProductionAttachmentKinds.Design or ProductionAttachmentKinds.CustomerRequirement))
                errors.Add($"Đính kèm {attachmentIndex}: loại không hợp lệ.");
            if (string.IsNullOrWhiteSpace(attachment.OriginalFileName))
                errors.Add($"Đính kèm {attachmentIndex}: thiếu tên file.");
            if (string.IsNullOrWhiteSpace(attachment.StagingPath) && string.IsNullOrWhiteSpace(attachment.StoredPath))
                errors.Add($"Đính kèm {attachmentIndex}: thiếu đường dẫn file.");
            if (attachment.LineNumber is int lineNo && lineNo > 0 && !lineNos.Contains(lineNo))
                errors.Add($"Đính kèm {attachmentIndex}: dòng mẫu {lineNo} không tồn tại.");
        }

        return errors;
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();
}

public static class ProductionMaterialBalanceValidator
{
    public static bool IsBalanced(
        decimal quantityIssued,
        decimal quantityInProduct,
        decimal quantityReturned,
        decimal quantityScrap,
        decimal quantityLoss) =>
        quantityIssued >= 0
        && quantityInProduct >= 0
        && quantityReturned >= 0
        && quantityScrap >= 0
        && quantityLoss >= 0
        && quantityIssued == quantityInProduct + quantityReturned + quantityScrap + quantityLoss;

    public static IReadOnlyList<string> ValidateReturnLines(
        IReadOnlyList<ProductionReturnBalanceLineDraft> lines,
        IReadOnlyDictionary<long, decimal> issuedByBalanceId)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(issuedByBalanceId);
        var errors = new List<string>();

        if (lines.Count == 0)
            errors.Add("Phải nhập đối soát cho toàn bộ nguyên liệu đã giao.");

        var seen = new HashSet<long>();
        var index = 0;
        foreach (var line in lines)
        {
            index++;
            if (line.MaterialBalanceId <= 0)
            {
                errors.Add($"Dòng đối soát {index}: thiếu balance.");
                continue;
            }

            if (!seen.Add(line.MaterialBalanceId))
                errors.Add($"Dòng đối soát {index}: trùng balance.");

            if (!issuedByBalanceId.TryGetValue(line.MaterialBalanceId, out var issued))
            {
                errors.Add($"Dòng đối soát {index}: không thuộc lệnh hoặc chưa giao.");
                continue;
            }

            if (line.QuantityInProduct < 0 || line.QuantityReturned < 0
                || line.QuantityScrap < 0 || line.QuantityLoss < 0)
            {
                errors.Add($"Dòng đối soát {index}: số lượng không được âm.");
                continue;
            }

            if (!IsBalanced(
                    issued,
                    line.QuantityInProduct,
                    line.QuantityReturned,
                    line.QuantityScrap,
                    line.QuantityLoss))
            {
                errors.Add(
                    $"Dòng đối soát {index}: chưa cân bằng (đã giao phải = vào TP + trả lại + phế liệu + hao hụt).");
            }
        }

        foreach (var balanceId in issuedByBalanceId.Keys)
        {
            if (!seen.Contains(balanceId))
                errors.Add($"Thiếu đối soát cho balance {balanceId}.");
        }

        return errors;
    }
}

public static class ProductionMaterialIssueValidator
{
    public static IReadOnlyList<string> Validate(ProductionMaterialIssueDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var errors = new List<string>();

        if (draft.ProductionOrderId <= 0)
            errors.Add("Thiếu lệnh sản xuất.");
        if (draft.ExpectedRowVersion < 0)
            errors.Add("Row version không hợp lệ.");
        if (draft.Lines is null || draft.Lines.Count == 0)
            errors.Add("Phải khai ít nhất một dòng nguyên liệu.");

        var lineNo = 0;
        var materialIds = new HashSet<long>();
        foreach (var line in draft.Lines ?? Array.Empty<ProductionMaterialIssueLineDraft>())
        {
            lineNo++;
            if (line.MaterialId <= 0)
                errors.Add($"Dòng {lineNo}: thiếu NVL.");
            else if (!materialIds.Add(line.MaterialId))
                errors.Add($"Dòng {lineNo}: trùng NVL trong cùng phiếu giao.");
            if (line.Quantity <= 0)
                errors.Add($"Dòng {lineNo}: số lượng phải > 0.");
        }

        return errors;
    }
}

public static class ProductionQcValidator
{
    public static IReadOnlyList<string> Validate(
        ProductionQcDraft draft,
        string currentUnitStatus,
        Func<long, decimal?> maxInProductByBalanceId)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(maxInProductByBalanceId);
        var errors = new List<string>();

        if (draft.QcUnitId <= 0)
            errors.Add("Thiếu đơn vị QC.");

        var status = (currentUnitStatus ?? string.Empty).Trim().ToUpperInvariant();
        if (status == ProductionQcUnitStatuses.Passed)
            errors.Add("Đơn vị đã đạt QC; muốn điều chỉnh phải tạo attempt điều chỉnh có audit.");
        if (status is not (ProductionQcUnitStatuses.Pending or ProductionQcUnitStatuses.ReworkRequired or ProductionQcUnitStatuses.Passed))
            errors.Add("Trạng thái đơn vị QC không hợp lệ.");

        var result = (draft.ResultCode ?? string.Empty).Trim().ToUpperInvariant();
        if (result == ProductionQcResultCodes.Fail)
        {
            if (string.IsNullOrWhiteSpace(draft.DefectNotes))
                errors.Add("QC không đạt phải nhập lý do lỗi.");
        }
        else if (result == ProductionQcResultCodes.Pass)
        {
            if (draft.ActualGoldWeight is null or <= 0)
                errors.Add("QC đạt phải nhập trọng lượng vàng thực tế > 0.");
            if (string.IsNullOrWhiteSpace(draft.ActualWeightUnitCode))
                errors.Add("QC đạt phải có đơn vị trọng lượng.");
            if (string.IsNullOrWhiteSpace(draft.GoldPurityCode))
                errors.Add("QC đạt phải có tuổi vàng.");
            if (draft.Components is null || draft.Components.Count == 0)
                errors.Add("QC đạt phải phân bổ cấu thành nguyên liệu.");
            if (draft.Images is null || draft.Images.Count == 0)
                errors.Add("QC đạt phải có ít nhất một ảnh sản phẩm.");
        }
        else
        {
            errors.Add("Kết quả QC không hợp lệ (PASS hoặc FAIL).");
        }

        var componentIndex = 0;
        var usedByBalance = new Dictionary<long, decimal>();
        foreach (var component in draft.Components ?? Array.Empty<ProductionQcComponentDraft>())
        {
            componentIndex++;
            if (component.MaterialBalanceId <= 0)
                errors.Add($"Cấu thành {componentIndex}: thiếu balance.");
            if (component.QuantityUsed <= 0)
                errors.Add($"Cấu thành {componentIndex}: số lượng phải > 0.");

            var kind = (component.ComponentKind ?? string.Empty).Trim().ToUpperInvariant();
            if (kind is not (
                ProductionComponentKinds.Gold or
                ProductionComponentKinds.Stone or
                ProductionComponentKinds.Accessory or
                ProductionComponentKinds.Other))
            {
                errors.Add($"Cấu thành {componentIndex}: loại không hợp lệ.");
            }

            if (component.MaterialBalanceId > 0)
            {
                usedByBalance.TryGetValue(component.MaterialBalanceId, out var prior);
                usedByBalance[component.MaterialBalanceId] = prior + component.QuantityUsed;
            }
        }

        foreach (var pair in usedByBalance)
        {
            var max = maxInProductByBalanceId(pair.Key);
            if (max is null)
                errors.Add($"Cấu thành vượt balance không thuộc lệnh ({pair.Key}).");
            else if (pair.Value > max.Value)
                errors.Add($"Tổng phân bổ balance {pair.Key} vượt lượng vào thành phẩm.");
        }

        return errors;
    }
}

public static class ProductionCompletionValidator
{
    public static IReadOnlyList<string> Validate(
        string orderStatus,
        bool hasMaterialIssue,
        bool hasWorkerReturn,
        IReadOnlyList<ProductionQcUnitDto> units,
        int plannedQuantityTotal,
        IReadOnlyList<ProductionMaterialBalanceDto> balances)
    {
        var errors = new List<string>();
        var status = (orderStatus ?? string.Empty).Trim().ToUpperInvariant();

        if (status == ProductionOrderStatuses.Completed)
            return errors;

        if (status != ProductionOrderStatuses.InProgress)
            errors.Add("Chỉ hoàn tất lệnh đang làm.");
        if (!hasMaterialIssue)
            errors.Add("Chưa ghi sổ giao nguyên liệu.");
        if (!hasWorkerReturn)
            errors.Add("Chưa ghi nhận nhận hàng chính.");
        if (units.Count != plannedQuantityTotal)
            errors.Add("Số đơn vị QC không khớp tổng số lượng đặt làm.");
        if (units.Any(u => !string.Equals(u.StatusCode, ProductionQcUnitStatuses.Passed, StringComparison.OrdinalIgnoreCase)))
            errors.Add("Mọi món phải đạt QC trước khi hoàn tất.");

        foreach (var balance in balances)
        {
            if (!ProductionMaterialBalanceValidator.IsBalanced(
                    balance.QuantityIssued,
                    balance.QuantityInProduct,
                    balance.QuantityReturned,
                    balance.QuantityScrap,
                    balance.QuantityLoss))
            {
                errors.Add($"Đối soát NVL '{balance.MaterialCode}' chưa cân bằng.");
            }
        }

        return errors;
    }
}

public static class ProductionCostCalculator
{
    public sealed record ComponentCostInput(
        string ComponentKind,
        decimal QuantityUsed,
        decimal UnitCostSnapshot,
        decimal? GoldPricePerChiSnapshot);

    public sealed record UnitCostResult(
        decimal GoldMaterialCost,
        decimal NonGoldMaterialCost,
        decimal LaborCost,
        decimal TotalCost,
        decimal GoldWeight);

    public static UnitCostResult Calculate(
        IEnumerable<ComponentCostInput> components,
        decimal laborCostPerUnit,
        decimal? actualGoldWeight = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        if (laborCostPerUnit < 0)
            throw new ArgumentOutOfRangeException(nameof(laborCostPerUnit));

        decimal gold = 0;
        decimal nonGold = 0;
        decimal goldWeight = 0;

        foreach (var component in components)
        {
            if (component.QuantityUsed < 0)
                throw new InvalidOperationException("Số lượng cấu thành không được âm.");

            var kind = (component.ComponentKind ?? string.Empty).Trim().ToUpperInvariant();
            if (kind == ProductionComponentKinds.Gold)
            {
                var price = component.GoldPricePerChiSnapshot ?? component.UnitCostSnapshot;
                gold += RoundMoney(component.QuantityUsed * price);
                goldWeight += component.QuantityUsed;
            }
            else
            {
                nonGold += RoundMoney(component.QuantityUsed * component.UnitCostSnapshot);
            }
        }

        if (actualGoldWeight is > 0)
            goldWeight = actualGoldWeight.Value;

        var labor = RoundMoney(laborCostPerUnit);
        var total = RoundMoney(gold + nonGold + labor);
        return new UnitCostResult(gold, nonGold, labor, total, goldWeight);
    }

    public static decimal RoundMoney(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
