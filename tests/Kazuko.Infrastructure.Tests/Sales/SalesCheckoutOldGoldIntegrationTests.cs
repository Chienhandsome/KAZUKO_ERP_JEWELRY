using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Infrastructure.Customers;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Sales;
using Xunit;

namespace Kazuko.Infrastructure.Tests.Sales;

[Collection(nameof(FirebirdTestCollection))]
public sealed class SalesCheckoutOldGoldIntegrationTests : IDisposable
{
    private readonly SalesTestDatabase _database = new();
    private readonly FirebirdSalesCheckoutApplicationService _service = new();
    private readonly FirebirdSalesQueryRepository _queries = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Posts_trade_in_with_positive_net_receipt()
    {
        var sources = await PaymentSourcesAsync();
        var draft = await TradeInDraftAsync(oldGoldAmount: 77m, receiptAmount: 234m, sources.CashFundId);

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(SalesCheckoutTypes.TradeIn, result.CheckoutType);
        Assert.Equal(311m, result.Totals.SaleGrossVnd);
        Assert.Equal(77m, result.Totals.OldGoldTotalVnd);
        Assert.Equal(77m, result.Totals.OffsetAmountVnd);
        Assert.Equal(234m, result.Totals.NetAmountVnd);
        Assert.NotNull(result.SalesOrderId);
        Assert.NotNull(result.OldGoldReceiptId);
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUT_OFFSETS WHERE CHECKOUT_ID = @ID AND AMOUNT_VND = 77",
            ("@ID", result.CheckoutId)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM FINANCIAL_SETTLEMENTS WHERE ID = (SELECT FINANCIAL_SETTLEMENT_ID FROM SALES_CHECKOUTS WHERE ID = @ID)",
            ("@ID", result.CheckoutId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM PAYABLE_DEBTS WHERE ORIGIN_DOCUMENT_ID = @DOC",
            ("@DOC", result.OldGoldReceiptId!.Value)));
    }

    [Fact]
    public async Task Posts_zero_net_without_cash_voucher()
    {
        var draft = await TradeInDraftAsync(oldGoldAmount: 311m, receiptAmount: 0m, cashFundId: null);

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(0m, result.Totals.NetAmountVnd);
        Assert.Equal(311m, result.Totals.OffsetAmountVnd);
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE ID = @ID AND FINANCIAL_SETTLEMENT_ID IS NOT NULL",
            ("@ID", result.CheckoutId)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM CASH_VOUCHERS WHERE NOTES CONTAINING @CODE",
            ("@CODE", result.DocumentCode)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUT_OFFSETS WHERE CHECKOUT_ID = @ID",
            ("@ID", result.CheckoutId)));
    }

    [Fact]
    public async Task Posts_negative_net_payout()
    {
        var sources = await PaymentSourcesAsync();
        var draft = await TradeInDraftAsync(oldGoldAmount: 400m, receiptAmount: 0m, cashFundId: null);
        draft = draft with
        {
            Allocations = [new(PaymentMethods.Cash, 89m, sources.CashFundId, null, null)]
        };

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(-89m, result.Totals.NetAmountVnd);
        Assert.Equal(89m, result.Totals.PayoutAmountVnd);
        Assert.Equal(311m, result.Totals.OffsetAmountVnd);
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE ID = @ID AND FINANCIAL_SETTLEMENT_ID IS NOT NULL",
            ("@ID", result.CheckoutId)));
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM STOCK_DOCUMENT_PAYMENTS P
            JOIN CASH_VOUCHERS V ON V.ID = P.CASH_VOUCHER_ID
            WHERE P.DOCUMENT_ID = @DOC AND P.AMOUNT_VND = 89 AND V.VOUCHER_TYPE = 'OUT'
            """, ("@DOC", result.OldGoldReceiptId!.Value)));
    }

    [Fact]
    public async Task Posts_standalone_old_gold_purchase()
    {
        var sources = await PaymentSourcesAsync();
        var draft = OldGoldOnlyDraft(77m, sources.CashFundId);

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(SalesCheckoutTypes.OldGoldPurchase, result.CheckoutType);
        Assert.Null(result.SalesOrderId);
        Assert.NotNull(result.OldGoldReceiptId);
        Assert.Equal(0m, result.Totals.SaleGrossVnd);
        Assert.Equal(77m, result.Totals.OldGoldTotalVnd);
        Assert.Equal(-77m, result.Totals.NetAmountVnd);
        Assert.StartsWith("MVC", result.DocumentCode, StringComparison.Ordinal);
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUT_OFFSETS WHERE CHECKOUT_ID = @ID",
            ("@ID", result.CheckoutId)));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM PAYABLE_DEBTS WHERE CUSTOMER_ID = @CID AND ORIGIN_DOCUMENT_TYPE = 'STOCK_DOCUMENT'",
            ("@CID", _database.NamedCustomerId)));
    }

    [Fact]
    public async Task Creates_one_traceable_stock_line_per_old_gold_line()
    {
        var sources = await PaymentSourcesAsync();
        var draft = OldGoldOnlyDraft(154m, sources.CashFundId) with
        {
            OldGoldLines =
            [
                new(_database.OldGoldMaterialId, 1, 1, 0, 0, 100, 77, null, "line-a"),
                new(_database.OldGoldMaterialId, 1, 1, 0, 0, 100, 77, null, "line-b")
            ],
            Allocations = [new(PaymentMethods.Cash, 154m, sources.CashFundId, null, null)]
        };

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM SALES_OLD_GOLD_LINES WHERE CHECKOUT_ID = @ID",
            ("@ID", result.CheckoutId)));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) FROM STOCK_DOCUMENT_LINES WHERE DOCUMENT_ID = @DOC",
            ("@DOC", result.OldGoldReceiptId!.Value)));
        Assert.Equal(2, await CountAsync("""
            SELECT COUNT(*) FROM SALES_OLD_GOLD_LINES L
            JOIN STOCK_DOCUMENT_LINES S ON S.ID = L.STOCK_DOCUMENT_LINE_ID
            WHERE L.CHECKOUT_ID = @ID
            """, ("@ID", result.CheckoutId)));
    }

    [Fact]
    public async Task Rejects_walk_in_or_inactive_customer()
    {
        var sources = await PaymentSourcesAsync();
        var walkIn = (await _queries.SearchCustomersAsync("WALK_IN", AdminSession())).Single();
        var draft = OldGoldOnlyDraft(77m, sources.CashFundId) with { CustomerId = walkIn.Id };

        var walkInError = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.PostAsync(draft, AdminSession()));
        Assert.Contains("Khách lẻ", walkInError.Message, StringComparison.OrdinalIgnoreCase);

        var named = OldGoldOnlyDraft(77m, sources.CashFundId);
        await ExecuteAsync("UPDATE CUSTOMERS SET IS_ACTIVE = FALSE WHERE ID = @ID", ("@ID", named.CustomerId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(named, AdminSession()));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", named.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Rejects_card_payout()
    {
        var sources = await PaymentSourcesAsync();
        var draft = await TradeInDraftAsync(oldGoldAmount: 400m, receiptAmount: 0m, cashFundId: null);
        draft = draft with
        {
            Allocations = [new(PaymentMethods.Card, 89m, null, sources.BankAccountId, "CARD-PAY")]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Contains("thẻ", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Rolls_back_sale_when_old_gold_stock_post_fails()
    {
        var product = await ProductAsync("T3_PRODUCT_DISPLAYED");
        var sources = await PaymentSourcesAsync();
        var draft = BaseSaleDraft(product) with
        {
            OldGoldDestinationLocationId = _database.SalesFloorLocationId,
            OldGoldLines = [new(_database.OldGoldMaterialId, 1, 1, 0, 0, 100, 77, null, null)],
            Allocations = [new(PaymentMethods.Cash, 234m, sources.CashFundId, null, null)]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
        Assert.NotEqual("SOLD", await ScalarAsync<string>(
            "SELECT STATUS_CODE FROM FINISHED_PRODUCTS WHERE ID = @ID", ("@ID", product.FinishedProductId)));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_ORDERS WHERE CODE STARTING WITH 'MB26/'"));
    }

    [Fact]
    public async Task Sums_rounded_old_gold_lines()
    {
        var sources = await PaymentSourcesAsync();
        // Each line: converted 0.3333 * 77 = 25.6441 → 26; two lines → 52 (not 51 from summed weight).
        var draft = OldGoldOnlyDraft(52m, sources.CashFundId) with
        {
            OldGoldLines =
            [
                new(_database.OldGoldMaterialId, 1, 0.3333m, 0, 0, 100, 77, null, null),
                new(_database.OldGoldMaterialId, 1, 0.3333m, 0, 0, 100, 77, null, null)
            ]
        };

        var result = await _service.PostAsync(draft, AdminSession());

        Assert.Equal(52m, result.Totals.OldGoldTotalVnd);
        Assert.Equal(-52m, result.Totals.NetAmountVnd);
    }

    [Fact]
    public async Task Customer_metrics_count_offset_once_without_cash()
    {
        var draft = await TradeInDraftAsync(oldGoldAmount: 311m, receiptAmount: 0m, cashFundId: null);
        await _service.PostAsync(draft, AdminSession());

        var snapshot = await new FirebirdCustomerRepository().GetDebtSnapshotAsync(_database.NamedCustomerId);

        // Seed order T3-SO-001 remains unpaid (350). Zero-net trade-in pays 311 via offset only.
        Assert.Equal(311m, snapshot.Paid);
        Assert.Equal(350m, snapshot.CurrentDebt);
        Assert.Equal(snapshot.TotalIncurred - snapshot.Paid, snapshot.CurrentDebt);
    }

    [Fact]
    public async Task Debt_ledger_counts_offset_once_without_cash()
    {
        var draft = await TradeInDraftAsync(oldGoldAmount: 311m, receiptAmount: 0m, cashFundId: null);
        var result = await _service.PostAsync(draft, AdminSession());

        var ledger = await new FirebirdDebtWorkbenchRepository()
            .GetCustomerLedgerAsync(_database.NamedCustomerId, AdminSession());
        var offsets = ledger.Where(x => x.Description.Contains("Bù trừ", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Single(offsets);
        Assert.Equal(311m, offsets[0].ReceiptAmountVnd);
        Assert.Contains(result.DocumentCode, offsets[0].DocumentCode, StringComparison.Ordinal);
        Assert.Equal(1, ledger.Count(x => x.ReceiptAmountVnd == 311m));
    }

    [Fact]
    public async Task Replays_same_request_and_hash_without_duplicate_old_gold()
    {
        var draft = await TradeInDraftAsync(oldGoldAmount: 77m, receiptAmount: 234m, (await PaymentSourcesAsync()).CashFundId);

        var first = await _service.PostAsync(draft, AdminSession());
        var second = await _service.PostAsync(draft, AdminSession());

        Assert.True(second.AlreadyPosted);
        Assert.Equal(first.CheckoutId, second.CheckoutId);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM STOCK_DOCUMENTS WHERE ID = @ID", ("@ID", first.OldGoldReceiptId!.Value)));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) FROM SALES_CHECKOUT_OFFSETS WHERE CHECKOUT_ID = @ID", ("@ID", first.CheckoutId)));
    }

    [Fact]
    public async Task Rejects_same_request_id_with_different_hash()
    {
        var draft = await TradeInDraftAsync(oldGoldAmount: 77m, receiptAmount: 234m, (await PaymentSourcesAsync()).CashFundId);
        await _service.PostAsync(draft, AdminSession());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.PostAsync(draft with { Notes = "changed" }, AdminSession()));

        Assert.Contains("mã yêu cầu", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
    }

    [Fact]
    public async Task Rolls_back_when_downstream_finance_fails_on_payout()
    {
        var draft = await TradeInDraftAsync(oldGoldAmount: 400m, receiptAmount: 0m, cashFundId: null);
        draft = draft with
        {
            Allocations = [new(PaymentMethods.Cash, 89m, long.MaxValue, null, null)]
        };

        await Assert.ThrowsAnyAsync<Exception>(() => _service.PostAsync(draft, AdminSession()));

        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_CHECKOUTS WHERE CLIENT_REQUEST_ID = @RID",
            ("@RID", draft.ClientRequestId.ToString())));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM SALES_ORDERS WHERE CODE STARTING WITH 'MB26/'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) FROM STOCK_DOCUMENTS WHERE PURPOSE_CODE = 'OLD_GOLD_PURCHASE' AND NOTES = @NOTES",
            ("@NOTES", draft.Notes!)));
    }

    private AuthenticatedSession AdminSession() => _database.BranchSession(_database.AdminUserId, "ADMIN");

    private async Task<SalesCheckoutDraft> TradeInDraftAsync(decimal oldGoldAmount, decimal receiptAmount, long? cashFundId)
    {
        var product = await ProductAsync("T3_PRODUCT_DISPLAYED");
        var overrideReason = oldGoldAmount == 77m ? null : "trade-in fixture";
        var appliedBuy = oldGoldAmount == 77m ? 77m : oldGoldAmount;
        var totalWeight = oldGoldAmount == 77m ? 1m : 1m;
        IReadOnlyList<PaymentAllocationDraft> allocations = receiptAmount > 0
            ? [new(PaymentMethods.Cash, receiptAmount, cashFundId, null, null)]
            : [];
        return BaseSaleDraft(product) with
        {
            OldGoldDestinationLocationId = _database.OldGoldLocationId,
            OldGoldLines =
            [
                new(_database.OldGoldMaterialId, 1, totalWeight, 0, 0, 100, appliedBuy, overrideReason, null)
            ],
            Allocations = allocations,
            ReceivableAmountVnd = 0
        };
    }

    private SalesCheckoutDraft OldGoldOnlyDraft(decimal amountVnd, long cashFundId) => new(
        Guid.NewGuid(), new DateTime(2026, 10, 6), _database.NamedCustomerId, _database.AdminUserId,
        0, _database.OldGoldLocationId, 0,
        [],
        [new(_database.OldGoldMaterialId, 1, amountVnd == 77m || amountVnd == 154m || amountVnd == 52m
                ? amountVnd == 52m ? 0.3333m : 1m
                : 1m,
            0, 0, 100,
            amountVnd is 77m or 154m or 52m ? 77m : amountVnd,
            amountVnd is 77m or 154m or 52m ? null : "purchase fixture", null)],
        [new(PaymentMethods.Cash, amountVnd, cashFundId, null, null)],
        0, $"Task 5 OG {Guid.NewGuid():N}");

    private SalesCheckoutDraft BaseSaleDraft(SalesProductOption product) => new(
        Guid.NewGuid(), new DateTime(2026, 10, 6), _database.NamedCustomerId, _database.AdminUserId,
        _database.SalesFloorLocationId, null, 0,
        [new(product.FinishedProductId, product.SourceGoldPricePerChi,
            product.SourceLaborAmountVnd, product.SourceStoneAmountVnd, null)],
        [], [], 0, $"Task 5 {Guid.NewGuid():N}");

    private async Task<SalesProductOption> ProductAsync(string code) =>
        Assert.IsType<SalesProductOption>(await _queries.FindAvailableProductAsync(code, AdminSession()));

    private async Task<(long CashFundId, long BankAccountId)> PaymentSourcesAsync()
    {
        var accountId = await InsertIdAsync("""
            INSERT INTO BANK_ACCOUNTS (COMPANY_ID, BRANCH_ID, CODE, BANK_NAME, ACCOUNT_NUMBER, IS_ACTIVE)
            VALUES (@CID, @BID, @CODE, 'Task 5 Bank', @CODE, TRUE)
            RETURNING ID
            """, ("@CID", _database.CompanyId), ("@BID", _database.BranchId),
            ("@CODE", $"T5{Guid.NewGuid():N}"[..20]));
        return (_database.CashFundId, accountId);
    }

    private static async Task<int> CountAsync(string sql, params (string Name, object Value)[] parameters) =>
        Convert.ToInt32(await ScalarObjectAsync(sql, parameters));

    private static async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters) =>
        (T)Convert.ChangeType(await ScalarObjectAsync(sql, parameters), typeof(T));

    private static async Task<object> ScalarObjectAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        return (await command.ExecuteScalarAsync())!;
    }

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync();
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertIdAsync(string sql, params (string Name, object Value)[] parameters) =>
        await ScalarAsync<long>(sql, parameters);

    private static FbCommand Command(FbConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new FbCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
