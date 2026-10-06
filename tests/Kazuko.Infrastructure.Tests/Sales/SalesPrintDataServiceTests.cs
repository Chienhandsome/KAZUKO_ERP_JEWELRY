using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SalesPrintDataServiceTests : IDisposable
{
    private readonly SalesTestDatabase _database = new();
    private readonly FirebirdSalesCheckoutApplicationService _checkout = new();
    private readonly FirebirdSalesQueryRepository _queries = new();
    private readonly FirebirdSalesPrintDataService _printData;

    public SalesPrintDataServiceTests()
    {
        _printData = new FirebirdSalesPrintDataService(_queries);
    }

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task GetReceipt_returns_snapshot_independent_of_live_prices()
    {
        var (draft, product) = await SaleDraftAsync();
        var posted = await _checkout.PostAsync(draft, AdminSession());

        var before = await _printData.GetReceiptAsync(posted.CheckoutId, AdminSession());
        Assert.Equal(posted.DocumentCode, before.Checkout.DocumentCode);
        Assert.Equal(SalesCheckoutTypes.Sale, before.Checkout.CheckoutType);
        Assert.NotEmpty(before.CompanyName);
        Assert.NotEmpty(before.BranchName);
        Assert.Single(before.Checkout.SaleLines);
        Assert.Equal(product.FinishedProductId, before.Checkout.SaleLines[0].FinishedProductId);
        Assert.Equal(311m, before.Checkout.Totals.NetAmountVnd);
        Assert.Equal(draft.Allocations[0].AmountVnd, before.Checkout.Allocations.Sum(x => x.AmountVnd));
        Assert.Equal(_database.AdminUserId, before.Checkout.CreatedBy);
        Assert.Equal(_database.AdminUserId, before.Checkout.SalespersonId);

        await ExecuteAsync("""
            UPDATE GOLD_PRICES
            SET SELL_PRICE_PER_CHI = SELL_PRICE_PER_CHI + 1000,
                BUY_PRICE_PER_CHI = BUY_PRICE_PER_CHI + 1000
            WHERE ID = @ID
            """, ("@ID", _database.RetailGoldPriceId));

        var after = await _printData.GetReceiptAsync(posted.CheckoutId, AdminSession());
        Assert.Equal(before.Checkout.SaleLines[0].Amounts.TotalAmountVnd, after.Checkout.SaleLines[0].Amounts.TotalAmountVnd);
        Assert.Equal(before.Checkout.SaleLines[0].AppliedPrices.AppliedGoldPricePerChi,
            after.Checkout.SaleLines[0].AppliedPrices.AppliedGoldPricePerChi);
        Assert.Equal(before.Checkout.Totals.NetAmountVnd, after.Checkout.Totals.NetAmountVnd);
    }

    [Fact]
    public async Task GetReceipt_rejects_other_branch_access()
    {
        var (draft, _) = await SaleDraftAsync();
        var posted = await _checkout.PostAsync(draft, AdminSession());

        var otherBranch = new AuthenticatedSession(
            _database.OtherBranchUserId,
            "t3-other-branch",
            "Task 3 Other Branch",
            "SALE",
            _database.OtherBranchId,
            "T3_BRB",
            "Task 3 Branch B");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _printData.GetReceiptAsync(posted.CheckoutId, otherBranch));
    }

    private AuthenticatedSession AdminSession() =>
        _database.BranchSession(_database.AdminUserId, "ADMIN");

    private async Task<(SalesCheckoutDraft Draft, SalesProductOption Product)> SaleDraftAsync()
    {
        var product = Assert.IsType<SalesProductOption>(
            await _queries.FindAvailableProductAsync("T3_PRODUCT_DISPLAYED", AdminSession()));
        var fundId = _database.CashFundId;
        var draft = new SalesCheckoutDraft(
            Guid.NewGuid(),
            new DateTime(2026, 10, 6),
            _database.NamedCustomerId,
            _database.AdminUserId,
            _database.SalesFloorLocationId,
            null,
            0,
            [new(product.FinishedProductId, product.SourceGoldPricePerChi,
                product.SourceLaborAmountVnd, product.SourceStoneAmountVnd, null)],
            [],
            [new(PaymentMethods.Cash, 311m, fundId, null, null)],
            0,
            "print-data-test");
        return (draft, product);
    }

    private async Task ExecuteAsync(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = new FirebirdSql.Data.FirebirdClient.FbCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
