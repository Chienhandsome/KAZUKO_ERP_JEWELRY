using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Inventory;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdMaterialReceiptService : IMaterialReceiptService
{
    private readonly FirebirdFinancialVoucherPostingService _vouchers = new();

    public async Task<MaterialReceiptPostResult> CreateDraftAndPostAsync(
        MaterialReceiptDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = MaterialReceiptValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var total = MaterialReceiptValidator.ComputeTotalAmountVnd(draft.Lines);
        var debt = total - draft.PaidAmountVnd;
        var source = draft.SourceType.Trim().ToUpperInvariant();
        var purpose = source == StockSourceTypes.OldGoldCustomer ? "OLD_GOLD_PURCHASE" : "SUPPLIER_PURCHASE";

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var materialTypes = new List<string>();
            foreach (var line in draft.Lines)
            {
                var material = await StockPostingSupport.GetMaterialAsync(
                    connection, fbTx, companyId, line.MaterialId, cancellationToken);
                materialTypes.Add(material.MaterialType);
            }

            FinishedGoodsIssueValidator.EnsureMaterialsAreRaw(materialTypes);
            FinishedGoodsIssueValidator.EnsureNotManualFinishedGoodsReceipt(
                StockDocumentTypes.Receipt, purpose, null, isMaterialOnlyReceipt: true);

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "STOCK_RECEIPT", session.BranchId, "PNK", cancellationToken);

            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     DESTINATION_LOCATION_ID, PURPOSE_CODE, SOURCE_TYPE, SUPPLIER_ID, CUSTOMER_ID,
                     NOTES, STATUS_CODE, TOTAL_AMOUNT_VND, PAID_AMOUNT_VND, DEBT_AMOUNT_VND,
                     CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'RECEIPT', @DATE,
                     @LOC, @PURPOSE, @SOURCE, @SUP, @CUS,
                     @NOTES, 'POSTED', @TOTAL, @PAID, @DEBT,
                     @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@LOC", FbDbType.BigInt).Value = draft.DestinationLocationId;
                insertDoc.Parameters.Add("@PURPOSE", FbDbType.VarChar, 40).Value = purpose;
                insertDoc.Parameters.Add("@SOURCE", FbDbType.VarChar, 30).Value = source;
                insertDoc.Parameters.Add("@SUP", FbDbType.BigInt).Value =
                    draft.SupplierId is > 0 ? draft.SupplierId.Value : DBNull.Value;
                insertDoc.Parameters.Add("@CUS", FbDbType.BigInt).Value =
                    draft.CustomerId is > 0 ? draft.CustomerId.Value : DBNull.Value;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    (object?)draft.Notes ?? DBNull.Value;
                insertDoc.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = total;
                insertDoc.Parameters.Add("@PAID", FbDbType.Decimal).Value = draft.PaidAmountVnd;
                insertDoc.Parameters.Add("@DEBT", FbDbType.Decimal).Value = debt;
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;

                var documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));

                var lineNumber = 0;
                foreach (var line in draft.Lines)
                {
                    lineNumber++;
                    var material = await StockPostingSupport.GetMaterialAsync(
                        connection, fbTx, companyId, line.MaterialId, cancellationToken);
                    var standardUnit = StockUnitConverter.StandardUnitFor(material.MaterialType, material.UnitCode);
                    var amountVnd = MaterialReceiptValidator.ComputeLineAmountVnd(line);
                    var unitCost = line.ExchangeRate == 0
                        ? line.UnitPrice
                        : line.UnitPrice * line.ExchangeRate;

                    long lineId;
                    await using (var insertLine = new FbCommand("""
                        INSERT INTO STOCK_DOCUMENT_LINES
                            (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                             STANDARD_UNIT_CODE, CURRENCY_CODE, EXCHANGE_RATE, UNIT_PRICE, AMOUNT_VND, NOTES)
                        VALUES
                            (@DOC, @LN, @MID, @QTY, @COST, @AMT,
                             @UNIT, @CUR, @RATE, @PRICE, @AVND, @NOTES)
                        RETURNING ID
                        """, connection, fbTx))
                    {
                        insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                        insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNumber;
                        insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                        insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                        insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                        insertLine.Parameters.Add("@AMT", FbDbType.Decimal).Value = amountVnd;
                        insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = standardUnit;
                        insertLine.Parameters.Add("@CUR", FbDbType.VarChar, 10).Value =
                            string.IsNullOrWhiteSpace(line.CurrencyCode) ? "VND" : line.CurrencyCode.Trim().ToUpperInvariant();
                        insertLine.Parameters.Add("@RATE", FbDbType.Decimal).Value = line.ExchangeRate;
                        insertLine.Parameters.Add("@PRICE", FbDbType.Decimal).Value = line.UnitPrice;
                        insertLine.Parameters.Add("@AVND", FbDbType.Decimal).Value = amountVnd;
                        insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                            (object?)line.Notes ?? DBNull.Value;
                        lineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                    }

                    await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                        connection, fbTx, line.MaterialId, draft.DestinationLocationId,
                        line.Quantity, unitCost, cancellationToken);

                    await StockPostingSupport.InsertMovementAsync(
                        connection, fbTx, documentId, lineId, line.MaterialId,
                        draft.DestinationLocationId, "IN", line.Quantity, unitCost,
                        session.UserId, cancellationToken);
                }

                var payLine = 0;
                foreach (var payment in draft.Payments ?? Array.Empty<PaymentAllocationDraft>())
                {
                    payLine++;
                    long paymentRowId;
                    await using (var insertPay = new FbCommand("""
                        INSERT INTO STOCK_DOCUMENT_PAYMENTS
                            (DOCUMENT_ID, LINE_NUMBER, PAYMENT_METHOD, AMOUNT_VND,
                             CASH_FUND_ID, BANK_ACCOUNT_ID, REFERENCE_NUMBER)
                        VALUES
                            (@DOC, @LN, @METHOD, @AMT, @FUND, @BANK, @REF)
                        RETURNING ID
                        """, connection, fbTx))
                    {
                        insertPay.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                        insertPay.Parameters.Add("@LN", FbDbType.Integer).Value = payLine;
                        insertPay.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value =
                            payment.PaymentMethod.Trim().ToUpperInvariant();
                        insertPay.Parameters.Add("@AMT", FbDbType.Decimal).Value = payment.AmountVnd;
                        insertPay.Parameters.Add("@FUND", FbDbType.BigInt).Value =
                            payment.CashFundId is > 0 ? payment.CashFundId.Value : DBNull.Value;
                        insertPay.Parameters.Add("@BANK", FbDbType.BigInt).Value =
                            payment.BankAccountId is > 0 ? payment.BankAccountId.Value : DBNull.Value;
                        insertPay.Parameters.Add("@REF", FbDbType.VarChar, 100).Value =
                            (object?)payment.ReferenceNumber ?? DBNull.Value;
                        paymentRowId = Convert.ToInt64(await insertPay.ExecuteScalarAsync(cancellationToken));
                    }

                    var voucher = await _vouchers.PostPaymentInTransactionAsync(
                        connection, fbTx, companyId, session.BranchId,
                        new FinancialVoucherPostRequest(
                            payment,
                            "STOCK_DOCUMENT",
                            documentId,
                            FinancialVoucherSourceAllocationTypes.StockDocumentPayment,
                            paymentRowId),
                        session.UserId,
                        cancellationToken);

                    await using var linkVoucher = new FbCommand("""
                        UPDATE STOCK_DOCUMENT_PAYMENTS
                           SET CASH_VOUCHER_ID = @VID
                         WHERE ID = @ID
                        """, connection, fbTx);
                    linkVoucher.Parameters.Add("@VID", FbDbType.BigInt).Value = voucher.VoucherId;
                    linkVoucher.Parameters.Add("@ID", FbDbType.BigInt).Value = paymentRowId;
                    await linkVoucher.ExecuteNonQueryAsync(cancellationToken);
                }

                long? payableId = null;
                if (debt > 0)
                {
                    var debtCode = await StockPostingSupport.NextDocumentCodeAsync(
                        connection, fbTx, "PAYABLE_DEBT", session.BranchId, "CN", cancellationToken);
                    var partyType = source == StockSourceTypes.OldGoldCustomer
                        ? PayablePartyTypes.Customer
                        : PayablePartyTypes.Supplier;

                    await using var insertDebt = new FbCommand("""
                        INSERT INTO PAYABLE_DEBTS
                            (COMPANY_ID, BRANCH_ID, CODE, PARTY_TYPE, SUPPLIER_ID, CUSTOMER_ID,
                             ORIGIN_DOCUMENT_TYPE, ORIGIN_DOCUMENT_ID, DEBT_KIND,
                             ORIGINAL_AMOUNT_VND, PAID_AMOUNT_VND, REMAINING_AMOUNT_VND,
                             STATUS_CODE, CREATED_BY, CREATED_AT)
                        VALUES
                            (@CID, @BID, @CODE, @PARTY, @SUP, @CUS,
                             'STOCK_DOCUMENT', @DOC, 'PURCHASE',
                             @ORIG, 0, @ORIG, 'OPEN', @BY, CURRENT_TIMESTAMP)
                        RETURNING ID
                        """, connection, fbTx);
                    insertDebt.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                    insertDebt.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                    insertDebt.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = debtCode;
                    insertDebt.Parameters.Add("@PARTY", FbDbType.VarChar, 30).Value = partyType;
                    insertDebt.Parameters.Add("@SUP", FbDbType.BigInt).Value =
                        draft.SupplierId is > 0 ? draft.SupplierId.Value : DBNull.Value;
                    insertDebt.Parameters.Add("@CUS", FbDbType.BigInt).Value =
                        draft.CustomerId is > 0 ? draft.CustomerId.Value : DBNull.Value;
                    insertDebt.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertDebt.Parameters.Add("@ORIG", FbDbType.Decimal).Value = debt;
                    insertDebt.Parameters.Add("@BY", FbDbType.BigInt).Value =
                        session.UserId > 0 ? session.UserId : DBNull.Value;
                    payableId = Convert.ToInt64(await insertDebt.ExecuteScalarAsync(cancellationToken));
                }

                await AuditLogWriter.WriteAsync(
                    connection, fbTx, "STOCK_RECEIPT_POSTED",
                    session.UserId, session.Username, session.BranchId,
                    "STOCK_DOCUMENT", documentId.ToString(),
                    $"{{\"code\":\"{code}\",\"total\":{total},\"paid\":{draft.PaidAmountVnd},\"debt\":{debt}}}",
                    cancellationToken);

                await transaction.CommitAsync(cancellationToken);
                return new MaterialReceiptPostResult(documentId, code, total, draft.PaidAmountVnd, debt, payableId);
            }
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
