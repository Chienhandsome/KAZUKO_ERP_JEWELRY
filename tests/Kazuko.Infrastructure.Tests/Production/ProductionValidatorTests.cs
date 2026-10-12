using Kazuko.Core.Production;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Production;

public sealed class ProductionValidatorTests
{
    [Fact]
    public void Draft_RejectsSalesOrderWithoutSalesId()
    {
        var draft = ValidStockDraft() with
        {
            OrderSource = ProductionOrderSources.SalesOrder,
            SalesOrderId = null
        };

        var errors = ProductionOrderDraftValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("đơn đặt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draft_RejectsStockWithSalesId()
    {
        var draft = ValidStockDraft() with { SalesOrderId = 9 };
        var errors = ProductionOrderDraftValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("không được gắn đơn bán", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draft_InHouseRequiresEmployees_RejectsExternalWorker()
    {
        var draft = ValidStockDraft() with
        {
            ExecutionType = ProductionExecutionTypes.InHouse,
            ExternalWorkerId = 3,
            EmployeeIds = []
        };

        var errors = ProductionOrderDraftValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("nhân viên", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("thợ ngoài", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draft_OutsourcedRequiresSingleWorker_RejectsEmployees()
    {
        var draft = ValidStockDraft() with
        {
            ExecutionType = ProductionExecutionTypes.Outsourced,
            ExternalWorkerId = null,
            EmployeeIds = [1, 2]
        };

        var errors = ProductionOrderDraftValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("thợ", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("nhân viên nội bộ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draft_RejectsNonPositivePlannedQuantityAndNegativeLabor()
    {
        var draft = ValidStockDraft() with
        {
            Lines =
            [
                new ProductionOrderLineDraft(null, 1, "N1", "Nhẫn", "9999", "Vàng 9999", 0, -1, 0, 0, 0, null, null)
            ]
        };

        var errors = ProductionOrderDraftValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("số lượng", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, e => e.Contains("tiền công", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MaterialBalance_RequiresExactEquation()
    {
        Assert.True(ProductionMaterialBalanceValidator.IsBalanced(10, 7, 2, 0.5m, 0.5m));
        Assert.False(ProductionMaterialBalanceValidator.IsBalanced(10, 7, 2, 0, 0));
    }

    [Fact]
    public void ComponentAllocation_RejectsOverInProduct()
    {
        var draft = new ProductionQcDraft(
            1,
            0,
            ProductionQcResultCodes.Pass,
            1.2m,
            "CHI",
            "9999",
            null,
            [new ProductionQcComponentDraft(10, ProductionComponentKinds.Gold, 2)],
            [new ProductionQcImageDraft("stage.jpg", "a.jpg", "image/jpeg", null, 0)]);

        var errors = ProductionQcValidator.Validate(draft, ProductionQcUnitStatuses.Pending, id => id == 10 ? 1m : null);
        Assert.Contains(errors, e => e.Contains("vượt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Qc_PassedRequiresExplicitAdjustmentFlag()
    {
        var draft = new ProductionQcDraft(
            1,
            0,
            ProductionQcResultCodes.Pass,
            1.2m,
            "CHI",
            "9999",
            "Sửa TL",
            [new ProductionQcComponentDraft(10, ProductionComponentKinds.Gold, 1)],
            [new ProductionQcImageDraft("stage.jpg", "a.jpg", "image/jpeg", null, 0)]);

        var blocked = ProductionQcValidator.Validate(draft, ProductionQcUnitStatuses.Passed, _ => 1m);
        Assert.Contains(blocked, e => e.Contains("Điều chỉnh QC", StringComparison.OrdinalIgnoreCase));

        var allowed = ProductionQcValidator.Validate(
            draft with { IsAdjustment = true },
            ProductionQcUnitStatuses.Passed,
            _ => 1m);
        Assert.Empty(allowed);
    }

    [Fact]
    public void Cost_UsesPerLotGoldSnapshot_NotAverage()
    {
        var result = ProductionCostCalculator.Calculate(
            [
                new ProductionCostCalculator.ComponentCostInput(ProductionComponentKinds.Gold, 0.5m, 0, 10_000_000m),
                new ProductionCostCalculator.ComponentCostInput(ProductionComponentKinds.Gold, 0.5m, 0, 12_000_000m),
                new ProductionCostCalculator.ComponentCostInput(ProductionComponentKinds.Stone, 2m, 100_000m, null)
            ],
            laborCostPerUnit: 500_000m,
            actualGoldWeight: 1m);

        Assert.Equal(11_000_000m, result.GoldMaterialCost);
        Assert.Equal(200_000m, result.NonGoldMaterialCost);
        Assert.Equal(500_000m, result.LaborCost);
        Assert.Equal(11_700_000m, result.TotalCost);
        Assert.Equal(1m, result.GoldWeight);
    }

    [Fact]
    public void Completion_RequiresAllUnitsPassed()
    {
        var units = new[]
        {
            new ProductionQcUnitDto(1, 1, 1, ProductionQcUnitStatuses.Passed, "Nhẫn", 1, "CHI", "9999", 100, null, null, null, 0),
            new ProductionQcUnitDto(2, 1, 2, ProductionQcUnitStatuses.Pending, "Nhẫn", null, null, null, 100, null, null, null, 0)
        };

        var balances = new[]
        {
            new ProductionMaterialBalanceDto(1, 1, 1, 1, "V01", "Vàng", "GOLD", "CHI", 1, 1, 0, 0, 0, 1, null, null)
        };

        var errors = ProductionCompletionValidator.Validate(
            ProductionOrderStatuses.InProgress,
            true,
            true,
            units,
            plannedQuantityTotal: 2,
            balances);

        Assert.Contains(errors, e => e.Contains("đạt QC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Draft_RejectsAttachmentLinkedToMissingLine()
    {
        var draft = ValidStockDraft() with
        {
            Attachments =
            [
                new ProductionAttachmentDraft(
                    null,
                    ProductionAttachmentKinds.Design,
                    "stage.pdf",
                    null,
                    "mau.pdf",
                    "application/pdf",
                    null,
                    0,
                    LineNumber: 9)
            ]
        };

        var errors = ProductionOrderDraftValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("dòng mẫu", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MaterialIssue_RejectsEmptyOrDuplicateLines()
    {
        var draft = new ProductionMaterialIssueDraft(
            1, 0, DateTime.Today, null, null, null,
            [
                new ProductionMaterialIssueLineDraft(5, 1, null),
                new ProductionMaterialIssueLineDraft(5, 2, null)
            ]);

        var errors = ProductionMaterialIssueValidator.Validate(draft);
        Assert.Contains(errors, e => e.Contains("trùng", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReturnLines_RequireEveryIssuedBalance()
    {
        var errors = ProductionMaterialBalanceValidator.ValidateReturnLines(
            [new ProductionReturnBalanceLineDraft(1, 1, 0, 0, 0)],
            new Dictionary<long, decimal> { [1] = 1m, [2] = 2m });

        Assert.Contains(errors, e => e.Contains("Thiếu đối soát", StringComparison.OrdinalIgnoreCase));
    }

    private static ProductionOrderDraft ValidStockDraft() =>
        new(
            null,
            null,
            ProductionOrderSources.Stock,
            null,
            ProductionExecutionTypes.InHouse,
            null,
            DateTime.Today,
            null,
            1,
            2,
            "A",
            "B",
            null,
            null,
            [10],
            [new ProductionOrderLineDraft(null, 1, "N1", "Nhẫn", "9999", "Vàng 9999", 5, 100_000, 0, 0, 0, null, null)],
            Array.Empty<ProductionAttachmentDraft>());
}
