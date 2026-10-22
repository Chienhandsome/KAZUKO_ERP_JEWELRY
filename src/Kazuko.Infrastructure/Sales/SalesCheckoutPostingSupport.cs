using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Sales;

public sealed record SalePostResult(
    long CheckoutId,
    string CheckoutCode,
    string CheckoutType,
    long? SalesOrderId,
    long CustomerId,
    DateTime DocumentDate,
    SalesCheckoutTotals Totals,
    IReadOnlyList<PreparedSaleLine> Lines);

public sealed record PreparedSaleLine(
    SalesLineDraft Draft,
    SalesLinePricing Pricing,
    SalesLineAmounts Amounts,
    string ProductCode,
    string ProductName,
    string GoldPurity);

public sealed record PreparedOldGoldLine(
    OldGoldLineDraft Draft,
    decimal SourceBuyPricePerChi,
    OldGoldLineAmounts Amounts,
    string MaterialCode,
    string MaterialName,
    string MaterialType,
    string UnitCode,
    bool IsPriceOverride);

public sealed record PreparedCheckout(
    string CheckoutType,
    bool IsWalkInCustomer,
    SalesCheckoutTotals Totals,
    IReadOnlyList<PreparedSaleLine> SaleLines,
    IReadOnlyList<PreparedOldGoldLine> OldGoldLines);

public sealed record OldGoldPostResult(
    long StockDocumentId,
    string StockDocumentCode,
    decimal TotalAmountVnd);

public static class SalesCheckoutPostingSupport
{
    public static async Task<PreparedCheckout> PrepareCheckoutAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        SalesCheckoutDraft draft,
        SalesPermissionSnapshot permissions,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (draft.SaleLines.Count == 0 && draft.OldGoldLines.Count == 0)
            throw new InvalidOperationException("Checkout phải có ít nhất một dòng bán hoặc vàng cũ.");

        var customer = await LoadCustomerAsync(
            connection, transaction, companyId, draft.CustomerId, cancellationToken);
        await EnsureSalespersonAsync(
            connection, transaction, companyId, session.BranchId, draft.SalespersonId, cancellationToken);

        if (draft.SaleLines.Count > 0)
            await EnsureSalesLocationAsync(
                connection, transaction, companyId, session.BranchId, draft.SourceLocationId, cancellationToken);
        if (draft.OldGoldLines.Count > 0)
        {
            if (draft.OldGoldDestinationLocationId is null or <= 0)
                throw new InvalidOperationException("Phải chọn kho nhập vàng cũ.");
            await EnsureOldGoldLocationAsync(
                connection, transaction, companyId, session.BranchId,
                draft.OldGoldDestinationLocationId.Value, cancellationToken);
        }

        var saleLines = new List<PreparedSaleLine>(draft.SaleLines.Count);
        foreach (var line in draft.SaleLines)
        {
            var product = await LoadProductAsync(
                connection, transaction, companyId, session.BranchId,
                draft.SourceLocationId, line.FinishedProductId, cancellationToken);
            var pricing = new SalesLinePricing(
                product.Id, product.GoldWeightChi, product.SourceGoldPricePerChi,
                product.SourceLaborAmountVnd, product.SourceStoneAmountVnd);
            var amounts = SalesCheckoutCalculator.CalculateSaleLine(line, pricing);
            saleLines.Add(new PreparedSaleLine(line, pricing, amounts,
                product.Code, product.Name, product.GoldPurity));
        }

        var oldGoldLines = new List<PreparedOldGoldLine>(draft.OldGoldLines.Count);
        foreach (var line in draft.OldGoldLines)
        {
            var material = await LoadOldGoldMaterialAsync(
                connection, transaction, companyId, line.MaterialId, cancellationToken);
            var amounts = SalesCheckoutCalculator.CalculateOldGold(line);
            var isOverride = line.AppliedBuyPricePerChi != material.SourceBuyPricePerChi;
            oldGoldLines.Add(new PreparedOldGoldLine(
                line, material.SourceBuyPricePerChi, amounts,
                material.Code, material.Name, material.MaterialType, material.UnitCode, isOverride));
        }

        var totals = SalesCheckoutCalculator.CalculateTotals(
            draft, saleLines.Select(x => x.Amounts).ToArray(),
            oldGoldLines.Select(x => x.Amounts).ToArray());
        var hasOverride = saleLines.Any(x => x.Amounts.IsPriceOverride)
            || oldGoldLines.Any(x => x.IsPriceOverride);
        var validation = SalesCheckoutValidator.Validate(
            draft, totals,
            new SalesValidationContext(customer.IsWalkIn, session.UserId, permissions, hasOverride));
        if (validation.Count > 0)
            throw new InvalidOperationException(string.Join(" ", validation));

