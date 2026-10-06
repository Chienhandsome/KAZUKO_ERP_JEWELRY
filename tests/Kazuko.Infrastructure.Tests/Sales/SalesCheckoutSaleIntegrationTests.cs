using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SalesCheckoutSaleIntegrationTests : IDisposable
{
    private readonly SalesTestDatabase _database = new();
    private readonly FirebirdSalesCheckoutApplicationService _service = new();
    private readonly FirebirdSalesQueryRepository _queries = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Posts_sale_and_finished_goods_issue_atomically()
    {
        var (draft, product) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.False(result.AlreadyPosted);
        Assert.Equal(SalesCheckoutTypes.Sale, result.CheckoutType);
        Assert.Equal(311m, result.Totals.SaleGrossVnd);
        Assert.Equal(311m, result.Totals.NetAmountVnd);
        Assert.NotNull(result.SalesOrderId);
        Assert.Equal("SOLD", await ScalarAsync<string>(
            "SELECT STATUS_CODE FROM FINISHED_PRODUCTS WHERE ID = @ID", ("@ID", product.FinishedProductId)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM FINISHED_PRODUCT_MOVEMENTS WHERE FINISHED_PRODUCT_ID = @ID AND MOVEMENT_TYPE = 'OUT'",
            ("@ID", product.FinishedProductId)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM STOCK_DOCUMENTS WHERE SALES_ORDER_ID = @ID AND DOCUMENT_TYPE = 'FINISHED_GOODS_ISSUE'",
            ("@ID", result.SalesOrderId!.Value)));
    }

    [Fact]
    public async Task Posts_mixed_cash_bank_card_receipts()
    {
        var sources = await PaymentSourcesAsync();
        var (draft, _) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        draft = draft with
        {
            Allocations =
            [
                new(PaymentMethods.Cash, 100m, sources.CashFundId, null, null),
                new(PaymentMethods.BankTransfer, 100m, null, sources.BankAccountId, "BANK-1"),
                new(PaymentMethods.Card, 111m, null, sources.BankAccountId, "CARD-1")
            ]
        };

        var result = await _service.PostAsync(draft, AdminSession());

        var settlementId = await ScalarAsync<long>(
            "SELECT FINANCIAL_SETTLEMENT_ID FROM SALES_CHECKOUTS WHERE ID = @ID", ("@ID", result.CheckoutId));
        Assert.True(settlementId > 0);
        Assert.Equal(3, await CountAsync(
            "SELECT COUNT(*) FROM FINANCIAL_SETTLEMENT_ALLOCATIONS WHERE SETTLEMENT_ID = @ID", ("@ID", settlementId)));
        Assert.Equal(3, await CountAsync(
            "SELECT COUNT(*) FROM ORDER_PAYMENTS WHERE ORDER_ID = @ID AND FINANCIAL_SETTLEMENT_ID = @SID",
            ("@ID", result.SalesOrderId!.Value), ("@SID", settlementId)));
        Assert.Equal(0, await CountAsync("""
            SELECT COUNT(*) FROM FINANCIAL_SETTLEMENT_ALLOCATIONS
            WHERE SETTLEMENT_ID = @ID AND PAYMENT_METHOD = 'CARD' AND CASH_FUND_ID IS NOT NULL
            """, ("@ID", settlementId)));
    }

    [Fact]
    public async Task Creates_receivable_for_authorized_named_customer()
    {
        var (draft, _) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        draft = draft with { Allocations = [], ReceivableAmountVnd = 311m };

        var result = await _service.PostAsync(draft, AdminSession());

        var debtId = await ScalarAsync<long>(
            "SELECT RECEIVABLE_DEBT_ID FROM SALES_CHECKOUTS WHERE ID = @ID", ("@ID", result.CheckoutId));
        Assert.Equal(311m, await ScalarAsync<decimal>(
            "SELECT REMAINING_AMOUNT_VND FROM RECEIVABLE_DEBTS WHERE ID = @ID AND DEBT_KIND = 'SALES'",
            ("@ID", debtId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM FINANCIAL_SETTLEMENTS WHERE ID = (SELECT FINANCIAL_SETTLEMENT_ID FROM SALES_CHECKOUTS WHERE ID = @ID)",
            ("@ID", result.CheckoutId)));
    }

    [Fact]
    public async Task Rejects_walk_in_debt()
    {
        var walkIn = (await _queries.SearchCustomersAsync("WALK_IN", AdminSession())).Single();
        var (draft, product) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        draft = draft with { CustomerId = walkIn.Id, Allocations = [], ReceivableAmountVnd = 311m };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Contains("Khách lẻ", error.Message, StringComparison.OrdinalIgnoreCase);
        await AssertUnpostedAsync(draft.ClientRequestId, product.FinishedProductId);
    }

    [Fact]
    public async Task Rejects_customer_locked_after_lookup()
    {
        var (draft, product) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        await ExecuteAsync("UPDATE CUSTOMERS SET IS_ACTIVE = FALSE WHERE ID = @ID", ("@ID", draft.CustomerId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        await AssertUnpostedAsync(draft.ClientRequestId, product.FinishedProductId);
    }

    [Fact]
    public async Task Rolls_back_when_product_changes_branch_or_status()
    {
        var (draft, product) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        await ExecuteAsync("UPDATE FINISHED_PRODUCTS SET STATUS_CODE = 'SOLD', ROW_VERSION = ROW_VERSION + 1 WHERE ID = @ID",
            ("@ID", product.FinishedProductId));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_ORDERS WHERE CODE STARTING WITH 'MB26/'"));
    }

    [Fact]
    public async Task Only_one_concurrent_checkout_can_sell_product()
    {
        var (firstDraft, _) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        var secondDraft = firstDraft with { ClientRequestId = Guid.NewGuid(), Notes = "second terminal" };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<(bool Success, SalesCheckoutResult? Result)> PostAsync(SalesCheckoutDraft draft)
        {
            await gate.Task;
            try { return (true, await _service.PostAsync(draft, AdminSession())); }
            catch (InvalidOperationException) { return (false, null); }
            catch (FbException) { return (false, null); }
        }

        var first = PostAsync(firstDraft);
        var second = PostAsync(secondDraft);
        gate.SetResult();
        var outcomes = await Task.WhenAll(first, second);

        Assert.Single(outcomes, x => x.Success);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID IN (@A, @B)",
            ("@A", firstDraft.ClientRequestId.ToString()), ("@B", secondDraft.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Replays_same_request_and_hash_without_duplicate()
    {
        var (draft, _) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");

        var first = await _service.PostAsync(draft, AdminSession());
        var second = await _service.PostAsync(draft, AdminSession());

        Assert.True(second.AlreadyPosted);
        Assert.Equal(first.CheckoutId, second.CheckoutId);
        Assert.Equal(first.SalesOrderId, second.SalesOrderId);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Rejects_same_request_id_with_different_hash()
    {
        var (draft, _) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        await _service.PostAsync(draft, AdminSession());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PostAsync(draft with { Notes = "different business value" }, AdminSession()));

        Assert.Contains("mã yêu cầu", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Audits_price_override_debt_approval_and_salesperson_change()
    {
        var product = await ProductAsync("T3_PRODUCT_DISPLAYED");
        var draft = BaseDraft(product) with
        {
            SalespersonId = _database.SalespersonId,
            SaleLines = [new(product.FinishedProductId, product.SourceGoldPricePerChi + 1,
                product.SourceLaborAmountVnd, product.SourceStoneAmountVnd, "Manager approved")],
            Allocations = [],
            ReceivableAmountVnd = 313m
        };

        var result = await _service.PostAsync(draft, AdminSession());

        var audit = await RowsAsync<string>(
            "SELECT EVENT_TYPE FROM AUDIT_LOG WHERE ENTITY_TYPE = 'SALES_CHECKOUT' AND ENTITY_ID = @ID",
            ("@ID", result.CheckoutId.ToString()));
        Assert.Contains("SALES_CHECKOUT_POSTED", audit);
        Assert.Contains("SALES_PRICE_OVERRIDE", audit);
        Assert.Contains("SALES_DEBT_APPROVED", audit);
        Assert.Contains("SALES_SALESPERSON_CHANGED", audit);
        Assert.Equal(_database.AdminUserId, await ScalarAsync<long>(
            "SELECT CREATED_BY FROM SALES_CHECKOUTS WHERE ID = @ID", ("@ID", result.CheckoutId)));
    }

    [Fact]
    public async Task Rejects_old_gold_lines_until_trade_in_is_implemented()
    {
        var (draft, product) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        draft = draft with
        {
            OldGoldDestinationLocationId = _database.OldGoldLocationId,
            OldGoldLines = [new(_database.OldGoldMaterialId, 1, 1, 0, 0, 100, 77, null, null)]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Contains("vàng cũ", error.Message, StringComparison.OrdinalIgnoreCase);
        await AssertUnpostedAsync(draft.ClientRequestId, product.FinishedProductId);
    }

    [Fact]
    public async Task Full_debt_creates_no_zero_receipt_settlement()
    {
        var (draft, _) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        draft = draft with { Allocations = [], ReceivableAmountVnd = 311m };

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE ID = @ID AND FINANCIAL_SETTLEMENT_ID IS NOT NULL",
            ("@ID", result.CheckoutId)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM ORDER_PAYMENTS WHERE ORDER_ID = @ID",
            ("@ID", result.SalesOrderId!.Value)));
    }

    [Fact]
    public async Task Requires_override_reason_on_every_changed_line()
    {
        var first = await ProductAsync("T3_PRODUCT_DISPLAYED");
        var second = await ProductAsync("T3_PRODUCT_INSTOCK");
        var total = 311m + 314m;
        var draft = BaseDraft(first) with
        {
            SaleLines =
            [
                new(first.FinishedProductId, first.SourceGoldPricePerChi + 1,
                    first.SourceLaborAmountVnd, first.SourceStoneAmountVnd, "approved"),
                new(second.FinishedProductId, second.SourceGoldPricePerChi + 1,
                    second.SourceLaborAmountVnd, second.SourceStoneAmountVnd, null)
            ],
            Allocations = [],
            ReceivableAmountVnd = total
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Contains("lý do", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Reloads_explicit_retail_price_mapping_before_posting()
    {
        var product = await ProductAsync("T3_PRODUCT_DISPLAYED");
        var replacementPriceId = await InsertIdAsync("""
            INSERT INTO GOLD_PRICES
                (COMPANY_ID, CODE, NAME, PURITY_RATE, BUY_PRICE_PER_CHI, SELL_PRICE_PER_CHI,
                 BUY_PRICE_PER_GRAM, SELL_PRICE_PER_GRAM, CURRENCY_CODE, DISPLAY_ORDER, IS_ACTIVE)
            VALUES (@CID, 'T4_RETAIL', 'Task 4 replacement', .75, 80, 200, 0, 0, 'VND', 1, TRUE)
            RETURNING ID
            """, ("@CID", _database.CompanyId));
        await ExecuteAsync("UPDATE FINISHED_PRODUCTS SET RETAIL_GOLD_PRICE_ID = @PID WHERE ID = @ID",
            ("@PID", replacementPriceId), ("@ID", product.FinishedProductId));
        var draft = BaseDraft(product);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Contains("lý do", error.Message, StringComparison.OrdinalIgnoreCase);
        await AssertUnpostedAsync(draft.ClientRequestId, product.FinishedProductId);
    }

    [Fact]
    public async Task Rolls_back_sale_when_downstream_finance_fails()
    {
        var (draft, product) = await SaleDraftAsync("T3_PRODUCT_DISPLAYED");
        draft = draft with { Allocations = [new(PaymentMethods.Cash, 311m, long.MaxValue, null, null)] };

        await Assert.ThrowsAnyAsync<Exception>(() => _service.PostAsync(draft, AdminSession()));

        await AssertUnpostedAsync(draft.ClientRequestId, product.FinishedProductId);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_ORDERS WHERE CODE STARTING WITH 'MB26/'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM FINANCIAL_SETTLEMENTS WHERE NOTES = @NOTES",
            ("@NOTES", draft.Notes!)));
    }

    private AuthenticatedSession AdminSession() => _database.BranchSession(_database.AdminUserId, "ADMIN");

    private async Task<(SalesCheckoutDraft Draft, SalesProductOption Product)> SaleDraftAsync(string code)
    {
        var product = await ProductAsync(code);
        var sources = await PaymentSourcesAsync();
        return (BaseDraft(product) with
        {
            Allocations = [new(PaymentMethods.Cash, 311m, sources.CashFundId, null, null)]
        }, product);
    }

    private SalesCheckoutDraft BaseDraft(SalesProductOption product) => new(
        Guid.NewGuid(), new DateTime(2026, 10, 6), _database.NamedCustomerId, _database.AdminUserId,
        _database.SalesFloorLocationId, null, 0,
        [new(product.FinishedProductId, product.SourceGoldPricePerChi,
            product.SourceLaborAmountVnd, product.SourceStoneAmountVnd, null)],
        [], [], 0, $"Task 4 {Guid.NewGuid():N}");

    private async Task<SalesProductOption> ProductAsync(string code) =>
        Assert.IsType<SalesProductOption>(await _queries.FindAvailableProductAsync(code, AdminSession()));

    private async Task<(long CashFundId, long BankAccountId)> PaymentSourcesAsync()
    {
        var fundId = _database.CashFundId;
        var accountId = await InsertIdAsync("""
            INSERT INTO BANK_ACCOUNTS (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
            VALUES (@CID, @BID, @CODE, 'Task 4 Bank', @CODE, TRUE)
            RETURNING ID
            """, ("@CID", _database.CompanyId), ("@BID", _database.BranchId),
            ("@CODE", $"T4{Guid.NewGuid():N}"[..20]));
        return (fundId, accountId);
    }

    private async Task AssertUnpostedAsync(Guid requestId, long productId)
    {
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", requestId.ToString())));
        Assert.NotEqual("SOLD", await ScalarAsync<string>(
            "SELECT STATUS_CODE FROM FINISHED_PRODUCTS WHERE ID = @ID", ("@ID", productId)));
    }

    private static async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt32(await ScalarObjectAsync(sql, parameters));

    private static async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
        (T)Convert.ChangeType(await ScalarObjectAsync(sql, parameters), typeof(T));

    private static async Task<object> ScalarObjectAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(connection, null, sql, parameters);
        return (await command.ExecuteScalarAsync())!;
    }

    private static async Task<IReadOnlyList<T>> RowsAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(connection, null, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<T>();
        while (await reader.ReadAsync()) result.Add((T)Convert.ChangeType(reader.GetValue(0), typeof(T)));
        return result;
    }

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(connection, null, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertIdAsync(string sql, params (string Name, object Value)[] parameters) =>
        await ScalarAsync<long>(sql, parameters);

    private static FbCommand Command(
        FbConnection connection, FbTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = new FbCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
