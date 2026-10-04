using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Inventory;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdOutsourcedCraftService : IOutsourcedCraftService
{
    private readonly FirebirdFinancialVoucherPostingService _vouchers = new();

    public async Task<long> CreateAndPostIssueAsync(
        OutsourcedCraftIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = OutsourcedCraftIssueValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "OUTSOURCED_CRAFT_ISSUE", session.BranchId, "XCT", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, EXTERNAL_WORKER_ID, PRODUCTION_ORDER_ID, PURPOSE_CODE,
                     EXPECTED_RETURN_DATE, DELIVERED_BY_NAME, RECEIVED_BY_NAME, CRAFT_INSTRUCTIONS,
                     NOTES, STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'OUTSOURCED_CRAFT_ISSUE', @DATE,
                     @LOC, @WID, @PO, 'OUTSOURCED_CRAFT',
                     @RET, @DEL, @REC, @INSTR,
                     @NOTES, 'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@LOC", FbDbType.BigInt).Value = draft.SourceLocationId;
                insertDoc.Parameters.Add("@WID", FbDbType.BigInt).Value = draft.ExternalWorkerId;
                insertDoc.Parameters.Add("@PO", FbDbType.BigInt).Value =
                    draft.ProductionOrderId is > 0 ? draft.ProductionOrderId.Value : DBNull.Value;
                insertDoc.Parameters.Add("@RET", FbDbType.TimeStamp).Value =
                    draft.ExpectedReturnDate.HasValue ? draft.ExpectedReturnDate.Value : DBNull.Value;
                insertDoc.Parameters.Add("@DEL", FbDbType.VarChar, 150).Value =
                    (object?)draft.DeliveredByName ?? DBNull.Value;
                insertDoc.Parameters.Add("@REC", FbDbType.VarChar, 150).Value =
                    (object?)draft.ReceivedByName ?? DBNull.Value;
                insertDoc.Parameters.Add("@INSTR", FbDbType.VarChar, 1000).Value =
                    (object?)draft.CraftInstructions ?? DBNull.Value;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    (object?)draft.Notes ?? DBNull.Value;
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var lineNumber = 0;
            foreach (var line in draft.Lines)
            {
                lineNumber++;
                var material = await StockPostingSupport.GetMaterialAsync(
                    connection, fbTx, companyId, line.MaterialId, cancellationToken);
                if (!MaterialTypes.IsRawMaterial(material.MaterialType))
                    throw new InvalidOperationException("Chỉ được xuất NVL cho thợ ngoài.");

                decimal issueCost = 0;
                await using (var costCmd = new FbCommand("""
                    SELECT COALESCE(AVERAGE_COST, 0) FROM STOCK_BALANCES
                    WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
                    """, connection, fbTx))
                {
                    costCmd.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    costCmd.Parameters.Add("@LID", FbDbType.BigInt).Value = draft.SourceLocationId;
                    var costObj = await costCmd.ExecuteScalarAsync(cancellationToken);
                    if (costObj is not null and not DBNull)
                        issueCost = Convert.ToDecimal(costObj);
                }

                WarehouseGoldPriceSnapshot? warehouseGoldPrice = null;
                if (string.Equals(material.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
                {
                    warehouseGoldPrice = await FirebirdWarehouseGoldPricingSupport.ResolveAsync(
                        connection, fbTx, companyId,
                        string.IsNullOrWhiteSpace(material.PurityCode) ? material.Code : material.PurityCode,
                        cancellationToken);
                    issueCost = warehouseGoldPrice.IssuePricePerChi;
                }

                long lineId;
                await using (var insertLine = new FbCommand("""
                    INSERT INTO STOCK_DOCUMENT_LINES
                        (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                         STANDARD_UNIT_CODE, CURRENCY_CODE, EXCHANGE_RATE, UNIT_PRICE, AMOUNT_VND,
                         WAREHOUSE_GOLD_PRICE_ID, WAREHOUSE_GOLD_PRICE_PER_CHI, NOTES)
                    VALUES
                        (@DOC, @LN, @MID, @QTY, @COST, @AMT,
                         @UNIT, 'VND', 1, @COST, @AMT, @WGP, @WGPRICE, @NOTES)
                    RETURNING ID
                    """, connection, fbTx))
                {
                    var amount = line.Quantity * issueCost;
                    insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNumber;
                    insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                    insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = issueCost;
                    insertLine.Parameters.Add("@AMT", FbDbType.Decimal).Value = amount;
                    insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = material.UnitCode;
                    insertLine.Parameters.Add("@WGP", FbDbType.BigInt).Value =
                        warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.Id;
                    insertLine.Parameters.Add("@WGPRICE", FbDbType.Decimal).Value =
                        warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.IssuePricePerChi;
                    insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        (object?)line.Notes ?? DBNull.Value;
                    lineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                }

                await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                    connection, fbTx, line.MaterialId, draft.SourceLocationId,
                    -line.Quantity, issueCost, cancellationToken);

                await StockPostingSupport.InsertMovementAsync(
                    connection, fbTx, documentId, lineId, line.MaterialId,
                    draft.SourceLocationId, "OUT", -line.Quantity, issueCost,
                    session.UserId, cancellationToken);

                await using var insertHold = new FbCommand("""
                    INSERT INTO EXTERNAL_WORKER_HOLDINGS
                        (COMPANY_ID, BRANCH_ID, EXTERNAL_WORKER_ID, ISSUE_DOCUMENT_ID, ISSUE_LINE_ID,
                         MATERIAL_ID, QUANTITY_ISSUED, STATUS_CODE, CREATED_AT)
                    VALUES
                        (@CID, @BID, @WID, @DOC, @LINE, @MID, @QTY, 'IN_PROGRESS', CURRENT_TIMESTAMP)
                    """, connection, fbTx);
                insertHold.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertHold.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertHold.Parameters.Add("@WID", FbDbType.BigInt).Value = draft.ExternalWorkerId;
                insertHold.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                insertHold.Parameters.Add("@LINE", FbDbType.BigInt).Value = lineId;
                insertHold.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                insertHold.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                await insertHold.ExecuteNonQueryAsync(cancellationToken);
            }

            // Intentionally no PAYABLE / CASH_VOUCHER for NVL value handed to worker.
            await AuditLogWriter.WriteAsync(
                connection, fbTx, "OUTSOURCED_CRAFT_ISSUE_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), code, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return documentId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<long> CreateAndPostReturnAsync(
        CraftReturnReceiptDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);

            long workerId;
            await using (var issueCmd = new FbCommand("""
                SELECT EXTERNAL_WORKER_ID, DOCUMENT_TYPE, STATUS_CODE
                FROM STOCK_DOCUMENTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                issueCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.SourceIssueDocumentId;
                issueCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                issueCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await issueCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy phiếu xuất thợ.");
                if (reader.GetString(1) != StockDocumentTypes.OutsourcedCraftIssue || reader.GetString(2) != StockDocumentStatuses.Posted)
                    throw new InvalidOperationException("Phiếu nguồn phải là xuất chế tác ngoài đã ghi sổ.");
                workerId = reader.GetInt64(0);
            }

            var outstanding = new Dictionary<long, decimal>();
            await using (var holdCmd = new FbCommand("""
                SELECT ID,
                       QUANTITY_ISSUED - QUANTITY_RETURNED - QUANTITY_IN_PRODUCT - QUANTITY_SCRAP - QUANTITY_LOSS
                FROM EXTERNAL_WORKER_HOLDINGS
                WHERE ISSUE_DOCUMENT_ID = @DOC
                WITH LOCK
                """, connection, fbTx))
            {
                holdCmd.Parameters.Add("@DOC", FbDbType.BigInt).Value = draft.SourceIssueDocumentId;
                await using var reader = await holdCmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    outstanding[reader.GetInt64(0)] = reader.GetDecimal(1);
            }

            var errors = CraftSettlementValidator.Validate(draft, outstanding);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "CRAFT_RETURN_RECEIPT", session.BranchId, "NCT", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     DESTINATION_LOCATION_ID, EXTERNAL_WORKER_ID, SOURCE_ISSUE_DOCUMENT_ID, PURPOSE_CODE,
                     DELIVERED_BY_NAME, RECEIVED_BY_NAME, LABOR_AMOUNT_VND,
                     TOTAL_AMOUNT_VND, PAID_AMOUNT_VND, DEBT_AMOUNT_VND,
                     NOTES, STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, 'CRAFT_RETURN_RECEIPT', @DATE,
                     @LOC, @WID, @SRC, 'CRAFT_SETTLEMENT',
                     @DEL, @REC, @LABOR,
                     @LABOR, @PAID, @DEBT,
                     @NOTES, 'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                var laborDebt = draft.LaborAmountVnd - draft.PaidLaborAmountVnd;
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@LOC", FbDbType.BigInt).Value = draft.DestinationLocationId;
                insertDoc.Parameters.Add("@WID", FbDbType.BigInt).Value = workerId;
                insertDoc.Parameters.Add("@SRC", FbDbType.BigInt).Value = draft.SourceIssueDocumentId;
                insertDoc.Parameters.Add("@DEL", FbDbType.VarChar, 150).Value =
                    (object?)draft.DeliveredByName ?? DBNull.Value;
                insertDoc.Parameters.Add("@REC", FbDbType.VarChar, 150).Value =
                    (object?)draft.ReceivedByName ?? DBNull.Value;
                insertDoc.Parameters.Add("@LABOR", FbDbType.Decimal).Value = draft.LaborAmountVnd;
                insertDoc.Parameters.Add("@PAID", FbDbType.Decimal).Value = draft.PaidLaborAmountVnd;
                insertDoc.Parameters.Add("@DEBT", FbDbType.Decimal).Value = laborDebt;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    (object?)draft.Notes ?? DBNull.Value;
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            foreach (var line in draft.SettlementLines)
            {
                long materialId;
                decimal issued;
                await using (var h = new FbCommand("""
                    SELECT MATERIAL_ID, QUANTITY_ISSUED,
                           QUANTITY_ISSUED - QUANTITY_RETURNED - QUANTITY_IN_PRODUCT - QUANTITY_SCRAP - QUANTITY_LOSS
                    FROM EXTERNAL_WORKER_HOLDINGS WHERE ID = @ID
                    """, connection, fbTx))
                {
                    h.Parameters.Add("@ID", FbDbType.BigInt).Value = line.HoldingId;
                    await using var reader = await h.ExecuteReaderAsync(cancellationToken);
                    if (!await reader.ReadAsync(cancellationToken))
                        throw new InvalidOperationException("Holding không tồn tại.");
                    materialId = reader.GetInt64(0);
                    issued = reader.GetDecimal(2);
                }

                var loss = CraftSettlementValidator.ComputeLoss(
                    issued, line.QuantityInProduct, line.QuantityReturned, line.QuantityScrap);
                var variance = CraftSettlementValidator.ComputeVariance(loss, line.AllowedLoss);
                var responsibility = ResolveVarianceResponsibility(line, variance);

                long? receivableDebtId = null;
                if (responsibility == VarianceResponsibilities.WorkerCompensation)
                {
                    if (line.CompensationAmountVnd <= 0 || line.CompensationQty <= 0)
                        throw new InvalidOperationException(
                            "Bồi thường hao hụt thợ bắt buộc nhập lượng, đơn giá và số tiền VNĐ.");
                    var expectedComp = Math.Round(
                        line.CompensationQty * line.CompensationUnitPriceVnd, 2, MidpointRounding.AwayFromZero);
                    if (line.CompensationAmountVnd != expectedComp)
                        throw new InvalidOperationException(
                            $"Số tiền bồi thường phải = lượng × đơn giá ({expectedComp:N0}).");

                    var recvCode = await StockPostingSupport.NextDocumentCodeAsync(
                        connection, fbTx, "RECEIVABLE_DEBT", session.BranchId, "PTU", cancellationToken);
                    await using var insertRecv = new FbCommand("""
                        INSERT INTO RECEIVABLE_DEBTS
                            (COMPANY_ID, BRANCH_ID, CODE, PARTY_TYPE, EXTERNAL_WORKER_ID,
                             ORIGIN_DOCUMENT_TYPE, ORIGIN_DOCUMENT_ID, DEBT_KIND,
                             ORIGINAL_AMOUNT_VND, COLLECTED_AMOUNT_VND, REMAINING_AMOUNT_VND,
                             STATUS_CODE, NOTES, CREATED_BY, CREATED_AT)
                        VALUES
                            (@CID, @BID, @CODE, 'EXTERNAL_WORKER', @WID,
                             'STOCK_DOCUMENT', @DOC, 'LOSS_COMPENSATION',
                             @ORIG, 0, @ORIG, 'OPEN', @NOTES, @BY, CURRENT_TIMESTAMP)
                        RETURNING ID
                        """, connection, fbTx);
                    insertRecv.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                    insertRecv.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                    insertRecv.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = recvCode;
                    insertRecv.Parameters.Add("@WID", FbDbType.BigInt).Value = workerId;
                    insertRecv.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertRecv.Parameters.Add("@ORIG", FbDbType.Decimal).Value = line.CompensationAmountVnd;
                    insertRecv.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        (object?)line.CompensationReason ?? DBNull.Value;
                    insertRecv.Parameters.Add("@BY", FbDbType.BigInt).Value =
                        session.UserId > 0 ? session.UserId : DBNull.Value;
                    receivableDebtId = Convert.ToInt64(await insertRecv.ExecuteScalarAsync(cancellationToken));
                }

                await using (var settle = new FbCommand("""
                    INSERT INTO STOCK_CRAFT_SETTLEMENT_LINES
                        (RETURN_DOCUMENT_ID, HOLDING_ID, MATERIAL_ID,
                         QUANTITY_ISSUED, QUANTITY_IN_PRODUCT, QUANTITY_RETURNED, QUANTITY_SCRAP,
                         QUANTITY_LOSS, ALLOWED_LOSS, VARIANCE_QTY, NOTES,
                         VARIANCE_RESPONSIBILITY, COMPENSATION_QTY, COMPENSATION_UNIT_PRICE_VND,
                         COMPENSATION_AMOUNT_VND, COMPENSATION_REASON, RECEIVABLE_DEBT_ID)
                    VALUES
                        (@DOC, @HID, @MID, @ISS, @PROD, @RET, @SCRAP, @LOSS, @ALLOW, @VAR, @NOTES,
                         @RESP, @CQTY, @CPRICE, @CAMT, @CREASON, @RID)
                    """, connection, fbTx))
                {
                    settle.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    settle.Parameters.Add("@HID", FbDbType.BigInt).Value = line.HoldingId;
                    settle.Parameters.Add("@MID", FbDbType.BigInt).Value = materialId;
                    settle.Parameters.Add("@ISS", FbDbType.Decimal).Value = issued;
                    settle.Parameters.Add("@PROD", FbDbType.Decimal).Value = line.QuantityInProduct;
                    settle.Parameters.Add("@RET", FbDbType.Decimal).Value = line.QuantityReturned;
                    settle.Parameters.Add("@SCRAP", FbDbType.Decimal).Value = line.QuantityScrap;
                    settle.Parameters.Add("@LOSS", FbDbType.Decimal).Value = loss;
                    settle.Parameters.Add("@ALLOW", FbDbType.Decimal).Value = line.AllowedLoss;
                    settle.Parameters.Add("@VAR", FbDbType.Decimal).Value = variance;
                    settle.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        (object?)line.Notes ?? DBNull.Value;
                    settle.Parameters.Add("@RESP", FbDbType.VarChar, 30).Value =
                        (object?)responsibility ?? DBNull.Value;
                    settle.Parameters.Add("@CQTY", FbDbType.Decimal).Value = line.CompensationQty;
                    settle.Parameters.Add("@CPRICE", FbDbType.Decimal).Value = line.CompensationUnitPriceVnd;
                    settle.Parameters.Add("@CAMT", FbDbType.Decimal).Value = line.CompensationAmountVnd;
                    settle.Parameters.Add("@CREASON", FbDbType.VarChar, 500).Value =
                        (object?)line.CompensationReason ?? DBNull.Value;
                    settle.Parameters.Add("@RID", FbDbType.BigInt).Value =
                        receivableDebtId is > 0 ? receivableDebtId.Value : DBNull.Value;
                    await settle.ExecuteNonQueryAsync(cancellationToken);
                }

                var status = variance != 0 ? HoldingStatuses.Variance
                    : (issued - line.QuantityInProduct - line.QuantityReturned - line.QuantityScrap - loss) > 0
                        ? HoldingStatuses.PartialReturn
                        : HoldingStatuses.Settled;

                await using var updateHold = new FbCommand("""
                    UPDATE EXTERNAL_WORKER_HOLDINGS
                       SET QUANTITY_RETURNED = QUANTITY_RETURNED + @RET,
                           QUANTITY_IN_PRODUCT = QUANTITY_IN_PRODUCT + @PROD,
                           QUANTITY_SCRAP = QUANTITY_SCRAP + @SCRAP,
                           QUANTITY_LOSS = QUANTITY_LOSS + @LOSS,
                           STATUS_CODE = @STATUS,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID
                    """, connection, fbTx);
                updateHold.Parameters.Add("@RET", FbDbType.Decimal).Value = line.QuantityReturned;
                updateHold.Parameters.Add("@PROD", FbDbType.Decimal).Value = line.QuantityInProduct;
                updateHold.Parameters.Add("@SCRAP", FbDbType.Decimal).Value = line.QuantityScrap;
                updateHold.Parameters.Add("@LOSS", FbDbType.Decimal).Value = loss;
                updateHold.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = status;
                updateHold.Parameters.Add("@ID", FbDbType.BigInt).Value = line.HoldingId;
                await updateHold.ExecuteNonQueryAsync(cancellationToken);

                if (line.QuantityReturned > 0)
                {
                    await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                        connection, fbTx, materialId, draft.DestinationLocationId,
                        line.QuantityReturned, 0, cancellationToken);
                }

                if (line.QuantityScrap > 0)
                {
                    await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                        connection, fbTx, materialId, draft.DestinationLocationId,
                        line.QuantityScrap, 0, cancellationToken);
                }
            }

            var outputLineNo = 0;
            foreach (var output in draft.OutputLines ?? Array.Empty<CraftOutputLineDraft>())
            {
                outputLineNo++;
                var productId = await UpsertFinishedProductFromCraftOutputAsync(
                    connection, fbTx, companyId, session.BranchId, output, session.UserId, cancellationToken);

                await using (var insertOut = new FbCommand("""
                    INSERT INTO STOCK_CRAFT_OUTPUT_LINES
                        (RETURN_DOCUMENT_ID, LINE_NUMBER, PRODUCTION_ORDER_ID, SALES_ORDER_ID,
                         FINISHED_PRODUCT_ID, QUANTITY, WEIGHT, GOLD_PURITY, DESTINATION_LOCATION_ID,
                         MATERIAL_VALUE_VND, ALLOCATED_LABOR_COST_VND, TOTAL_COST_VND, NOTES)
                    VALUES
                        (@DOC, @LN, @PO, @SO, @FP, @QTY, @W, @PURITY, @LOC,
                         @MAT, @LABOR, @TOTAL, @NOTES)
                    """, connection, fbTx))
                {
                    insertOut.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertOut.Parameters.Add("@LN", FbDbType.Integer).Value = outputLineNo;
                    insertOut.Parameters.Add("@PO", FbDbType.BigInt).Value =
                        output.ProductionOrderId is > 0 ? output.ProductionOrderId.Value : DBNull.Value;
                    insertOut.Parameters.Add("@SO", FbDbType.BigInt).Value =
                        output.SalesOrderId is > 0 ? output.SalesOrderId.Value : DBNull.Value;
                    insertOut.Parameters.Add("@FP", FbDbType.BigInt).Value = productId;
                    insertOut.Parameters.Add("@QTY", FbDbType.Decimal).Value = output.Quantity;
                    insertOut.Parameters.Add("@W", FbDbType.Decimal).Value = output.Weight;
                    insertOut.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                        (object?)output.GoldPurity ?? DBNull.Value;
                    insertOut.Parameters.Add("@LOC", FbDbType.BigInt).Value = output.DestinationLocationId;
                    insertOut.Parameters.Add("@MAT", FbDbType.Decimal).Value = output.MaterialValueVnd;
                    insertOut.Parameters.Add("@LABOR", FbDbType.Decimal).Value = output.AllocatedLaborCostVnd;
                    insertOut.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = output.TotalCostVnd;
                    insertOut.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        (object?)output.Notes ?? DBNull.Value;
                    await insertOut.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var insertMove = new FbCommand("""
                    INSERT INTO FINISHED_PRODUCT_MOVEMENTS
                        (COMPANY_ID, BRANCH_ID, FINISHED_PRODUCT_ID, MOVEMENT_TYPE,
                         DOCUMENT_TYPE, DOCUMENT_ID, OCCURRED_AT, QUANTITY_CHANGE,
                         UNIT_COST, VALUE_CHANGE, LOCATION_ID, STATUS_BEFORE, STATUS_AFTER,
                         NOTES, CREATED_BY)
                    VALUES
                        (@CID, @BID, @FP, 'IN',
                         'CRAFT_RETURN_RECEIPT', @DOC, CURRENT_TIMESTAMP, @QTY,
                         @COST, @VALUE, @LOC, @BEFORE, 'IN_STOCK',
                         @NOTES, @BY)
                    """, connection, fbTx);
                insertMove.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertMove.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertMove.Parameters.Add("@FP", FbDbType.BigInt).Value = productId;
                insertMove.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                insertMove.Parameters.Add("@QTY", FbDbType.Decimal).Value = output.Quantity;
                insertMove.Parameters.Add("@COST", FbDbType.Decimal).Value = output.TotalCostVnd;
                insertMove.Parameters.Add("@VALUE", FbDbType.Decimal).Value = output.TotalCostVnd;
                insertMove.Parameters.Add("@LOC", FbDbType.BigInt).Value = output.DestinationLocationId;
                insertMove.Parameters.Add("@BEFORE", FbDbType.VarChar, 30).Value =
                    output.FinishedProductId is > 0 ? "WAITING_RECEIPT" : DBNull.Value;
                insertMove.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    (object?)output.Notes ?? DBNull.Value;
                insertMove.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                await insertMove.ExecuteNonQueryAsync(cancellationToken);
            }

            var payLine = 0;
            foreach (var payment in draft.LaborPayments ?? Array.Empty<PaymentAllocationDraft>())
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

            var laborDebtAmount = draft.LaborAmountVnd - draft.PaidLaborAmountVnd;
            if (laborDebtAmount > 0)
            {
                var debtCode = await StockPostingSupport.NextDocumentCodeAsync(
                    connection, fbTx, "PAYABLE_DEBT", session.BranchId, "CN", cancellationToken);
                await using var insertDebt = new FbCommand("""
                    INSERT INTO PAYABLE_DEBTS
                        (COMPANY_ID, BRANCH_ID, CODE, PARTY_TYPE, EXTERNAL_WORKER_ID,
                         ORIGIN_DOCUMENT_TYPE, ORIGIN_DOCUMENT_ID, DEBT_KIND,
                         ORIGINAL_AMOUNT_VND, PAID_AMOUNT_VND, REMAINING_AMOUNT_VND,
                         STATUS_CODE, CREATED_BY, CREATED_AT)
                    VALUES
                        (@CID, @BID, @CODE, 'EXTERNAL_WORKER', @WID,
                         'STOCK_DOCUMENT', @DOC, 'LABOR',
                         @ORIG, 0, @ORIG, 'OPEN', @BY, CURRENT_TIMESTAMP)
                    """, connection, fbTx);
                insertDebt.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDebt.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDebt.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = debtCode;
                insertDebt.Parameters.Add("@WID", FbDbType.BigInt).Value = workerId;
                insertDebt.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                insertDebt.Parameters.Add("@ORIG", FbDbType.Decimal).Value = laborDebtAmount;
                insertDebt.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                await insertDebt.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "CRAFT_RETURN_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), code, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return documentId;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<OutsourcedHoldingRow>> SearchHoldingsAsync(
        long? externalWorkerId,
        string statusCode,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var result = new List<OutsourcedHoldingRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT H.ID, H.ISSUE_DOCUMENT_ID, D.CODE, H.EXTERNAL_WORKER_ID, W.CODE, W.NAME,
                   H.MATERIAL_ID, M.CODE, M.NAME, M.UNIT_CODE,
                   H.QUANTITY_ISSUED, H.QUANTITY_RETURNED, H.QUANTITY_IN_PRODUCT,
                   H.QUANTITY_SCRAP, H.QUANTITY_LOSS, H.STATUS_CODE,
                   COALESCE(D2.LABOR_AMOUNT_VND, 0),
                   COALESCE((SELECT SUM(P.REMAINING_AMOUNT_VND) FROM PAYABLE_DEBTS P
                             WHERE P.EXTERNAL_WORKER_ID = H.EXTERNAL_WORKER_ID
                               AND P.DEBT_KIND = 'LABOR'
                               AND P.STATUS_CODE IN ('OPEN', 'PARTIAL')), 0)
            FROM EXTERNAL_WORKER_HOLDINGS H
            JOIN STOCK_DOCUMENTS D ON D.ID = H.ISSUE_DOCUMENT_ID
            JOIN EXTERNAL_WORKERS W ON W.ID = H.EXTERNAL_WORKER_ID
            JOIN MATERIALS M ON M.ID = H.MATERIAL_ID
            LEFT JOIN STOCK_DOCUMENTS D2 ON D2.SOURCE_ISSUE_DOCUMENT_ID = H.ISSUE_DOCUMENT_ID
                 AND D2.DOCUMENT_TYPE = 'CRAFT_RETURN_RECEIPT' AND D2.STATUS_CODE = 'POSTED'
            WHERE H.BRANCH_ID = @BID
              AND (@HAS_W = 0 OR H.EXTERNAL_WORKER_ID = @WID)
              AND (@HAS_S = 0 OR H.STATUS_CODE = @STATUS)
            ORDER BY W.NAME, M.NAME
            """, connection);
        var status = (statusCode ?? string.Empty).Trim().ToUpperInvariant();
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_W", FbDbType.Integer).Value = externalWorkerId is > 0 ? 1 : 0;
        command.Parameters.Add("@WID", FbDbType.BigInt).Value = externalWorkerId ?? 0;
        command.Parameters.Add("@HAS_S", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = status;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var issued = reader.GetDecimal(10);
            var returned = reader.GetDecimal(11);
            var inProduct = reader.GetDecimal(12);
            var scrap = reader.GetDecimal(13);
            var loss = reader.GetDecimal(14);
            result.Add(new OutsourcedHoldingRow(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetString(9),
                issued, returned, inProduct, scrap, loss,
                issued - returned - inProduct - scrap - loss,
                reader.GetDecimal(16),
                reader.GetDecimal(17),
                reader.GetString(15)));
        }

        return result;
    }

    private static async Task<long> UpsertFinishedProductFromCraftOutputAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        CraftOutputLineDraft output,
        long userId,
        CancellationToken cancellationToken)
    {
        if (output.FinishedProductId is > 0)
        {
            await using var update = new FbCommand("""
                UPDATE FINISHED_PRODUCTS
                   SET LOCATION_ID = @LOC,
                       TOTAL_WEIGHT = @W,
                       GOLD_WEIGHT = @W,
                       GOLD_PURITY = COALESCE(@PURITY, GOLD_PURITY),
                       MATERIAL_COST = @MAT,
                       LABOR_COST = @LABOR,
                       NON_GOLD_MATERIAL_COST = @NON_GOLD,
                       TOTAL_COST = @TOTAL,
                       PRODUCTION_ORDER_ID = COALESCE(@PO, PRODUCTION_ORDER_ID),
                       SALES_ORDER_ID = COALESCE(@SO, SALES_ORDER_ID),
                       STATUS_CODE = 'IN_STOCK',
                       RECEIVED_AT = CURRENT_TIMESTAMP,
                       RECEIVED_BY = @BY
                 WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                RETURNING ID
                """, connection, transaction);
            update.Parameters.Add("@LOC", FbDbType.BigInt).Value = output.DestinationLocationId;
            update.Parameters.Add("@W", FbDbType.Decimal).Value = output.Weight;
            update.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                (object?)output.GoldPurity ?? DBNull.Value;
            update.Parameters.Add("@MAT", FbDbType.Decimal).Value = output.MaterialValueVnd;
            update.Parameters.Add("@LABOR", FbDbType.Decimal).Value = output.AllocatedLaborCostVnd;
            update.Parameters.Add("@NON_GOLD", FbDbType.Decimal).Value =
                Math.Max(0, output.TotalCostVnd - output.MaterialValueVnd - output.AllocatedLaborCostVnd);
            update.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = output.TotalCostVnd;
            update.Parameters.Add("@PO", FbDbType.BigInt).Value =
                output.ProductionOrderId is > 0 ? output.ProductionOrderId.Value : DBNull.Value;
            update.Parameters.Add("@SO", FbDbType.BigInt).Value =
                output.SalesOrderId is > 0 ? output.SalesOrderId.Value : DBNull.Value;
            update.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
            update.Parameters.Add("@ID", FbDbType.BigInt).Value = output.FinishedProductId.Value;
            update.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            update.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            var updated = await update.ExecuteScalarAsync(cancellationToken);
            if (updated is null or DBNull)
                throw new InvalidOperationException($"Không cập nhật được thành phẩm #{output.FinishedProductId}.");
            return Convert.ToInt64(updated);
        }

        var code = string.IsNullOrWhiteSpace(output.ProductCode)
            ? await StockPostingSupport.NextDocumentCodeAsync(
                connection, transaction, "FINISHED_PRODUCT", branchId, "TP", cancellationToken)
            : output.ProductCode.Trim();
        var name = string.IsNullOrWhiteSpace(output.ProductName) ? code : output.ProductName.Trim();

        await using var insert = new FbCommand("""
            INSERT INTO FINISHED_PRODUCTS
                (COMPANY_ID, BRANCH_ID, PRODUCTION_ORDER_ID, SALES_ORDER_ID,
                 CODE, PRODUCT_NAME, GOLD_PURITY, TOTAL_WEIGHT,
                 GOLD_WEIGHT, MATERIAL_COST, LABOR_COST, NON_GOLD_MATERIAL_COST, TOTAL_COST,
                 LOCATION_ID, STATUS_CODE, RECEIVED_BY, RECEIVED_AT, CREATED_BY, CREATED_AT)
            VALUES
                (@CID, @BID, @PO, @SO,
                 @CODE, @NAME, @PURITY, @W,
                 @W, @MAT, @LABOR, @NON_GOLD, @TOTAL,
                 @LOC, 'IN_STOCK', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
            RETURNING ID
            """, connection, transaction);
        insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        insert.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        insert.Parameters.Add("@PO", FbDbType.BigInt).Value =
            output.ProductionOrderId is > 0 ? output.ProductionOrderId.Value : DBNull.Value;
        insert.Parameters.Add("@SO", FbDbType.BigInt).Value =
            output.SalesOrderId is > 0 ? output.SalesOrderId.Value : DBNull.Value;
        insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
        insert.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
        insert.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
            (object?)output.GoldPurity ?? DBNull.Value;
        insert.Parameters.Add("@W", FbDbType.Decimal).Value = output.Weight;
        insert.Parameters.Add("@MAT", FbDbType.Decimal).Value = output.MaterialValueVnd;
        insert.Parameters.Add("@LABOR", FbDbType.Decimal).Value = output.AllocatedLaborCostVnd;
        insert.Parameters.Add("@NON_GOLD", FbDbType.Decimal).Value =
            Math.Max(0, output.TotalCostVnd - output.MaterialValueVnd - output.AllocatedLaborCostVnd);
        insert.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = output.TotalCostVnd;
        insert.Parameters.Add("@LOC", FbDbType.BigInt).Value = output.DestinationLocationId;
        insert.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
    }

    private static string? ResolveVarianceResponsibility(CraftSettlementLineDraft line, decimal variance)
    {
        var explicitResp = (line.VarianceResponsibility ?? string.Empty).Trim().ToUpperInvariant();
        if (explicitResp.Length > 0)
        {
            if (explicitResp is not (
                VarianceResponsibilities.WithinAllowance or
                VarianceResponsibilities.CompanyAccepted or
                VarianceResponsibilities.WorkerCompensation))
                throw new InvalidOperationException("VARIANCE_RESPONSIBILITY không hợp lệ.");
            return explicitResp;
        }

        if (variance <= 0)
            return VarianceResponsibilities.WithinAllowance;
        // Excess variance without explicit responsibility stays recorded but does not auto-create debt.
        return null;
    }
}