        var checkoutType = draft.SaleLines.Count > 0 && draft.OldGoldLines.Count > 0
            ? SalesCheckoutTypes.TradeIn
            : draft.OldGoldLines.Count > 0
                ? SalesCheckoutTypes.OldGoldPurchase
                : SalesCheckoutTypes.Sale;
        return new PreparedCheckout(checkoutType, customer.IsWalkIn, totals, saleLines, oldGoldLines);
    }

    public static async Task<SalesCheckoutTotals> GetTrustedTotalsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        SalesCheckoutDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var saleAmounts = new List<SalesLineAmounts>(draft.SaleLines.Count);
        foreach (var line in draft.SaleLines)
        {
            var product = await LoadProductAsync(
                connection, transaction, companyId, session.BranchId,
                draft.SourceLocationId, line.FinishedProductId, cancellationToken);
            saleAmounts.Add(SalesCheckoutCalculator.CalculateSaleLine(line,
                new SalesLinePricing(product.Id, product.GoldWeightChi,
                    product.SourceGoldPricePerChi, product.SourceLaborAmountVnd,
                    product.SourceStoneAmountVnd)));
        }

        var oldGoldAmounts = new List<OldGoldLineAmounts>(draft.OldGoldLines.Count);
        foreach (var line in draft.OldGoldLines)
        {
            _ = await LoadOldGoldMaterialAsync(
                connection, transaction, companyId, line.MaterialId, cancellationToken);
            oldGoldAmounts.Add(SalesCheckoutCalculator.CalculateOldGold(line));
        }

        return SalesCheckoutCalculator.CalculateTotals(draft, saleAmounts, oldGoldAmounts);
    }

    public static async Task<SalePostResult> InsertCheckoutAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        SalesCheckoutDraft draft,
        PreparedCheckout prepared,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var code = await NextCheckoutCodeAsync(
            connection, transaction, session.BranchId, draft.DocumentDate,
            prepared.CheckoutType, cancellationToken);
        var requestHash = ComputeRequestHash(draft);
        long checkoutId;
        await using (var insertCheckout = new FbCommand("""
            INSERT INTO SALES_CHECKOUTS
                (COMPANY_ID, BRANCH_ID, CODE, CHECKOUT_TYPE, DOCUMENT_DATE,
                 CUSTOMER_ID, SALESPERSON_ID, SOURCE_LOCATION_ID, OLD_GOLD_DESTINATION_LOCATION_ID,
                 SALE_GROSS_VND, OLD_GOLD_TOTAL_VND, DISCOUNT_AMOUNT_VND, NET_AMOUNT_VND,
                 CLIENT_REQUEST_ID, REQUEST_HASH, STATUS_CODE, NOTES, CREATED_BY)
            VALUES
                (@CID, @BID, @CODE, @TYPE, @DATE,
                 @CUSTOMER, @SALESPERSON, @LOCATION, @OLD_LOC,
                 @GROSS, @OLD_GOLD, @DISCOUNT, @NET,
                 @REQUEST_ID, @HASH, 'POSTED', @NOTES, @BY)
            RETURNING ID
            """, connection, transaction))
        {
            insertCheckout.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            insertCheckout.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            insertCheckout.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
            insertCheckout.Parameters.Add("@TYPE", FbDbType.VarChar, 30).Value = prepared.CheckoutType;
            insertCheckout.Parameters.Add("@DATE", FbDbType.Date).Value = draft.DocumentDate.Date;
            insertCheckout.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = draft.CustomerId;
            insertCheckout.Parameters.Add("@SALESPERSON", FbDbType.BigInt).Value = draft.SalespersonId;
            insertCheckout.Parameters.Add("@LOCATION", FbDbType.BigInt).Value =
                draft.SaleLines.Count > 0 ? draft.SourceLocationId : DBNull.Value;
            insertCheckout.Parameters.Add("@OLD_LOC", FbDbType.BigInt).Value =
                draft.OldGoldDestinationLocationId is > 0
                    ? draft.OldGoldDestinationLocationId.Value
                    : DBNull.Value;
            insertCheckout.Parameters.Add("@GROSS", FbDbType.Decimal).Value = prepared.Totals.SaleGrossVnd;
            insertCheckout.Parameters.Add("@OLD_GOLD", FbDbType.Decimal).Value = prepared.Totals.OldGoldTotalVnd;
            insertCheckout.Parameters.Add("@DISCOUNT", FbDbType.Decimal).Value = prepared.Totals.DiscountAmountVnd;
            insertCheckout.Parameters.Add("@NET", FbDbType.Decimal).Value = prepared.Totals.NetAmountVnd;
            insertCheckout.Parameters.Add("@REQUEST_ID", FbDbType.Char, 36).Value = draft.ClientRequestId.ToString("D");
            insertCheckout.Parameters.Add("@HASH", FbDbType.Char, 64).Value = requestHash;
            insertCheckout.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value = DbText(draft.Notes);
            insertCheckout.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            checkoutId = Convert.ToInt64(await insertCheckout.ExecuteScalarAsync(cancellationToken));
        }

        return new SalePostResult(
            checkoutId, code, prepared.CheckoutType, null, draft.CustomerId,
            draft.DocumentDate, prepared.Totals, prepared.SaleLines);
    }

    public static async Task<long> PostSaleOrderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long checkoutId,
        SalesCheckoutDraft draft,
        PreparedCheckout prepared,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (prepared.SaleLines.Count == 0)
            throw new InvalidOperationException("Không có dòng bán để tạo đơn.");

        long orderId;
        await using (var insertOrder = new FbCommand("""
            INSERT INTO SALES_ORDERS
                (COMPANY_ID, BRANCH_ID, CODE, ORDER_TYPE, CUSTOMER_ID, ORDER_DATE,
                 SALES_PERSON_ID, STATUS_CODE, SUBTOTAL, DISCOUNT_AMOUNT, TAX_AMOUNT,
                 GRAND_TOTAL, NOTES, CREATED_BY, UPDATED_BY)
            VALUES
                (@CID, @BID, @CODE, 'RETAIL', @CUSTOMER, @DATE,
                 @SALESPERSON, 'COMPLETED', @SUBTOTAL, @DISCOUNT, 0,
                 @TOTAL, @NOTES, @BY, @BY)
            RETURNING ID
            """, connection, transaction))
        {
            insertOrder.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            insertOrder.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            insertOrder.Parameters.Add("@CODE", FbDbType.VarChar, 50).Value =
                (await LoadCheckoutCodeAsync(connection, transaction, checkoutId, cancellationToken));
            insertOrder.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = draft.CustomerId;
            insertOrder.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
            insertOrder.Parameters.Add("@SALESPERSON", FbDbType.BigInt).Value = draft.SalespersonId;
            insertOrder.Parameters.Add("@SUBTOTAL", FbDbType.Decimal).Value = prepared.Totals.SaleGrossVnd;
            insertOrder.Parameters.Add("@DISCOUNT", FbDbType.Decimal).Value = prepared.Totals.DiscountAmountVnd;
            insertOrder.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = prepared.Totals.SaleNetVnd;
            insertOrder.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value = DbText(draft.Notes);
            insertOrder.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            orderId = Convert.ToInt64(await insertOrder.ExecuteScalarAsync(cancellationToken));
        }

        var lineNumber = 0;
        foreach (var line in prepared.SaleLines)
        {
            lineNumber++;
            await using var insertLine = new FbCommand("""
                INSERT INTO SALES_ORDER_ITEMS
                    (ORDER_ID, LINE_NUMBER, PRODUCT_NAME, GOLD_PURITY, QUANTITY, ESTIMATED_WEIGHT,
                     GOLD_UNIT_PRICE, LABOR_AMOUNT, STONE_AMOUNT, LINE_SUBTOTAL, LINE_TOTAL,
                     FINISHED_PRODUCT_ID, SOURCE_GOLD_PRICE_PER_CHI, APPLIED_GOLD_PRICE_PER_CHI,
                     SOURCE_LABOR_AMOUNT_VND, APPLIED_LABOR_AMOUNT_VND,
                     SOURCE_STONE_AMOUNT_VND, APPLIED_STONE_AMOUNT_VND,
                     IS_PRICE_OVERRIDE, PRICE_OVERRIDE_REASON)
                VALUES
                    (@ORDER_ID, @LINE, @NAME, @PURITY, 1, @WEIGHT,
                     @GOLD_PRICE, @LABOR, @STONE, @TOTAL, @TOTAL,
                     @PRODUCT, @SOURCE_GOLD, @APPLIED_GOLD,
                     @SOURCE_LABOR, @APPLIED_LABOR,
                     @SOURCE_STONE, @APPLIED_STONE,
                     @OVERRIDE, @REASON)
                """, connection, transaction);
            insertLine.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
            insertLine.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNumber;
            insertLine.Parameters.Add("@NAME", FbDbType.VarChar, 255).Value = line.ProductName;
            insertLine.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value = line.GoldPurity;
            insertLine.Parameters.Add("@WEIGHT", FbDbType.Decimal).Value = line.Pricing.GoldWeightChi;
            insertLine.Parameters.Add("@GOLD_PRICE", FbDbType.Decimal).Value = line.Draft.AppliedGoldPricePerChi;
            insertLine.Parameters.Add("@LABOR", FbDbType.Decimal).Value = line.Draft.AppliedLaborAmountVnd;
            insertLine.Parameters.Add("@STONE", FbDbType.Decimal).Value = line.Draft.AppliedStoneAmountVnd;
            insertLine.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = line.Amounts.TotalAmountVnd;
            insertLine.Parameters.Add("@PRODUCT", FbDbType.BigInt).Value = line.Draft.FinishedProductId;
            insertLine.Parameters.Add("@SOURCE_GOLD", FbDbType.Decimal).Value = line.Pricing.SourceGoldPricePerChi;
            insertLine.Parameters.Add("@APPLIED_GOLD", FbDbType.Decimal).Value = line.Draft.AppliedGoldPricePerChi;
            insertLine.Parameters.Add("@SOURCE_LABOR", FbDbType.Decimal).Value = line.Pricing.SourceLaborAmountVnd;
            insertLine.Parameters.Add("@APPLIED_LABOR", FbDbType.Decimal).Value = line.Draft.AppliedLaborAmountVnd;
            insertLine.Parameters.Add("@SOURCE_STONE", FbDbType.Decimal).Value = line.Pricing.SourceStoneAmountVnd;
            insertLine.Parameters.Add("@APPLIED_STONE", FbDbType.Decimal).Value = line.Draft.AppliedStoneAmountVnd;
            insertLine.Parameters.Add("@OVERRIDE", FbDbType.Boolean).Value = line.Amounts.IsPriceOverride;
            insertLine.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value =
                OverrideReasonOrDbNull(line.Amounts.IsPriceOverride, line.Draft.OverrideReason);
            await insertLine.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var link = new FbCommand(
            "UPDATE SALES_CHECKOUTS SET SALES_ORDER_ID = @ORDER_ID WHERE ID = @ID",
            connection, transaction))
        {
            link.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
            link.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
            await link.ExecuteNonQueryAsync(cancellationToken);
        }

        return orderId;
    }

    public static async Task<OldGoldPostResult> PostOldGoldReceiptAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long checkoutId,
        SalesCheckoutDraft draft,
        PreparedCheckout prepared,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (prepared.OldGoldLines.Count == 0)
            throw new InvalidOperationException("Không có dòng vàng cũ để nhập kho.");
        var destinationId = draft.OldGoldDestinationLocationId
            ?? throw new InvalidOperationException("Thiếu kho nhập vàng cũ.");

        var total = prepared.Totals.OldGoldTotalVnd;
        var code = await StockPostingSupport.NextDocumentCodeAsync(
            connection, transaction, "STOCK_RECEIPT", session.BranchId, "PNK", cancellationToken);

        long documentId;
        await using (var insertDoc = new FbCommand("""
            INSERT INTO STOCK_DOCUMENTS
                (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                 DESTINATION_LOCATION_ID, PURPOSE_CODE, SOURCE_TYPE, CUSTOMER_ID,
                 NOTES, STATUS_CODE, TOTAL_AMOUNT_VND, PAID_AMOUNT_VND, DEBT_AMOUNT_VND,
                 CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
            VALUES
                (@CID, @BID, @CODE, 'RECEIPT', @DATE,
                 @LOC, 'OLD_GOLD_PURCHASE', @SOURCE, @CUS,
                 @NOTES, 'POSTED', @TOTAL, @TOTAL, 0,
                 @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
            RETURNING ID
            """, connection, transaction))
        {
            insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
            insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
            insertDoc.Parameters.Add("@LOC", FbDbType.BigInt).Value = destinationId;
            insertDoc.Parameters.Add("@SOURCE", FbDbType.VarChar, 30).Value = StockSourceTypes.OldGoldCustomer;
            insertDoc.Parameters.Add("@CUS", FbDbType.BigInt).Value = draft.CustomerId;
            insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value = DbText(draft.Notes);
            insertDoc.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = total;
            insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
        }

        var lineNumber = 0;
        foreach (var line in prepared.OldGoldLines)
        {
            lineNumber++;
            var quantity = line.Amounts.GoldWeightChi;
            var unitCost = quantity > 0
                ? decimal.Round(line.Amounts.AmountVnd / quantity, 4, MidpointRounding.AwayFromZero)
                : line.Amounts.AmountVnd;

            long stockLineId;
            await using (var insertLine = new FbCommand("""
                INSERT INTO STOCK_DOCUMENT_LINES
                    (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                     STANDARD_UNIT_CODE, CURRENCY_CODE, EXCHANGE_RATE, UNIT_PRICE, AMOUNT_VND, NOTES)
                VALUES
                    (@DOC, @LN, @MID, @QTY, @COST, @AMT,
                     @UNIT, 'VND', 1, @PRICE, @AVND, @NOTES)
                RETURNING ID
                """, connection, transaction))
            {
                insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNumber;
                insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = line.Draft.MaterialId;
                insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = quantity;
                insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                insertLine.Parameters.Add("@AMT", FbDbType.Decimal).Value = line.Amounts.AmountVnd;
                insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = line.UnitCode;
                insertLine.Parameters.Add("@PRICE", FbDbType.Decimal).Value = line.Draft.AppliedBuyPricePerChi;
                insertLine.Parameters.Add("@AVND", FbDbType.Decimal).Value = line.Amounts.AmountVnd;
                insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = DbText(line.Draft.Notes);
                stockLineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
            }

            await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                connection, transaction, line.Draft.MaterialId, destinationId,
                quantity, unitCost, cancellationToken);
            await StockPostingSupport.InsertMovementAsync(
                connection, transaction, documentId, stockLineId, line.Draft.MaterialId,
                destinationId, "IN", quantity, unitCost, session.UserId, cancellationToken);

            await using var insertSnapshot = new FbCommand("""
                INSERT INTO SALES_OLD_GOLD_LINES
                    (CHECKOUT_ID, LINE_NUMBER, MATERIAL_ID, STOCK_DOCUMENT_LINE_ID,
                     ITEM_QUANTITY, TOTAL_WEIGHT_CHI, STONE_WEIGHT_CHI, IMPURITY_WEIGHT_CHI,
                     GOLD_WEIGHT_CHI, VALUE_PERCENT, CONVERTED_WEIGHT_CHI,
                     SOURCE_BUY_PRICE_PER_CHI, APPLIED_BUY_PRICE_PER_CHI, AMOUNT_VND,
                     IS_PRICE_OVERRIDE, PRICE_OVERRIDE_REASON, NOTES)
                VALUES
                    (@CHECKOUT, @LINE, @MATERIAL, @STOCK_LINE,
                     @QTY, @TOTAL_W, @STONE, @IMPURITY,
                     @GOLD, @PERCENT, @CONVERTED,
                     @SOURCE, @APPLIED, @AMOUNT,
                     @OVERRIDE, @REASON, @NOTES)
                """, connection, transaction);
            insertSnapshot.Parameters.Add("@CHECKOUT", FbDbType.BigInt).Value = checkoutId;
            insertSnapshot.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNumber;
            insertSnapshot.Parameters.Add("@MATERIAL", FbDbType.BigInt).Value = line.Draft.MaterialId;
            insertSnapshot.Parameters.Add("@STOCK_LINE", FbDbType.BigInt).Value = stockLineId;
            insertSnapshot.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Draft.ItemQuantity;
            insertSnapshot.Parameters.Add("@TOTAL_W", FbDbType.Decimal).Value = line.Draft.TotalWeightChi;
            insertSnapshot.Parameters.Add("@STONE", FbDbType.Decimal).Value = line.Draft.StoneWeightChi;
            insertSnapshot.Parameters.Add("@IMPURITY", FbDbType.Decimal).Value = line.Draft.ImpurityWeightChi;
            insertSnapshot.Parameters.Add("@GOLD", FbDbType.Decimal).Value = line.Amounts.GoldWeightChi;
            insertSnapshot.Parameters.Add("@PERCENT", FbDbType.Decimal).Value = line.Draft.ValuePercent;
            insertSnapshot.Parameters.Add("@CONVERTED", FbDbType.Decimal).Value = line.Amounts.ConvertedWeightChi;
            insertSnapshot.Parameters.Add("@SOURCE", FbDbType.Decimal).Value = line.SourceBuyPricePerChi;
            insertSnapshot.Parameters.Add("@APPLIED", FbDbType.Decimal).Value = line.Draft.AppliedBuyPricePerChi;
            insertSnapshot.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = line.Amounts.AmountVnd;
            insertSnapshot.Parameters.Add("@OVERRIDE", FbDbType.Boolean).Value = line.IsPriceOverride;
            insertSnapshot.Parameters.Add("@REASON", FbDbType.VarChar, 500).Value =
                OverrideReasonOrDbNull(line.IsPriceOverride, line.Draft.OverrideReason);
            insertSnapshot.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = DbText(line.Draft.Notes);
            await insertSnapshot.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var link = new FbCommand(
            "UPDATE SALES_CHECKOUTS SET OLD_GOLD_STOCK_DOCUMENT_ID = @DOC WHERE ID = @ID",
            connection, transaction))
        {
            link.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
            link.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
            await link.ExecuteNonQueryAsync(cancellationToken);
        }

        return new OldGoldPostResult(documentId, code, total);
    }

    public static async Task<long?> PostOffsetAsync(
        FbConnection connection,
        FbTransaction transaction,
        long checkoutId,
        long salesOrderId,
        long stockDocumentId,
        decimal amountVnd,
        CancellationToken cancellationToken)
    {
        if (amountVnd <= 0)
            return null;

        await using var insert = new FbCommand("""
            INSERT INTO SALES_CHECKOUT_OFFSETS
                (CHECKOUT_ID, SALES_ORDER_ID, STOCK_DOCUMENT_ID, AMOUNT_VND)
            VALUES
                (@CHECKOUT, @ORDER, @DOC, @AMOUNT)
            RETURNING ID
            """, connection, transaction);
        insert.Parameters.Add("@CHECKOUT", FbDbType.BigInt).Value = checkoutId;
        insert.Parameters.Add("@ORDER", FbDbType.BigInt).Value = salesOrderId;
        insert.Parameters.Add("@DOC", FbDbType.BigInt).Value = stockDocumentId;
        insert.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = amountVnd;
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
    }

    public static async Task<IReadOnlyList<long>> PostPayoutAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long checkoutId,
        OldGoldPostResult oldGold,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var payout = allocations.Sum(x => x.AmountVnd);
        if (payout == 0)
            return Array.Empty<long>();

        if (allocations.Any(x => string.Equals(
                x.PaymentMethod?.Trim(), PaymentMethods.Card, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Không được dùng thẻ cho khoản chi.");

        var vouchers = new FirebirdFinancialVoucherPostingService();
        var voucherIds = new List<long>();
        var line = 0;
        foreach (var allocation in allocations)
        {
            line++;
            long paymentRowId;
            await using (var insertPay = new FbCommand("""
                INSERT INTO STOCK_DOCUMENT_PAYMENTS
                    (DOCUMENT_ID, LINE_NUMBER, PAYMENT_METHOD, AMOUNT_VND,
                     CASH_FUND_ID, BANK_ACCOUNT_ID, REFERENCE_NUMBER)
                VALUES
                    (@DOC, @LN, @METHOD, @AMT, @FUND, @BANK, @REF)
                RETURNING ID
                """, connection, transaction))
            {
                insertPay.Parameters.Add("@DOC", FbDbType.BigInt).Value = oldGold.StockDocumentId;
                insertPay.Parameters.Add("@LN", FbDbType.Integer).Value = line;
                insertPay.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value =
                    allocation.PaymentMethod.Trim().ToUpperInvariant();
                insertPay.Parameters.Add("@AMT", FbDbType.Decimal).Value = allocation.AmountVnd;
                insertPay.Parameters.Add("@FUND", FbDbType.BigInt).Value =
                    allocation.CashFundId is > 0 ? allocation.CashFundId.Value : DBNull.Value;
                insertPay.Parameters.Add("@BANK", FbDbType.BigInt).Value =
                    allocation.BankAccountId is > 0 ? allocation.BankAccountId.Value : DBNull.Value;
                insertPay.Parameters.Add("@REF", FbDbType.VarChar, 100).Value =
                    (object?)allocation.ReferenceNumber ?? DBNull.Value;
                paymentRowId = Convert.ToInt64(await insertPay.ExecuteScalarAsync(cancellationToken));
            }

            var voucher = await vouchers.PostPaymentInTransactionAsync(
                connection, transaction, companyId, session.BranchId,
                new FinancialVoucherPostRequest(
                    allocation,
                    "STOCK_DOCUMENT",
                    oldGold.StockDocumentId,
                    FinancialVoucherSourceAllocationTypes.StockDocumentPayment,
                    paymentRowId,
                    $"Sales checkout payout {checkoutId}"),
                session.UserId,
                cancellationToken);

            await using var link = new FbCommand("""
                UPDATE STOCK_DOCUMENT_PAYMENTS
                   SET CASH_VOUCHER_ID = @VID
                 WHERE ID = @ID
                """, connection, transaction);
            link.Parameters.Add("@VID", FbDbType.BigInt).Value = voucher.VoucherId;
            link.Parameters.Add("@ID", FbDbType.BigInt).Value = paymentRowId;
            await link.ExecuteNonQueryAsync(cancellationToken);
            voucherIds.Add(voucher.VoucherId);
        }

        return voucherIds;
    }

    public static async Task<FinancialSettlementResult?> PostReceiptAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long checkoutId,
        SalePostResult sale,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var receiptAmount = allocations.Sum(x => x.AmountVnd);
        if (receiptAmount == 0)
            return null;
        if (sale.SalesOrderId is null)
            throw new InvalidOperationException("Không thể thu tiền khi thiếu đơn bán.");

        var settlement = await new FirebirdFinancialSettlementService().PostInTransactionAsync(
            connection, transaction, companyId, session.BranchId,
            new FinancialSettlementDraft(
                SettlementTypes.Receipt, SettlementPartyTypes.Customer, sale.CustomerId,
                sale.DocumentDate, allocations,
                [new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.SalesOrder, sale.SalesOrderId.Value, receiptAmount)],
                $"Sales checkout {sale.CheckoutCode}"),
            session.UserId, cancellationToken);

        var allocationRows = new List<SettlementAllocationRow>();
        await using (var query = new FbCommand("""
            SELECT ID, PAYMENT_METHOD, AMOUNT_VND, REFERENCE_NUMBER, CASH_VOUCHER_ID
            FROM FINANCIAL_SETTLEMENT_ALLOCATIONS
            WHERE SETTLEMENT_ID = @ID
            ORDER BY LINE_NUMBER
            """, connection, transaction))
        {
            query.Parameters.Add("@ID", FbDbType.BigInt).Value = settlement.SettlementId;
            await using var reader = await query.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                allocationRows.Add(new SettlementAllocationRow(
                    reader.GetInt64(0), reader.GetString(1), reader.GetDecimal(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetInt64(4)));
        }

        foreach (var allocation in allocationRows)
        {
            await using var insert = new FbCommand("""
                INSERT INTO ORDER_PAYMENTS
                    (ORDER_ID, PAYMENT_DATE, PAYMENT_TYPE, AMOUNT, PAYMENT_METHOD,
                     REFERENCE_NO, NOTES, IS_VOIDED, CREATED_BY, CREATED_AT,
                     FINANCIAL_SETTLEMENT_ID, FINANCIAL_SETTLEMENT_ALLOCATION_ID, CASH_VOUCHER_ID)
                VALUES
                    (@ORDER_ID, @DATE, 'PAYMENT', @AMOUNT, @METHOD,
                     @REFERENCE, @NOTES, FALSE, @BY, CURRENT_TIMESTAMP,
                     @SETTLEMENT, @ALLOCATION, @VOUCHER)
                """, connection, transaction);
            insert.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = sale.SalesOrderId.Value;
            insert.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = sale.DocumentDate;
            insert.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = allocation.AmountVnd;
            insert.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value = allocation.PaymentMethod;
            insert.Parameters.Add("@REFERENCE", FbDbType.VarChar, 100).Value =
                (object?)allocation.ReferenceNumber ?? settlement.Code;
            insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = $"Sales checkout {sale.CheckoutCode}";
            insert.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            insert.Parameters.Add("@SETTLEMENT", FbDbType.BigInt).Value = settlement.SettlementId;
            insert.Parameters.Add("@ALLOCATION", FbDbType.BigInt).Value = allocation.Id;
            insert.Parameters.Add("@VOUCHER", FbDbType.BigInt).Value = allocation.CashVoucherId;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var update = new FbCommand(
            "UPDATE SALES_CHECKOUTS SET FINANCIAL_SETTLEMENT_ID = @SID WHERE ID = @ID",
            connection, transaction);
        update.Parameters.Add("@SID", FbDbType.BigInt).Value = settlement.SettlementId;
        update.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
        await update.ExecuteNonQueryAsync(cancellationToken);
        return settlement;
    }

    public static async Task<long?> CreateReceivableAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long checkoutId,
        SalePostResult sale,
        decimal amountVnd,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        if (amountVnd == 0)
            return null;
        if (sale.SalesOrderId is null)
            throw new InvalidOperationException("Không thể tạo công nợ khi thiếu đơn bán.");

        var code = await StockPostingSupport.NextDocumentCodeAsync(
            connection, transaction, "RECEIVABLE_DEBT", session.BranchId, "PTU", cancellationToken);
        long debtId;
        await using (var insert = new FbCommand("""
            INSERT INTO RECEIVABLE_DEBTS
                (COMPANY_ID, BRANCH_ID, CODE, PARTY_TYPE, CUSTOMER_ID,
                 ORIGIN_DOCUMENT_TYPE, ORIGIN_DOCUMENT_ID, DEBT_KIND,
                 ORIGINAL_AMOUNT_VND, COLLECTED_AMOUNT_VND, REMAINING_AMOUNT_VND,
                 STATUS_CODE, NOTES, CREATED_BY, CREATED_AT)
            VALUES
                (@CID, @BID, @CODE, 'CUSTOMER', @CUSTOMER,
                 'SALES_ORDER', @ORDER_ID, 'SALES',
                 @AMOUNT, 0, @AMOUNT,
                 'OPEN', @NOTES, @BY, CURRENT_TIMESTAMP)
            RETURNING ID
            """, connection, transaction))
        {
            insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
            insert.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = sale.CustomerId;
            insert.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = sale.SalesOrderId.Value;
            insert.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = amountVnd;
            insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = $"Sales checkout {sale.CheckoutCode}";
            insert.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            debtId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        }

        await using var update = new FbCommand(
            "UPDATE SALES_CHECKOUTS SET RECEIVABLE_DEBT_ID = @DEBT WHERE ID = @ID",
            connection, transaction);
        update.Parameters.Add("@DEBT", FbDbType.BigInt).Value = debtId;
        update.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
        await update.ExecuteNonQueryAsync(cancellationToken);
        return debtId;
    }

    public static string ComputeRequestHash(SalesCheckoutDraft draft)
    {
        var canonical = new StringBuilder();
        Add(canonical, draft.ClientRequestId.ToString("D"));
        Add(canonical, draft.DocumentDate.ToString("O", CultureInfo.InvariantCulture));
        Add(canonical, draft.CustomerId);
        Add(canonical, draft.SalespersonId);
        Add(canonical, draft.SourceLocationId);
        Add(canonical, draft.OldGoldDestinationLocationId);
        Add(canonical, draft.DiscountAmountVnd);
        Add(canonical, draft.ReceivableAmountVnd);
        Add(canonical, draft.Notes);
        Add(canonical, draft.SaleLines.Count);
        foreach (var line in draft.SaleLines)
        {
            Add(canonical, line.FinishedProductId);
            Add(canonical, line.AppliedGoldPricePerChi);
            Add(canonical, line.AppliedLaborAmountVnd);
            Add(canonical, line.AppliedStoneAmountVnd);
            Add(canonical, line.OverrideReason);
        }
        Add(canonical, draft.OldGoldLines.Count);
        foreach (var line in draft.OldGoldLines)
        {
            Add(canonical, line.MaterialId);
            Add(canonical, line.ItemQuantity);
            Add(canonical, line.TotalWeightChi);
            Add(canonical, line.StoneWeightChi);
            Add(canonical, line.ImpurityWeightChi);
            Add(canonical, line.ValuePercent);
            Add(canonical, line.AppliedBuyPricePerChi);
            Add(canonical, line.OverrideReason);
            Add(canonical, line.Notes);
        }
        Add(canonical, draft.Allocations.Count);
        foreach (var allocation in draft.Allocations)
        {
            Add(canonical, allocation.PaymentMethod);
            Add(canonical, allocation.AmountVnd);
            Add(canonical, allocation.CashFundId);
            Add(canonical, allocation.BankAccountId);
            Add(canonical, allocation.ReferenceNumber);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    public static async Task WriteAuditAsync(
        FbConnection connection, FbTransaction transaction, string eventType,
        AuthenticatedSession session, long checkoutId, string eventData,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            INSERT INTO AUDIT_LOG
                (USER_ID, USERNAME, BRANCH_ID, EVENT_TYPE, ENTITY_TYPE, ENTITY_ID,
                 EVENT_DATA, MACHINE_NAME)
            VALUES
                (@USER_ID, @USERNAME, @BRANCH_ID, @EVENT_TYPE, 'SALES_CHECKOUT', @ENTITY_ID,
                 @EVENT_DATA, @MACHINE_NAME)
            """, connection, transaction);
        command.Parameters.Add("@USER_ID", FbDbType.BigInt).Value = session.UserId;
        command.Parameters.Add("@USERNAME", FbDbType.VarChar, 100).Value = session.Username;
        command.Parameters.Add("@BRANCH_ID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@EVENT_TYPE", FbDbType.VarChar, 100).Value = eventType;
        command.Parameters.Add("@ENTITY_ID", FbDbType.VarChar, 100).Value =
            checkoutId.ToString(CultureInfo.InvariantCulture);
        command.Parameters.Add("@EVENT_DATA", FbDbType.VarChar, 4000).Value = eventData;
        command.Parameters.Add("@MACHINE_NAME", FbDbType.VarChar, 255).Value = Environment.MachineName;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> LoadCheckoutCodeAsync(
        FbConnection connection, FbTransaction transaction, long checkoutId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            "SELECT CODE FROM SALES_CHECKOUTS WHERE ID = @ID", connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = checkoutId;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<CustomerState> LoadCustomerAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        long customerId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT IS_WALK_IN, NAME, PHONE
            FROM CUSTOMERS
            WHERE ID = @ID AND COMPANY_ID = @CID AND IS_ACTIVE = TRUE
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = customerId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Khách hàng không còn hoạt động hoặc không thuộc công ty.");
        return new CustomerState(reader.GetBoolean(0));
    }

    private static async Task EnsureSalespersonAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        long branchId, long salespersonId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM USERS U
            JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
            WHERE U.ID = @ID AND U.COMPANY_ID = @CID AND U.IS_ACTIVE = TRUE
              AND (U.DEFAULT_BRANCH_ID = @BID OR EXISTS (
                  SELECT 1 FROM USER_SCOPES S
                  WHERE S.USER_ID = U.ID AND S.SCOPE_TYPE = 'BRANCH'
                    AND S.SCOPE_ID = @BID AND S.CAN_ACCESS = TRUE))
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salespersonId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Nhân viên bán hàng không hợp lệ cho chi nhánh.");
    }

    private static async Task EnsureSalesLocationAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        long branchId, long locationId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM LOCATIONS L
            JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @CID
            WHERE L.ID = @ID AND L.BRANCH_ID = @BID AND L.IS_ACTIVE = TRUE
              AND L.INVENTORY_SCOPE IN ('SALES_FLOOR', 'FINISHED_GOODS')
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho xuất bán không hợp lệ hoặc không còn hoạt động.");
    }

    private static async Task EnsureOldGoldLocationAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        long branchId, long locationId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM LOCATIONS L
            JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @CID
            WHERE L.ID = @ID AND L.BRANCH_ID = @BID AND L.IS_ACTIVE = TRUE
              AND L.INVENTORY_SCOPE = 'RAW_MATERIAL'
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho nhập vàng cũ không hợp lệ hoặc không còn hoạt động.");
    }

    private static async Task<ProductState> LoadProductAsync(
        FbConnection connection, FbTransaction transaction, long companyId, long branchId,
        long sourceLocationId, long productId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT F.ID, F.CODE, F.PRODUCT_NAME, COALESCE(F.GOLD_PURITY, ''),
                   F.GOLD_WEIGHT, F.TOTAL_WEIGHT, GP.SELL_PRICE_PER_CHI,
                   F.LABOR_COST, F.NON_GOLD_MATERIAL_COST
            FROM FINISHED_PRODUCTS F
            JOIN LOCATIONS L ON L.ID = F.LOCATION_ID AND L.BRANCH_ID = @BID
                            AND L.INVENTORY_SCOPE IN ('SALES_FLOOR', 'FINISHED_GOODS')
                            AND L.IS_ACTIVE = TRUE
            JOIN GOLD_PRICES GP ON GP.COMPANY_ID = F.COMPANY_ID AND GP.IS_ACTIVE = TRUE
                               AND GP.ID = COALESCE(
                                    F.RETAIL_GOLD_PRICE_ID,
                                    (SELECT MIN(P.ID)
                                     FROM GOLD_PRICES P
                                     WHERE P.COMPANY_ID = F.COMPANY_ID
                                       AND P.IS_ACTIVE = TRUE
                                       AND UPPER(TRIM(P.CODE)) = UPPER(TRIM(COALESCE(F.GOLD_PURITY, '')))))
            WHERE F.ID = @ID AND F.COMPANY_ID = @CID AND F.BRANCH_ID = @BID
              AND F.LOCATION_ID = @LOCATION
              AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = productId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        command.Parameters.Add("@LOCATION", FbDbType.BigInt).Value = sourceLocationId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Thành phẩm #{productId} không còn đủ điều kiện bán.");
        return new ProductState(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetDecimal(4) > 0 ? reader.GetDecimal(4) : reader.GetDecimal(5),
            reader.GetDecimal(6), reader.GetDecimal(7), reader.GetDecimal(8));
    }

    private static async Task<OldGoldMaterialState> LoadOldGoldMaterialAsync(
        FbConnection connection, FbTransaction transaction, long companyId,
        long materialId, CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT M.CODE, M.NAME, M.MATERIAL_TYPE, M.UNIT_CODE, GP.BUY_PRICE_PER_CHI
            FROM MATERIALS M
            JOIN GOLD_PRICES GP ON GP.ID = M.OLD_GOLD_PRICE_ID
                               AND GP.COMPANY_ID = M.COMPANY_ID AND GP.IS_ACTIVE = TRUE
            WHERE M.ID = @ID AND M.COMPANY_ID = @CID AND M.IS_ACTIVE = TRUE
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = materialId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Vàng cũ #{materialId} không còn đủ điều kiện thu mua.");
        return new OldGoldMaterialState(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetDecimal(4));
    }

    private static async Task<string> NextCheckoutCodeAsync(
        FbConnection connection, FbTransaction transaction, long branchId,
        DateTime documentDate, string checkoutType, CancellationToken cancellationToken)
    {
        var isOldGoldOnly = checkoutType == SalesCheckoutTypes.OldGoldPurchase;
        var documentType = isOldGoldOnly ? "SALES_OLD_GOLD_PURCHASE" : "SALES_CHECKOUT";
        var prefix = isOldGoldOnly ? "MVC" : "MB";
        var year = documentDate.Year;
        await using (var ensure = new FbCommand("""
            INSERT INTO DOCUMENT_SEQUENCES (DOCUMENT_TYPE, BRANCH_ID, YEAR_NO, LAST_NUMBER, UPDATED_AT)
            SELECT @TYPE, @BID, @YEAR, 0, CURRENT_TIMESTAMP
            FROM RDB$DATABASE
            WHERE NOT EXISTS (SELECT 1 FROM DOCUMENT_SEQUENCES
                              WHERE DOCUMENT_TYPE = @TYPE
                                AND BRANCH_ID = @BID AND YEAR_NO = @YEAR)
            """, connection, transaction))
        {
            ensure.Parameters.Add("@TYPE", FbDbType.VarChar, 40).Value = documentType;
            ensure.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            ensure.Parameters.Add("@YEAR", FbDbType.Integer).Value = year;
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var update = new FbCommand("""
            UPDATE DOCUMENT_SEQUENCES
               SET LAST_NUMBER = LAST_NUMBER + 1, UPDATED_AT = CURRENT_TIMESTAMP
             WHERE DOCUMENT_TYPE = @TYPE AND BRANCH_ID = @BID AND YEAR_NO = @YEAR
            RETURNING LAST_NUMBER
            """, connection, transaction);
        update.Parameters.Add("@TYPE", FbDbType.VarChar, 40).Value = documentType;
        update.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        update.Parameters.Add("@YEAR", FbDbType.Integer).Value = year;
        var number = Convert.ToInt32(await update.ExecuteScalarAsync(cancellationToken));
        return $"{prefix}{year % 100:D2}/{number:D5}";
    }

    private static object DbText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static object OverrideReasonOrDbNull(bool isOverride, string? reason) =>
        isOverride ? DbText(reason) : DBNull.Value;

    private static void Add(StringBuilder builder, object? value)
    {
        var text = value switch
        {
            null => "<null>",
            decimal number => number.ToString("G29", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
        builder.Append(text.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(text).Append('|');
    }

    private sealed record CustomerState(bool IsWalkIn);
    private sealed record ProductState(
        long Id, string Code, string Name, string GoldPurity, decimal GoldWeightChi,
        decimal SourceGoldPricePerChi, decimal SourceLaborAmountVnd, decimal SourceStoneAmountVnd);
    private sealed record OldGoldMaterialState(
        string Code, string Name, string MaterialType, string UnitCode, decimal SourceBuyPricePerChi);
    private sealed record SettlementAllocationRow(
        long Id, string PaymentMethod, decimal AmountVnd, string? ReferenceNumber, long CashVoucherId);
}
