using Kazuko.Infrastructure.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SalesQueryRepositoryTests : IDisposable
{
    private readonly SalesTestDatabase _database = new();
    private readonly FirebirdSalesQueryRepository _repository = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task FindAvailableProductAsync_ReturnsFullTrustedSnapshotForEligibleSalesFloorProduct()
    {
        var result = await _repository.FindAvailableProductAsync(
            "T3_PRODUCT_DISPLAYED",
            _database.BranchSession(_database.LimitedUserId));

        Assert.NotNull(result);
        Assert.Equal("T3_PRODUCT_DISPLAYED", result.ProductCode);
        Assert.Equal("Task 3 Displayed", result.ProductName);
        Assert.Equal(_database.OldGoldMaterialId, result.MaterialId);
        Assert.Equal("Task 3 Gold 750", result.MaterialName);
        Assert.Equal(_database.SalesFloorLocationId, result.SourceLocationId);
        Assert.Equal("T3_750", result.GoldPurity);
        Assert.Equal(9, result.RowVersion);
        Assert.Equal(3.25m, result.TotalWeightChi);
        Assert.Equal(0.75m, result.StoneWeightChi);
        Assert.Equal(2.50m, result.GoldWeightChi);
        Assert.Equal(111m, result.SourceGoldPricePerChi);
        Assert.Equal(25m, result.SourceLaborAmountVnd);
        Assert.Equal(8m, result.SourceStoneAmountVnd);
    }

    [Fact]
    public async Task FindAvailableProductAsync_ResolvesCommonKaratLabelThroughMaterialPurity()
    {
        var result = await _repository.FindAvailableProductAsync(
            "T3_PRODUCT_18K",
            _database.BranchSession(_database.LimitedUserId));

        Assert.NotNull(result);
        Assert.Equal("18K", result.GoldPurity);
        Assert.Equal("Vàng 18K / 750", result.MaterialName);
        Assert.Equal(10_500_000m, result.SourceGoldPricePerChi);
    }

    [Theory]
    [InlineData("T3_PRODUCT_INSTOCK", true)]
    [InlineData("T3_PRODUCT_SOLD", false)]
    [InlineData("T3_PRODUCT_RAW", false)]
    [InlineData("T3_PRODUCT_BRANCH_B", false)]
    [InlineData("T3_PRODUCT_COMPANY_B", false)]
    public async Task FindAvailableProductAsync_EnforcesStatusLocationBranchAndCompany(
        string code,
        bool expected)
    {
        var result = await _repository.FindAvailableProductAsync(
            code,
            _database.BranchSession(_database.LimitedUserId));

        Assert.Equal(expected, result is not null);
    }

    [Fact]
    public async Task SearchCustomersAsync_ReturnsActiveCompanyCustomersAndIsBounded()
    {
        var session = _database.BranchSession(_database.LimitedUserId);

        var named = await _repository.SearchCustomersAsync("T3_NAMED", session);
        var inactive = await _repository.SearchCustomersAsync("T3_INACTIVE", session);
        var otherCompany = await _repository.SearchCustomersAsync("T3_OTHER_CO", session);
        var bounded = await _repository.SearchCustomersAsync("T3_LIMIT_", session);

        var customer = Assert.Single(named);
        Assert.Equal(_database.NamedCustomerId, customer.Id);
        Assert.False(customer.IsWalkIn);
        Assert.Empty(inactive);
        Assert.Empty(otherCompany);
        Assert.Equal(50, bounded.Count);
        Assert.Equal(bounded.OrderBy(item => item.Name).ThenBy(item => item.Code).Select(item => item.Id),
            bounded.Select(item => item.Id));
    }

    [Fact]
    public async Task SearchCustomersAsync_IdentifiesWalkInCustomer()
    {
        var result = await _repository.SearchCustomersAsync(
            "Task 3 Walk-in",
            _database.BranchSession(_database.LimitedUserId));

        Assert.Contains(result, customer => customer.IsWalkIn);
    }

    [Fact]
    public async Task GetSalespeopleAsync_ReturnsOnlyActiveUsersScopedToCurrentCompanyAndBranch()
    {
        var result = await _repository.GetSalespeopleAsync(
            _database.BranchSession(_database.LimitedUserId));

        Assert.Contains(result, salesperson => salesperson.Id == _database.SalespersonId);
        Assert.DoesNotContain(result, salesperson => salesperson.Username == "t3-other-branch");
        Assert.DoesNotContain(result, salesperson => salesperson.Username == "t3-other-company");
        Assert.DoesNotContain(result, salesperson => salesperson.Username == "t3-inactive");
        Assert.True(result.Count <= 100);
        Assert.Equal(result.OrderBy(item => item.DisplayName).ThenBy(item => item.Username).Select(item => item.Id),
            result.Select(item => item.Id));
    }

    [Fact]
    public async Task GetOldGoldMaterialsAsync_ReturnsActiveCompanyGoldWithTrustedBuyPrice()
    {
        var result = await _repository.GetOldGoldMaterialsAsync(
            _database.BranchSession(_database.LimitedUserId));

        var material = Assert.Single(result, item => item.Code == "T3_GOLD_750");
        Assert.Equal("Task 3 Gold 750", material.Name);
        Assert.Equal(75m, material.ValuePercent);
        Assert.Equal(77m, material.SourceBuyPricePerChi);
        Assert.DoesNotContain(result, item => item.Code == "T3_GOLD_INACTIVE");
        Assert.DoesNotContain(result, item => item.Name == "Task 3 Other Gold 750");
        Assert.True(result.Count <= 100);
    }

    [Fact]
    public async Task GetOldGoldLocationsAsync_ReturnsOnlyActiveRawMaterialLocationsInBranch()
    {
        var result = await _repository.GetOldGoldLocationsAsync(
            _database.BranchSession(_database.LimitedUserId));

        var location = Assert.Single(result, item => item.Id == _database.OldGoldLocationId);
        Assert.True(location.IsDefault);
        Assert.DoesNotContain(result, item => item.Code is "T3_INACTIVE_LOC" or "T3_SALES_B" or "T3_SALES_C");
        Assert.True(result.Count <= 100);
    }

    [Fact]
    public async Task GetCheckoutAsync_ReturnsSnapshotOnlyInsideCurrentBranchAndCompany()
    {
        var session = _database.BranchSession(_database.LimitedUserId);

        var detail = await _repository.GetCheckoutAsync(_database.MiddleCheckoutId, session);
        var otherBranch = await _repository.GetCheckoutAsync(_database.OtherBranchCheckoutId, session);
        var otherCompany = await _repository.GetCheckoutAsync(_database.OtherCompanyCheckoutId, session);

        Assert.NotNull(detail);
        Assert.Equal("T3-CHK-002", detail.DocumentCode);
        Assert.Equal("Task 3 Named Customer", detail.CustomerName);
        Assert.Equal("Task 3 Salesperson", detail.SalespersonName);
        Assert.Equal("Task 3 Limited User", detail.CreatedByDisplayName);
        var line = Assert.Single(detail.SaleLines);
        Assert.Equal("T3_PRODUCT_DISPLAYED", line.ProductCode);
        Assert.Equal(111m, line.SourceGoldPricePerChi);
        Assert.Equal(100m, line.AppliedPrices.AppliedGoldPricePerChi);
        Assert.True(line.Amounts.IsPriceOverride);
        Assert.Empty(detail.OldGoldLines);
        Assert.Empty(detail.Allocations);
        Assert.Equal(353m, detail.Totals.SaleGrossVnd);
        Assert.Equal(350m, detail.Totals.NetAmountVnd);
        Assert.Null(otherBranch);
        Assert.Null(otherCompany);
    }

    [Fact]
    public async Task GetAdjacentCheckoutIdAsync_UsesDeterministicScopedDocumentOrder()
    {
        var session = _database.BranchSession(_database.LimitedUserId);

        var previous = await _repository.GetAdjacentCheckoutIdAsync(_database.MiddleCheckoutId, false, session);
        var next = await _repository.GetAdjacentCheckoutIdAsync(_database.MiddleCheckoutId, true, session);
        var crossBranch = await _repository.GetAdjacentCheckoutIdAsync(_database.OtherBranchCheckoutId, true, session);

        Assert.NotNull(previous);
        Assert.NotNull(next);
        Assert.NotEqual(_database.OtherBranchCheckoutId, next);
        Assert.NotEqual(_database.OtherCompanyCheckoutId, next);
        Assert.Null(crossBranch);
    }
}
