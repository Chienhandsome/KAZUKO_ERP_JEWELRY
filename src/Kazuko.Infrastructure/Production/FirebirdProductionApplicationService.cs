using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Inventory;
using Kazuko.Core.Production;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Production;

public sealed partial class FirebirdProductionApplicationService : IProductionApplicationService
{
    private readonly IProductionAttachmentStore _attachmentStore;

    public FirebirdProductionApplicationService(IProductionAttachmentStore attachmentStore)
    {
        _attachmentStore = attachmentStore;
    }

    private sealed record OrderRequestRow(long OrderId, string Code, long RowVersion, string RequestHash);

    public async Task<ProductionOrderSaveResult> SaveDraftAsync(
        ProductionOrderDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = ProductionOrderDraftValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        using var files=new ProductionAttachmentCommit(_attachmentStore);
        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            await ValidateLocationsAsync(connection, fbTx, session.BranchId, draft, cancellationToken);
            await ValidateSalesOrderAsync(connection, fbTx, session.BranchId, draft, cancellationToken);
            await ValidateWorkersAsync(connection, fbTx, draft, cancellationToken);

            long orderId;
            string code;
            long rowVersion;
            var isCreate = draft.Id is null or <= 0;

            // Idempotency cho tạo mới (kiểu PRODUCTION_RECEIPT_REQUESTS): retry cùng
            // RequestId trả LSX cũ, khác nội dung bị chặn. Sửa bản nháp không dedupe.
            string? orderRequestHash = null;
            if (isCreate && draft.RequestId != Guid.Empty)
            {
                orderRequestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(draft))));
                var priorOrder = await connection.QuerySingleOrDefaultAsync<OrderRequestRow>(new CommandDefinition("""
                    SELECT R.ORDER_ID AS OrderId, O.CODE AS Code, O.ROW_VERSION AS RowVersion, R.REQUEST_HASH AS RequestHash
                    FROM PRODUCTION_ORDER_REQUESTS R JOIN PRODUCTION_ORDERS O ON O.ID = R.ORDER_ID
                    WHERE R.COMPANY_ID = @companyId AND R.BRANCH_ID = @branch AND R.REQUEST_ID = @id
                    """, new { companyId, branch = session.BranchId, id = draft.RequestId.ToString() }, fbTx, cancellationToken: cancellationToken));
                if (priorOrder is not null)
                {
                    if (priorOrder.RequestHash.Trim() != orderRequestHash)
                        throw new InvalidOperationException("Yêu cầu tạo lệnh này đã lưu với nội dung khác. Tải lại lệnh đã tạo.");
                    await transaction.CommitAsync(cancellationToken);
                    return new ProductionOrderSaveResult(priorOrder.OrderId, priorOrder.Code, priorOrder.RowVersion);
                }
            }

            if (isCreate)
            {
                code = await StockPostingSupport.NextDocumentCodeAsync(
                    connection, fbTx, "PRODUCTION_ORDER", session.BranchId, "SX", cancellationToken);

                await using (var insert = new FbCommand("""
                    INSERT INTO PRODUCTION_ORDERS
                        (COMPANY_ID, BRANCH_ID, CODE, ORDER_SOURCE, SALES_ORDER_ID, EXECUTION_TYPE,
                         EXTERNAL_WORKER_ID, STATUS_CODE, ORDER_DATE, DUE_DATE, SOURCE_LOCATION_ID, FINISHED_LOCATION_ID,
                         NOTES, ROW_VERSION, CREATED_BY, CREATED_AT)
                    VALUES
                        (@CID, @BID, @CODE, @SOURCE, @SO, @EXEC, @EXT, 'DRAFT', @ODATE, @DUE, @SRC, @FIN,
                         @NOTES, 0, @BY, CURRENT_TIMESTAMP)
                    RETURNING ID, ROW_VERSION
                    """, connection, fbTx))
                {
                    insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                    insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                    insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                    AddDraftHeaderParameters(insert, session, draft);
                    await using var reader = await insert.ExecuteReaderAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);
                    orderId = reader.GetInt64(0);
                    rowVersion = reader.GetInt64(1);
                }
            }
            else
            {
                orderId = draft.Id!.Value;
                var locked = await LockDraftOrderAsync(
                    connection, fbTx, companyId, session.BranchId, orderId, draft.RowVersion, cancellationToken);
                code = locked.Code;
                rowVersion = locked.RowVersion;

                await using var update = new FbCommand("""
                    UPDATE PRODUCTION_ORDERS
                       SET ORDER_SOURCE = @SOURCE,
                           SALES_ORDER_ID = @SO,
                           EXECUTION_TYPE = @EXEC,
                           EXTERNAL_WORKER_ID = @EXT,
                           ORDER_DATE = @ODATE,
                           DUE_DATE = @DUE,
                           SOURCE_LOCATION_ID = @SRC,
                           FINISHED_LOCATION_ID = @FIN,
                           NOTES = @NOTES,
                           UPDATED_BY = @BY,
                           UPDATED_AT = CURRENT_TIMESTAMP,
                           ROW_VERSION = ROW_VERSION + 1
                     WHERE ID = @ID
                       AND STATUS_CODE = 'DRAFT'
                       AND ROW_VERSION = @VERSION
                    RETURNING ROW_VERSION
                    """, connection, fbTx);
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
                update.Parameters.Add("@VERSION", FbDbType.BigInt).Value = draft.RowVersion ?? rowVersion;
                AddDraftHeaderParameters(update, session, draft);
                var updatedVersion = await update.ExecuteScalarAsync(cancellationToken);
                if (updatedVersion is null or DBNull)
                    throw new InvalidOperationException("Lệnh đã thay đổi hoặc không còn ở trạng thái bản nháp.");
                rowVersion = Convert.ToInt64(updatedVersion);
            }

            await ReplaceLinesAsync(connection, fbTx, orderId, draft.Lines, cancellationToken);
            await ReplaceWorkersAsync(connection, fbTx, orderId, draft.EmployeeIds, cancellationToken);
            await SyncAttachmentsAsync(connection, fbTx, orderId, code, session.UserId, draft.Attachments, cancellationToken,files);
            await MarkCraftOrderInProductionAsync(connection, fbTx, draft.SalesOrderId, cancellationToken);

            if (isCreate && draft.RequestId != Guid.Empty && orderRequestHash is not null)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO PRODUCTION_ORDER_REQUESTS(COMPANY_ID,BRANCH_ID,REQUEST_ID,REQUEST_HASH,ORDER_ID,PAYLOAD)
                    VALUES(@companyId,@branch,@id,@requestHash,@orderId,@payload)
                    """, new { companyId, branch = session.BranchId, id = draft.RequestId.ToString(), requestHash = orderRequestHash, orderId, payload = JsonSerializer.Serialize(draft) }, fbTx, cancellationToken: cancellationToken));
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PRODUCTION_ORDER_DRAFT_SAVED",
                session.UserId, session.Username, session.BranchId,
                "PRODUCTION_ORDER", orderId.ToString(), code, cancellationToken);

            files.BeginCommit();
            await transaction.CommitAsync(cancellationToken);
            files.Commit();
            return new ProductionOrderSaveResult(orderId, code, rowVersion);
        }
        catch (FbException ex) when (IsSalesOrderUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                "Đơn bán đã được gắn với một lệnh sản xuất khác. Không thể tạo lệnh thứ hai cho cùng đơn bán.");
        }
        catch (FbException ex) when (IsLegacyNotNullViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException(
                "Không lưu được lệnh: cột legacy trên PRODUCTION_ORDERS vẫn bắt buộc NOT NULL. " +
                "Đóng app và chạy migration 50 (`0050__production_legacy_columns_nullable.sql`).");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProductionMaterialIssueResult> PostMaterialIssueAsync(
        ProductionMaterialIssueDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = ProductionMaterialIssueValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var order = await LockOrderHeaderAsync(
                connection, fbTx, companyId, session.BranchId, draft.ProductionOrderId,
                draft.ExpectedRowVersion, cancellationToken);

            if (!string.Equals(order.StatusCode, ProductionOrderStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                if (order.MaterialIssueDocumentId is > 0)
                    throw new InvalidOperationException("Lệnh đã ghi sổ giao nguyên liệu. Không ghi lần hai.");
                throw new InvalidOperationException("Chỉ ghi sổ giao nguyên liệu khi lệnh còn bản nháp.");
            }

            await EnsureOrderHasLinesAndWorkersAsync(
                connection, fbTx, draft.ProductionOrderId, order, cancellationToken);

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, ProductionStockDocumentTypes.MaterialIssue,
                session.BranchId, "GX", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     SOURCE_LOCATION_ID, PRODUCTION_ORDER_ID, PURPOSE_CODE,
                     DELIVERED_BY_NAME, RECEIVED_BY_NAME, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, @DTYPE, @DATE,
                     @SRC, @PO, @PURPOSE,
                     @DEL, @REC, @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DTYPE", FbDbType.VarChar, 40).Value =
                    ProductionStockDocumentTypes.MaterialIssue;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@SRC", FbDbType.BigInt).Value = order.SourceLocationId;
                insertDoc.Parameters.Add("@PO", FbDbType.BigInt).Value = draft.ProductionOrderId;
                insertDoc.Parameters.Add("@PURPOSE", FbDbType.VarChar, 40).Value =
                    ProductionStockPurposes.MaterialDelivery;
                insertDoc.Parameters.Add("@DEL", FbDbType.VarChar, 150).Value =
                    string.IsNullOrWhiteSpace(draft.DeliveredByName) ? DBNull.Value : draft.DeliveredByName.Trim();
                insertDoc.Parameters.Add("@REC", FbDbType.VarChar, 150).Value =
                    string.IsNullOrWhiteSpace(draft.ReceivedByName) ? DBNull.Value : draft.ReceivedByName.Trim();
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var lineNo = 0;
            foreach (var line in draft.Lines)
            {
                lineNo++;
                var material = await StockPostingSupport.GetMaterialAsync(
                    connection, fbTx, companyId, line.MaterialId, cancellationToken);
                if (!MaterialTypes.IsRawMaterial(material.MaterialType))
                    throw new InvalidOperationException($"Dòng {lineNo}: '{material.Code}' không phải NVL.");

                decimal unitCost;
                await using (var costCmd = new FbCommand("""
                    SELECT COALESCE(AVERAGE_COST, 0)
                    FROM STOCK_BALANCES
                    WHERE MATERIAL_ID = @MID AND LOCATION_ID = @LID
                    WITH LOCK
                    """, connection, fbTx))
                {
                    costCmd.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    costCmd.Parameters.Add("@LID", FbDbType.BigInt).Value = order.SourceLocationId;
                    var value = await costCmd.ExecuteScalarAsync(cancellationToken);
                    unitCost = value is null or DBNull ? 0 : Convert.ToDecimal(value);
                }

                WarehouseGoldPriceSnapshot? warehouseGoldPrice = null;
                if (string.Equals(material.MaterialType, MaterialTypes.Gold, StringComparison.OrdinalIgnoreCase))
                {
                    warehouseGoldPrice = await FirebirdWarehouseGoldPricingSupport.ResolveAsync(
                        connection, fbTx, companyId, material.GoldTypeId,
                        string.IsNullOrWhiteSpace(material.PurityCode) ? material.Code : material.PurityCode,
                        cancellationToken);
                    unitCost = warehouseGoldPrice.IssuePricePerChi;
                }

                await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                    connection, fbTx, line.MaterialId, order.SourceLocationId,
                    -line.Quantity, unitCost, cancellationToken);

                long lineId;
                await using (var insertLine = new FbCommand("""
                    INSERT INTO STOCK_DOCUMENT_LINES
                        (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                         STANDARD_UNIT_CODE, WAREHOUSE_GOLD_PRICE_ID,
                         WAREHOUSE_GOLD_PRICE_PER_CHI, NOTES)
                    VALUES
                        (@DOC, @LN, @MID, @QTY, @COST, @AMOUNT, @UNIT, @WGP, @WGPRICE, @NOTES)
                    RETURNING ID
                    """, connection, fbTx))
                {
                    insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                    insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNo;
                    insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                    insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                    insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                    insertLine.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = line.Quantity * unitCost;
                    insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = material.UnitCode;
                    insertLine.Parameters.Add("@WGP", FbDbType.BigInt).Value =
                        warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.Id;
                    insertLine.Parameters.Add("@WGPRICE", FbDbType.Decimal).Value =
                        warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.IssuePricePerChi;
                    insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                        string.IsNullOrWhiteSpace(line.Notes) ? DBNull.Value : line.Notes.Trim();
                    lineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                }

                await StockPostingSupport.InsertMovementAsync(
                    connection, fbTx, documentId, lineId, line.MaterialId,
                    order.SourceLocationId, "OUT", -line.Quantity, unitCost,
                    session.UserId, cancellationToken);

                await using var insertBalance = new FbCommand("""
                    INSERT INTO PRODUCTION_MATERIAL_BALANCES
                        (PRODUCTION_ORDER_ID, ISSUE_DOCUMENT_ID, ISSUE_LINE_ID, MATERIAL_ID,
                         QUANTITY_ISSUED, UNIT_CODE, UNIT_COST_SNAPSHOT,
                         WAREHOUSE_GOLD_PRICE_ID, WAREHOUSE_GOLD_PRICE_PER_CHI)
                    VALUES
                        (@PO, @DOC, @LINE, @MID, @QTY, @UNIT, @COST, @WGP, @WGPRICE)
                    """, connection, fbTx);
                insertBalance.Parameters.Add("@PO", FbDbType.BigInt).Value = draft.ProductionOrderId;
                insertBalance.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                insertBalance.Parameters.Add("@LINE", FbDbType.BigInt).Value = lineId;
                insertBalance.Parameters.Add("@MID", FbDbType.BigInt).Value = line.MaterialId;
                insertBalance.Parameters.Add("@QTY", FbDbType.Decimal).Value = line.Quantity;
                insertBalance.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = material.UnitCode;
                insertBalance.Parameters.Add("@COST", FbDbType.Decimal).Value = unitCost;
                insertBalance.Parameters.Add("@WGP", FbDbType.BigInt).Value =
                    warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.Id;
                insertBalance.Parameters.Add("@WGPRICE", FbDbType.Decimal).Value =
                    warehouseGoldPrice is null ? DBNull.Value : warehouseGoldPrice.IssuePricePerChi;
                await insertBalance.ExecuteNonQueryAsync(cancellationToken);
            }

            long rowVersion;
            await using (var updateOrder = new FbCommand("""
                UPDATE PRODUCTION_ORDERS
                   SET STATUS_CODE = 'IN_PROGRESS',
                       MATERIAL_ISSUE_DOCUMENT_ID = @DOC,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID
                   AND STATUS_CODE = 'DRAFT'
                   AND MATERIAL_ISSUE_DOCUMENT_ID IS NULL
                   AND ROW_VERSION = @VERSION
                RETURNING ROW_VERSION
                """, connection, fbTx))
            {
                updateOrder.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                updateOrder.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateOrder.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.ProductionOrderId;
                updateOrder.Parameters.Add("@VERSION", FbDbType.BigInt).Value = draft.ExpectedRowVersion;
                var updated = await updateOrder.ExecuteScalarAsync(cancellationToken);
                if (updated is null or DBNull)
                    throw new InvalidOperationException("Không thể ghi sổ giao NVL. Lệnh đã thay đổi hoặc đã giao.");
                rowVersion = Convert.ToInt64(updated);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PRODUCTION_MATERIAL_ISSUE_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), code, cancellationToken);

            // Tạo QC units (1 unit / 1 sản phẩm theo SL kế hoạch) ngay khi giao NVL.
            // Trước đây việc này nằm ở PostWorkerReturnAsync, nhưng bước "thợ trả hàng"
            // đã bỏ khỏi luồng nên chuyển về đây để nhập kho TP có dữ liệu.
            await using (var linesCmd = new FbCommand("""
                SELECT ID, PLANNED_QUANTITY, LABOR_COST_PER_UNIT
                FROM PRODUCTION_ORDER_LINES
                WHERE PRODUCTION_ORDER_ID = @PO
                ORDER BY LINE_NUMBER
                """, connection, fbTx))
            {
                linesCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = draft.ProductionOrderId;
                await using var reader = await linesCmd.ExecuteReaderAsync(cancellationToken);
                var lineRows = new List<(long Id, int Qty, decimal Labor)>();
                while (await reader.ReadAsync(cancellationToken))
                    lineRows.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetDecimal(2)));

                foreach (var (lineId, plannedQty, labor) in lineRows)
                {
                    for (var unitNo = 1; unitNo <= plannedQty; unitNo++)
                    {
                        await using var insertUnit = new FbCommand("""
                            INSERT INTO PRODUCTION_QC_UNITS
                                (PRODUCTION_ORDER_ID, ORDER_LINE_ID, UNIT_NUMBER, STATUS_CODE, LABOR_COST_SNAPSHOT)
                            VALUES
                                (@PO, @LINE, @UNIT, 'PENDING', @LABOR)
                            """, connection, fbTx);
                        insertUnit.Parameters.Add("@PO", FbDbType.BigInt).Value = draft.ProductionOrderId;
                        insertUnit.Parameters.Add("@LINE", FbDbType.BigInt).Value = lineId;
                        insertUnit.Parameters.Add("@UNIT", FbDbType.Integer).Value = unitNo;
                        insertUnit.Parameters.Add("@LABOR", FbDbType.Decimal).Value = labor;
                        await insertUnit.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }

            await transaction.CommitAsync(cancellationToken);
            return new ProductionMaterialIssueResult(
                documentId, code, draft.ProductionOrderId, rowVersion);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Lệnh đã có phiếu giao nguyên liệu. Không ghi lần hai.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProductionWorkerReturnResult> PostWorkerReturnAsync(
        ProductionWorkerReturnDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (draft.ProductionOrderId <= 0)
            throw new InvalidOperationException("Thiếu lệnh sản xuất.");
        if (draft.BalanceLines is null || draft.BalanceLines.Count == 0)
            throw new InvalidOperationException("Phải nhập đối soát cho toàn bộ nguyên liệu đã giao.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var order = await LockOrderHeaderAsync(
                connection, fbTx, companyId, session.BranchId, draft.ProductionOrderId,
                draft.ExpectedRowVersion, cancellationToken);

            if (!string.Equals(order.StatusCode, ProductionOrderStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Chỉ ghi nhận nhận hàng khi lệnh đang làm.");
            if (order.MaterialIssueDocumentId is null or <= 0)
                throw new InvalidOperationException("Chưa giao nguyên liệu — không thể nhận hàng.");
            if (order.ReturnDocumentId is > 0)
                throw new InvalidOperationException("Lệnh đã có phiếu nhận hàng chính. Không ghi lần hai.");

            var balances = await LoadMaterialBalancesLockedAsync(
                connection, fbTx, draft.ProductionOrderId, cancellationToken);
            var issuedById = balances.ToDictionary(x => x.Id, x => x.QuantityIssued);
            var balanceErrors = ProductionMaterialBalanceValidator.ValidateReturnLines(draft.BalanceLines, issuedById);
            if (balanceErrors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", balanceErrors));

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, ProductionStockDocumentTypes.ReturnReceipt,
                session.BranchId, "NT", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     DESTINATION_LOCATION_ID, PRODUCTION_ORDER_ID, PURPOSE_CODE,
                     SOURCE_ISSUE_DOCUMENT_ID, DELIVERED_BY_NAME, RECEIVED_BY_NAME, NOTES,
                     STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, @DTYPE, @DATE,
                     @DEST, @PO, @PURPOSE,
                     @ISSUE, @DEL, @REC, @NOTES,
                     'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insertDoc.Parameters.Add("@DTYPE", FbDbType.VarChar, 40).Value =
                    ProductionStockDocumentTypes.ReturnReceipt;
                insertDoc.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertDoc.Parameters.Add("@DEST", FbDbType.BigInt).Value = order.SourceLocationId;
                insertDoc.Parameters.Add("@PO", FbDbType.BigInt).Value = draft.ProductionOrderId;
                insertDoc.Parameters.Add("@PURPOSE", FbDbType.VarChar, 40).Value =
                    ProductionStockPurposes.Return;
                insertDoc.Parameters.Add("@ISSUE", FbDbType.BigInt).Value = order.MaterialIssueDocumentId.Value;
                insertDoc.Parameters.Add("@DEL", FbDbType.VarChar, 150).Value =
                    string.IsNullOrWhiteSpace(draft.DeliveredByName) ? DBNull.Value : draft.DeliveredByName.Trim();
                insertDoc.Parameters.Add("@REC", FbDbType.VarChar, 150).Value =
                    string.IsNullOrWhiteSpace(draft.ReceivedByName) ? DBNull.Value : draft.ReceivedByName.Trim();
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var balanceById = balances.ToDictionary(x => x.Id);
            var lineNo = 0;
            foreach (var line in draft.BalanceLines)
            {
                var balance = balanceById[line.MaterialBalanceId];
                await using (var updateBal = new FbCommand("""
                    UPDATE PRODUCTION_MATERIAL_BALANCES
                       SET QUANTITY_IN_PRODUCT = @INP,
                           QUANTITY_RETURNED = @RET,
                           QUANTITY_SCRAP = @SCRAP,
                           QUANTITY_LOSS = @LOSS,
                           UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID
                       AND PRODUCTION_ORDER_ID = @PO
                    """, connection, fbTx))
                {
                    updateBal.Parameters.Add("@INP", FbDbType.Decimal).Value = line.QuantityInProduct;
                    updateBal.Parameters.Add("@RET", FbDbType.Decimal).Value = line.QuantityReturned;
                    updateBal.Parameters.Add("@SCRAP", FbDbType.Decimal).Value = line.QuantityScrap;
                    updateBal.Parameters.Add("@LOSS", FbDbType.Decimal).Value = line.QuantityLoss;
                    updateBal.Parameters.Add("@ID", FbDbType.BigInt).Value = line.MaterialBalanceId;
                    updateBal.Parameters.Add("@PO", FbDbType.BigInt).Value = draft.ProductionOrderId;
                    await updateBal.ExecuteNonQueryAsync(cancellationToken);
                }

                var stockInQty = line.QuantityReturned + line.QuantityScrap;
                if (stockInQty > 0)
                {
                    lineNo++;
                    await StockPostingSupport.ApplyMaterialBalanceChangeAsync(
                        connection, fbTx, balance.MaterialId, order.SourceLocationId,
                        stockInQty, balance.UnitCostSnapshot, cancellationToken);

                    long stockLineId;
                    await using (var insertLine = new FbCommand("""
                        INSERT INTO STOCK_DOCUMENT_LINES
                            (DOCUMENT_ID, LINE_NUMBER, MATERIAL_ID, QUANTITY, UNIT_COST, LINE_AMOUNT,
                             STANDARD_UNIT_CODE, WAREHOUSE_GOLD_PRICE_ID, WAREHOUSE_GOLD_PRICE_PER_CHI, NOTES)
                        VALUES
                            (@DOC, @LN, @MID, @QTY, @COST, @AMOUNT, @UNIT, @WGP, @WGPRICE, @NOTES)
                        RETURNING ID
                        """, connection, fbTx))
                    {
                        insertLine.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                        insertLine.Parameters.Add("@LN", FbDbType.Integer).Value = lineNo;
                        insertLine.Parameters.Add("@MID", FbDbType.BigInt).Value = balance.MaterialId;
                        insertLine.Parameters.Add("@QTY", FbDbType.Decimal).Value = stockInQty;
                        insertLine.Parameters.Add("@COST", FbDbType.Decimal).Value = balance.UnitCostSnapshot;
                        insertLine.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value =
                            stockInQty * balance.UnitCostSnapshot;
                        insertLine.Parameters.Add("@UNIT", FbDbType.VarChar, 20).Value = balance.UnitCode;
                        insertLine.Parameters.Add("@WGP", FbDbType.BigInt).Value =
                            balance.WarehouseGoldPriceId is > 0 ? balance.WarehouseGoldPriceId.Value : DBNull.Value;
                        insertLine.Parameters.Add("@WGPRICE", FbDbType.Decimal).Value =
                            balance.WarehouseGoldPricePerChi is > 0
                                ? balance.WarehouseGoldPricePerChi.Value
                                : DBNull.Value;
                        insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                            $"Trả lại={line.QuantityReturned}; Phế liệu={line.QuantityScrap}";
                        stockLineId = Convert.ToInt64(await insertLine.ExecuteScalarAsync(cancellationToken));
                    }

                    await StockPostingSupport.InsertMovementAsync(
                        connection, fbTx, documentId, stockLineId, balance.MaterialId,
                        order.SourceLocationId, "IN", stockInQty, balance.UnitCostSnapshot,
                        session.UserId, cancellationToken);
                }
            }

            // QC units giờ được tạo ở PostMaterialIssueAsync (lúc giao NVL).
            // Giữ method này cho tương thích nhưng không tạo units nữa để tránh trùng.

            long rowVersion;
            await using (var updateOrder = new FbCommand("""
                UPDATE PRODUCTION_ORDERS
                   SET RETURN_DOCUMENT_ID = @DOC,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID
                   AND STATUS_CODE = 'IN_PROGRESS'
                   AND RETURN_DOCUMENT_ID IS NULL
                   AND ROW_VERSION = @VERSION
                RETURNING ROW_VERSION
                """, connection, fbTx))
            {
                updateOrder.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                updateOrder.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateOrder.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.ProductionOrderId;
                updateOrder.Parameters.Add("@VERSION", FbDbType.BigInt).Value = draft.ExpectedRowVersion;
                var updated = await updateOrder.ExecuteScalarAsync(cancellationToken);
                if (updated is null or DBNull)
                    throw new InvalidOperationException("Không thể ghi nhận nhận hàng. Lệnh đã thay đổi.");
                rowVersion = Convert.ToInt64(updated);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PRODUCTION_RETURN_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), code, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new ProductionWorkerReturnResult(documentId, code, 0, rowVersion);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Lệnh đã có phiếu nhận hàng chính. Không ghi lần hai.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RecordQcAsync(
        ProductionQcDraft draft,
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
            var unit = await LockQcUnitAsync(connection, fbTx, draft.QcUnitId, draft.ExpectedRowVersion, cancellationToken);
            await EnsureOrderInProgressForQcAsync(
                connection, fbTx, companyId, session.BranchId, unit.ProductionOrderId, cancellationToken);

            if (unit.FinishedProductId is > 0)
                throw new InvalidOperationException("Món đã gắn thành phẩm — không thể điều chỉnh QC.");

            var isAdjustment =
                string.Equals(unit.StatusCode, ProductionQcUnitStatuses.Passed, StringComparison.OrdinalIgnoreCase)
                && draft.IsAdjustment;

            var remainingByBalance = await LoadRemainingInProductAsync(
                connection,
                fbTx,
                unit.ProductionOrderId,
                excludeQcUnitId: isAdjustment ? draft.QcUnitId : null,
                cancellationToken);
            var errors = ProductionQcValidator.Validate(
                draft, unit.StatusCode, id => remainingByBalance.TryGetValue(id, out var rem) ? rem : null);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            var result = draft.ResultCode.Trim().ToUpperInvariant();
            var nextAttempt = unit.AttemptCount + 1;
            long attemptId;
            await using (var insertAttempt = new FbCommand("""
                INSERT INTO PRODUCTION_QC_ATTEMPTS
                    (QC_UNIT_ID, ATTEMPT_NUMBER, RESULT_CODE,
                     ACTUAL_GOLD_WEIGHT, ACTUAL_WEIGHT_UNIT_CODE, GOLD_PURITY_CODE,
                     DEFECT_NOTES, INSPECTED_BY, INSPECTED_AT,
                     RETURNED_FOR_REWORK_AT, RETURNED_FOR_REWORK_BY)
                VALUES
                    (@UNIT, @NO, @RESULT,
                     @W, @WUNIT, @PURITY,
                     @DEFECT, @BY, CURRENT_TIMESTAMP,
                     @REWORK_AT, @REWORK_BY)
                RETURNING ID
                """, connection, fbTx))
            {
                insertAttempt.Parameters.Add("@UNIT", FbDbType.BigInt).Value = draft.QcUnitId;
                insertAttempt.Parameters.Add("@NO", FbDbType.Integer).Value = nextAttempt;
                insertAttempt.Parameters.Add("@RESULT", FbDbType.VarChar, 10).Value = result;
                insertAttempt.Parameters.Add("@W", FbDbType.Decimal).Value =
                    draft.ActualGoldWeight is > 0 ? draft.ActualGoldWeight.Value : DBNull.Value;
                insertAttempt.Parameters.Add("@WUNIT", FbDbType.VarChar, 20).Value =
                    string.IsNullOrWhiteSpace(draft.ActualWeightUnitCode)
                        ? DBNull.Value
                        : draft.ActualWeightUnitCode.Trim();
                insertAttempt.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                    string.IsNullOrWhiteSpace(draft.GoldPurityCode) ? DBNull.Value : draft.GoldPurityCode.Trim();
                insertAttempt.Parameters.Add("@DEFECT", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.DefectNotes) ? DBNull.Value : draft.DefectNotes.Trim();
                insertAttempt.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                if (result == ProductionQcResultCodes.Fail)
                {
                    insertAttempt.Parameters.Add("@REWORK_AT", FbDbType.TimeStamp).Value = DateTime.Now;
                    insertAttempt.Parameters.Add("@REWORK_BY", FbDbType.BigInt).Value =
                        session.UserId > 0 ? session.UserId : DBNull.Value;
                }
                else
                {
                    insertAttempt.Parameters.Add("@REWORK_AT", FbDbType.TimeStamp).Value = DBNull.Value;
                    insertAttempt.Parameters.Add("@REWORK_BY", FbDbType.BigInt).Value = DBNull.Value;
                }

                attemptId = Convert.ToInt64(await insertAttempt.ExecuteScalarAsync(cancellationToken));
            }

            if (isAdjustment)
            {
                await using var deleteComp = new FbCommand("""
                    DELETE FROM PRODUCTION_UNIT_COMPONENTS
                    WHERE QC_UNIT_ID = @UNIT
                    """, connection, fbTx);
                deleteComp.Parameters.Add("@UNIT", FbDbType.BigInt).Value = draft.QcUnitId;
                await deleteComp.ExecuteNonQueryAsync(cancellationToken);
            }

            if (result == ProductionQcResultCodes.Pass)
            {
                var balances = (await LoadMaterialBalancesLockedAsync(
                    connection, fbTx, unit.ProductionOrderId, cancellationToken))
                    .ToDictionary(x => x.Id);

                foreach (var component in draft.Components)
                {
                    if (!balances.TryGetValue(component.MaterialBalanceId, out var balance))
                        throw new InvalidOperationException("Cấu thành tham chiếu balance không thuộc lệnh.");

                    var kind = component.ComponentKind.Trim().ToUpperInvariant();
                    var amount = ProductionCostCalculator.RoundMoney(
                        component.QuantityUsed * (
                            kind == ProductionComponentKinds.Gold
                                ? (balance.WarehouseGoldPricePerChi ?? balance.UnitCostSnapshot)
                                : balance.UnitCostSnapshot));

                    await using var insertComp = new FbCommand("""
                        INSERT INTO PRODUCTION_UNIT_COMPONENTS
                            (QC_UNIT_ID, MATERIAL_BALANCE_ID, MATERIAL_ID, COMPONENT_KIND,
                             QUANTITY_USED, UNIT_CODE, UNIT_COST_SNAPSHOT, AMOUNT_VND,
                             WAREHOUSE_GOLD_PRICE_ID, GOLD_PRICE_PER_CHI_SNAPSHOT)
                        VALUES
                            (@QCU, @BAL, @MID, @KIND, @QTY, @UCODE, @COST, @AMT, @WGP, @WGPRICE)
                        """, connection, fbTx);
                    insertComp.Parameters.Add("@QCU", FbDbType.BigInt).Value = draft.QcUnitId;
                    insertComp.Parameters.Add("@BAL", FbDbType.BigInt).Value = component.MaterialBalanceId;
                    insertComp.Parameters.Add("@MID", FbDbType.BigInt).Value = balance.MaterialId;
                    insertComp.Parameters.Add("@KIND", FbDbType.VarChar, 20).Value = kind;
                    insertComp.Parameters.Add("@QTY", FbDbType.Decimal).Value = component.QuantityUsed;
                    insertComp.Parameters.Add("@UCODE", FbDbType.VarChar, 20).Value = balance.UnitCode;
                    insertComp.Parameters.Add("@COST", FbDbType.Decimal).Value = balance.UnitCostSnapshot;
                    insertComp.Parameters.Add("@AMT", FbDbType.Decimal).Value = amount;
                    insertComp.Parameters.Add("@WGP", FbDbType.BigInt).Value =
                        balance.WarehouseGoldPriceId is > 0 ? balance.WarehouseGoldPriceId.Value : DBNull.Value;
                    insertComp.Parameters.Add("@WGPRICE", FbDbType.Decimal).Value =
                        balance.WarehouseGoldPricePerChi is > 0
                            ? balance.WarehouseGoldPricePerChi.Value
                            : DBNull.Value;
                    await insertComp.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var image in draft.Images)
                {
                    var stored = await _attachmentStore.FinalizeAsync(
                        image.StagingPath, unit.OrderCode, cancellationToken);
                    await using var insertImg = new FbCommand("""
                        INSERT INTO PRODUCTION_QC_IMAGES
                            (QC_UNIT_ID, QC_ATTEMPT_ID, STORED_PATH, ORIGINAL_FILE_NAME,
                             MIME_TYPE, CAPTION, DISPLAY_ORDER, CREATED_BY)
                        VALUES
                            (@UNIT, @ATT, @PATH, @ORIG, @MIME, @CAP, @ORD, @BY)
                        """, connection, fbTx);
                    insertImg.Parameters.Add("@UNIT", FbDbType.BigInt).Value = draft.QcUnitId;
                    insertImg.Parameters.Add("@ATT", FbDbType.BigInt).Value = attemptId;
                    insertImg.Parameters.Add("@PATH", FbDbType.VarChar, 500).Value = stored;
                    insertImg.Parameters.Add("@ORIG", FbDbType.VarChar, 255).Value = image.OriginalFileName.Trim();
                    insertImg.Parameters.Add("@MIME", FbDbType.VarChar, 100).Value =
                        string.IsNullOrWhiteSpace(image.MimeType) ? DBNull.Value : image.MimeType.Trim();
                    insertImg.Parameters.Add("@CAP", FbDbType.VarChar, 500).Value =
                        string.IsNullOrWhiteSpace(image.Caption) ? DBNull.Value : image.Caption.Trim();
                    insertImg.Parameters.Add("@ORD", FbDbType.Integer).Value = image.DisplayOrder;
                    insertImg.Parameters.Add("@BY", FbDbType.BigInt).Value =
                        session.UserId > 0 ? session.UserId : DBNull.Value;
                    await insertImg.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            var newStatus = result == ProductionQcResultCodes.Pass
                ? ProductionQcUnitStatuses.Passed
                : ProductionQcUnitStatuses.ReworkRequired;

            await using (var updateUnit = new FbCommand("""
                UPDATE PRODUCTION_QC_UNITS
                   SET STATUS_CODE = @STATUS,
                       ACTUAL_GOLD_WEIGHT = @W,
                       ACTUAL_WEIGHT_UNIT_CODE = @WUNIT,
                       GOLD_PURITY_CODE = @PURITY,
                       DEFECT_NOTES = @DEFECT,
                       LAST_QC_AT = CURRENT_TIMESTAMP,
                       LAST_QC_BY = @BY,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID
                   AND ROW_VERSION = @VERSION
                """, connection, fbTx))
            {
                updateUnit.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = newStatus;
                updateUnit.Parameters.Add("@W", FbDbType.Decimal).Value =
                    draft.ActualGoldWeight is > 0 ? draft.ActualGoldWeight.Value : DBNull.Value;
                updateUnit.Parameters.Add("@WUNIT", FbDbType.VarChar, 20).Value =
                    string.IsNullOrWhiteSpace(draft.ActualWeightUnitCode)
                        ? DBNull.Value
                        : draft.ActualWeightUnitCode.Trim();
                updateUnit.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                    string.IsNullOrWhiteSpace(draft.GoldPurityCode) ? DBNull.Value : draft.GoldPurityCode.Trim();
                updateUnit.Parameters.Add("@DEFECT", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.DefectNotes) ? DBNull.Value : draft.DefectNotes.Trim();
                updateUnit.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateUnit.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.QcUnitId;
                updateUnit.Parameters.Add("@VERSION", FbDbType.BigInt).Value = draft.ExpectedRowVersion;
                if (await updateUnit.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Đơn vị QC đã thay đổi. Vui lòng tải lại.");
            }

            var auditAction = isAdjustment
                ? (result == ProductionQcResultCodes.Pass
                    ? "PRODUCTION_QC_ADJUSTED_PASS"
                    : "PRODUCTION_QC_ADJUSTED_FAIL")
                : (result == ProductionQcResultCodes.Pass
                    ? "PRODUCTION_QC_PASSED"
                    : "PRODUCTION_QC_FAILED");
            await AuditLogWriter.WriteAsync(
                connection, fbTx,
                auditAction,
                session.UserId, session.Username, session.BranchId,
                "PRODUCTION_QC_UNIT", draft.QcUnitId.ToString(), unit.OrderCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task RecordReworkReturnAsync(
        ProductionReworkReturnDraft draft,
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
            var unit = await LockQcUnitAsync(connection, fbTx, draft.QcUnitId, draft.ExpectedRowVersion, cancellationToken);
            if (!string.Equals(unit.StatusCode, ProductionQcUnitStatuses.ReworkRequired, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Chỉ ghi nhận làm lại khi món đang ở trạng thái cần làm lại.");

            await EnsureOrderInProgressForQcAsync(
                connection, fbTx, companyId, session.BranchId, unit.ProductionOrderId, cancellationToken);

            await using (var updateAttempt = new FbCommand("""
                UPDATE PRODUCTION_QC_ATTEMPTS
                   SET REWORK_RECEIVED_AT = CURRENT_TIMESTAMP,
                       REWORK_RECEIVED_BY = @BY,
                       REWORK_NOTES = @NOTES
                 WHERE ID = (
                     SELECT FIRST 1 ID FROM PRODUCTION_QC_ATTEMPTS
                     WHERE QC_UNIT_ID = @UNIT AND RESULT_CODE = 'FAIL'
                     ORDER BY ATTEMPT_NUMBER DESC)
                """, connection, fbTx))
            {
                updateAttempt.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateAttempt.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.ReworkNotes) ? DBNull.Value : draft.ReworkNotes.Trim();
                updateAttempt.Parameters.Add("@UNIT", FbDbType.BigInt).Value = draft.QcUnitId;
                await updateAttempt.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var updateUnit = new FbCommand("""
                UPDATE PRODUCTION_QC_UNITS
                   SET STATUS_CODE = 'PENDING',
                       DEFECT_NOTES = NULL,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID
                   AND STATUS_CODE = 'REWORK_REQUIRED'
                   AND ROW_VERSION = @VERSION
                """, connection, fbTx))
            {
                updateUnit.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.QcUnitId;
                updateUnit.Parameters.Add("@VERSION", FbDbType.BigInt).Value = draft.ExpectedRowVersion;
                if (await updateUnit.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không thể ghi nhận làm lại. Vui lòng tải lại.");
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PRODUCTION_REWORK_RETURNED",
                session.UserId, session.Username, session.BranchId,
                "PRODUCTION_QC_UNIT", draft.QcUnitId.ToString(), unit.OrderCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ProductionCompleteResult> CompleteAsync(
        long productionOrderId,
        long expectedRowVersion,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (productionOrderId <= 0)
            throw new InvalidOperationException("Thiếu lệnh sản xuất.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;
        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var order = await LockOrderHeaderAsync(
                connection, fbTx, companyId, session.BranchId, productionOrderId,
                expectedRowVersion, cancellationToken, requireVersionMatch: false);
            if (order.StatusCode == ProductionOrderStatuses.Completed)
            {
                if (order.FinishedReceiptDocumentId is > 0)
                {
                    var prior = await LoadCompletedResultAsync(
                        connection, fbTx, productionOrderId, order, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return prior;
                }
                var priorUnits = await LoadQcUnitsForCompleteAsync(
                    connection, fbTx, productionOrderId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new ProductionCompleteResult(productionOrderId, order.Code,
                    null, null, priorUnits.Count, null,
                    priorUnits.Sum(unit => unit.LaborCostSnapshot), order.RowVersion);
            }
            if (order.RowVersion != expectedRowVersion)
                throw new InvalidOperationException("Lệnh đã được cập nhật bởi người khác. Vui lòng tải lại.");

            var errors = ProductionCompletionValidator.Validate(
                order.StatusCode,
                order.MaterialIssueDocumentId is > 0);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));
            await EnsureAllocationWithinInProductAsync(connection, fbTx, productionOrderId, cancellationToken);

            var units = await LoadQcUnitsForCompleteAsync(connection, fbTx, productionOrderId, cancellationToken);
            var labor = await LoadLaborTotalAsync(connection, fbTx, productionOrderId, cancellationToken);
            long? payableDebtId = null;
            if (order.ExecutionType == ProductionExecutionTypes.Outsourced && labor > 0)
            {
                if (order.ExternalWorkerId is null or <= 0)
                    throw new InvalidOperationException("Lệnh thợ ngoài thiếu mã thợ khi hoàn tất.");
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
                         'PRODUCTION_ORDER', @PO, 'LABOR',
                         @ORIG, 0, @ORIG, 'OPEN', @BY, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """, connection, fbTx);
                insertDebt.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDebt.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDebt.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = debtCode;
                insertDebt.Parameters.Add("@WID", FbDbType.BigInt).Value = order.ExternalWorkerId.Value;
                insertDebt.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
                insertDebt.Parameters.Add("@ORIG", FbDbType.Decimal).Value = labor;
                insertDebt.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId > 0 ? session.UserId : DBNull.Value;
                payableDebtId = Convert.ToInt64(await insertDebt.ExecuteScalarAsync(cancellationToken));
            }

            await using var update = new FbCommand("""
                UPDATE PRODUCTION_ORDERS
                   SET STATUS_CODE = 'COMPLETED', COMPLETED_BY = @BY,
                       COMPLETED_AT = CURRENT_TIMESTAMP, UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP, ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID AND STATUS_CODE = 'IN_PROGRESS'
                   AND FINISHED_RECEIPT_DOCUMENT_ID IS NULL AND ROW_VERSION = @VERSION
                RETURNING ROW_VERSION
                """, connection, fbTx);
            update.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId > 0 ? session.UserId : DBNull.Value;
            update.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
            update.Parameters.Add("@VERSION", FbDbType.BigInt).Value = expectedRowVersion;
            var updated = await update.ExecuteScalarAsync(cancellationToken);
            if (updated is null or DBNull)
                throw new InvalidOperationException("Không thể hoàn tất lệnh. Dữ liệu đã thay đổi.");
            var rowVersion = Convert.ToInt64(updated);

            await AuditLogWriter.WriteAsync(connection, fbTx, "PRODUCTION_ORDER_COMPLETED",
                session.UserId, session.Username, session.BranchId,
                "PRODUCTION_ORDER", productionOrderId.ToString(), order.Code, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProductionCompleteResult(productionOrderId, order.Code, null, null,
                units.Count, payableDebtId, labor, rowVersion);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Lệnh đã hoàn thành hoặc đã có công nợ tiền công.", ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ManualFinishedReceiptResult> PostManualFinishedReceiptAsync(
        PostManualFinishedReceiptRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (request.ReceiptDate == default)
            throw new InvalidOperationException("Thiếu ngày nhập kho.");
        if (request.LocationId <= 0)
            throw new InvalidOperationException("Thiếu kho nhập thành phẩm.");
        if (request.Items is null || request.Items.Count == 0)
            throw new InvalidOperationException("Chưa có sản phẩm nào để nhập kho.");
        if(request.RequestId==Guid.Empty)throw new InvalidOperationException("Phiếu nhập cần mã yêu cầu để thử lại an toàn.");
        // Luồng sản xuất bắt buộc gắn LSX (khớp UI đã bắt chọn LSX). Nhập ngoài/
        // nhập đầu kỳ không LSX là nghiệp vụ khác, cần nguồn nhập riêng — không
        // cho đi qua đường này để tránh nhập kho không đối chiếu vàng, không ghi nợ.
        if (request.ProductionOrderId is not > 0)
            throw new InvalidOperationException(
                "Nhập kho thành phẩm trong luồng sản xuất bắt buộc chọn lệnh sản xuất (LSX).");

        foreach (var item in request.Items)
            FinishedReceiptValidation.ValidateItem(item);

        var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Items)
        {
            var providedCode = (item.ProductCode ?? string.Empty).Trim();
            if (providedCode.Length > 0 && !seenCodes.Add(providedCode))
                throw new InvalidOperationException($"Mã thành phẩm '{providedCode}' bị trùng trong phiếu.");
        }

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            await EnsureFinishedLocationAsync(
                connection, fbTx, request.LocationId, session.BranchId, cancellationToken);

            var requestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request))));
            var prior=await connection.QuerySingleOrDefaultAsync<ReceiptRequestRow>(new CommandDefinition("""
                SELECT D.ID AS Id,D.CODE AS Code,R.PAYLOAD AS Payload,R.REQUEST_HASH AS RequestHash
                FROM PRODUCTION_RECEIPT_REQUESTS R JOIN STOCK_DOCUMENTS D ON D.ID=R.DOCUMENT_ID
                WHERE R.COMPANY_ID=@companyId AND R.BRANCH_ID=@branch AND R.REQUEST_ID=@id
                """,new{companyId,branch=session.BranchId,id=request.RequestId.ToString()},fbTx,cancellationToken:cancellationToken));
            if(prior is not null)
            {
                if(prior.RequestHash.Trim()!=requestHash)throw new InvalidOperationException("Yêu cầu này đã lưu với nội dung khác. Tải lại phiếu đã ghi sổ.");
                var posted=JsonSerializer.Deserialize<PostManualFinishedReceiptRequest>(prior.Payload)!;
                await transaction.CommitAsync(cancellationToken);
                return new(prior.Id,prior.Code,posted.Items.Count,posted);
            }
            var goldCodes=(await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT CODE FROM GOLD_TYPES WHERE COMPANY_ID=@companyId AND IS_ACTIVE=TRUE",new{companyId},fbTx,cancellationToken:cancellationToken))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if(request.Items.Any(i=>!goldCodes.Contains(i.GoldPurityCode??"")))throw new InvalidOperationException("Loại vàng không còn hoạt động hoặc không thuộc danh mục.");
            string? orderCode = null;
            long? salesOrderId = null;
            long? productionOrderRowVersion = null;
            if (request.ProductionOrderId is > 0)
            {
                var orderLink = await LoadProductionOrderLinkAsync(
                    connection, fbTx, companyId, session.BranchId,
                    request.ProductionOrderId.Value, cancellationToken);
                var planned=(await connection.QueryAsync<ReceiptPlannedLine>(new CommandDefinition(
                    "SELECT ID AS Id,PLANNED_QUANTITY AS Quantity,GOLD_TYPE_CODE AS GoldCode FROM PRODUCTION_ORDER_LINES WHERE PRODUCTION_ORDER_ID=@id",
                    new{id=request.ProductionOrderId.Value},fbTx,cancellationToken:cancellationToken))).ToArray();
                FinishedReceiptValidation.ValidateQuantities(planned.ToDictionary(x=>x.Id,x=>x.Quantity),request.Items);
                if(request.Items.Any(i=>!string.Equals(i.GoldPurityCode,planned.Single(p=>p.Id==i.OrderLineId).GoldCode,StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Loại vàng từng món phải khớp dòng mẫu LSX.");
                orderCode = orderLink.Code;
                salesOrderId = orderLink.SalesOrderId;
                productionOrderRowVersion = orderLink.RowVersion;
                if (orderLink.FinishedReceiptDocumentId is > 0)
                    throw new InvalidOperationException(
                        $"Lệnh {orderCode} đã có phiếu nhập kho thành phẩm — không nhập lại.");
                if (!string.Equals(orderLink.StatusCode, ProductionOrderStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Chỉ nhập kho thành phẩm cho lệnh đã hoàn thành. Lệnh {orderCode} đang {orderLink.StatusCode}.");
            }

            var docCode = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, ProductionStockDocumentTypes.FinishedReceipt,
                session.BranchId, "NTP", cancellationToken);

            long documentId;
            await using (var insertDoc = new FbCommand("""
                INSERT INTO STOCK_DOCUMENTS
                    (COMPANY_ID, BRANCH_ID, CODE, DOCUMENT_TYPE, DOCUMENT_DATE,
                     DESTINATION_LOCATION_ID, PRODUCTION_ORDER_ID, PURPOSE_CODE,
                     NOTES, STATUS_CODE, CREATED_BY, CREATED_AT, POSTED_BY, POSTED_AT)
                VALUES
                    (@CID, @BID, @CODE, @DTYPE, @DDATE,
                     @DEST, @PO, @PURPOSE,
                     @NOTES, 'POSTED', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertDoc.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insertDoc.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDoc.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = docCode;
                insertDoc.Parameters.Add("@DTYPE", FbDbType.VarChar, 40).Value =
                    ProductionStockDocumentTypes.FinishedReceipt;
                insertDoc.Parameters.Add("@DDATE", FbDbType.TimeStamp).Value = request.ReceiptDate;
                insertDoc.Parameters.Add("@DEST", FbDbType.BigInt).Value = request.LocationId;
                insertDoc.Parameters.Add("@PO", FbDbType.BigInt).Value =
                    request.ProductionOrderId is > 0 ? request.ProductionOrderId.Value : DBNull.Value;
                insertDoc.Parameters.Add("@PURPOSE", FbDbType.VarChar, 40).Value =
                    ProductionStockPurposes.FinishedReceipt;
                insertDoc.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(request.Notes)
                        ? (orderCode is null
                            ? "Nhập kho thành phẩm (nhập tay)"
                            : $"Nhập kho thành phẩm (nhập tay) lệnh {orderCode}")
                        : request.Notes.Trim();
                insertDoc.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                documentId = Convert.ToInt64(await insertDoc.ExecuteScalarAsync(cancellationToken));
            }

            var committedItems=new List<ManualFinishedProductItem>();
            var lineNo = 0;
            foreach (var item in request.Items)
            {
                lineNo++;

                var code = string.Empty; // Official serial allocated only on commit.
                if (code.Length == 0)
                    code = await StockPostingSupport.NextDocumentCodeAsync(
                        connection, fbTx, "FINISHED_PRODUCT_MANUAL", session.BranchId,
                        ResolveManualProductCodePrefix(item.ProductGroup), cancellationToken);
                var barcode = string.Empty;
                if (barcode.Length == 0)
                    barcode = code;

                committedItems.Add(item with{ProductCode=code,BarcodeValue=barcode});
                var costPrice = item.CostPrice ?? 0;
                var laborCost = item.PurchaseLaborCost ?? item.CraftLaborCost ?? 0;
                var materialCost = Math.Max(0, costPrice - laborCost);
                var groupId = await ResolveFinishedProductGroupIdAsync(
                    connection, fbTx, companyId, item.ProductGroup, cancellationToken);

                long productId;
                await using (var insertFp = new FbCommand("""
                    INSERT INTO FINISHED_PRODUCTS
                        (COMPANY_ID, BRANCH_ID, PRODUCTION_ORDER_ID, SALES_ORDER_ID, PRODUCTION_QC_UNIT_ID,
                         CODE, BARCODE_VALUE, QR_VALUE, PRODUCT_NAME, GOLD_PURITY, TOTAL_WEIGHT, GOLD_WEIGHT,
                         MATERIAL_COST, LABOR_COST, NON_GOLD_MATERIAL_COST, TOTAL_COST,
                         LOCATION_ID, STATUS_CODE, RECEIVED_BY, RECEIVED_AT, CREATED_BY, CREATED_AT,
                         FINISHED_PRODUCT_GROUP_ID, SYMBOL_CODE)
                    VALUES
                        (@CID, @BID, @PO, @SO, NULL,
                         @CODE, @BARCODE, @QR, @NAME, @PURITY, @TW, @GW,
                         @MAT, @LABOR, 0, @TOTAL,
                         @LOC, 'IN_STOCK', @BY, CURRENT_TIMESTAMP, @BY, CURRENT_TIMESTAMP,
                         @GROUP, @SYMBOL)
                    RETURNING ID
                    """, connection, fbTx))
                {
                    insertFp.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                    insertFp.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                    insertFp.Parameters.Add("@PO", FbDbType.BigInt).Value =
                        request.ProductionOrderId is > 0 ? request.ProductionOrderId.Value : DBNull.Value;
                    insertFp.Parameters.Add("@SO", FbDbType.BigInt).Value =
                        salesOrderId is > 0 ? salesOrderId.Value : DBNull.Value;
                    insertFp.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                    insertFp.Parameters.Add("@BARCODE", FbDbType.VarChar, 80).Value = barcode;
                    insertFp.Parameters.Add("@QR", FbDbType.VarChar, 80).Value = barcode;
                    insertFp.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = item.ProductName.Trim();
                    insertFp.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                        string.IsNullOrWhiteSpace(item.GoldPurityCode) ? DBNull.Value : item.GoldPurityCode.Trim();
                    insertFp.Parameters.Add("@TW", FbDbType.Decimal).Value = item.TotalWeight;
                    insertFp.Parameters.Add("@GW", FbDbType.Decimal).Value = item.GoldWeight;
                    insertFp.Parameters.Add("@MAT", FbDbType.Decimal).Value = materialCost;
                    insertFp.Parameters.Add("@LABOR", FbDbType.Decimal).Value = laborCost;
                    insertFp.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = costPrice;
                    insertFp.Parameters.Add("@LOC", FbDbType.BigInt).Value = request.LocationId;
                    insertFp.Parameters.Add("@BY", FbDbType.BigInt).Value =
                        session.UserId > 0 ? session.UserId : DBNull.Value;
                    insertFp.Parameters.Add("@GROUP", FbDbType.BigInt).Value =
                        groupId is > 0 ? groupId.Value : DBNull.Value;
                    insertFp.Parameters.Add("@SYMBOL", FbDbType.VarChar, 40).Value =
                        string.IsNullOrWhiteSpace(item.SymbolCode) ? DBNull.Value : item.SymbolCode.Trim();
                    productId = Convert.ToInt64(await insertFp.ExecuteScalarAsync(cancellationToken));
                }

                await FirebirdFinishedProductTransferService.InsertDocumentLineAsync(
                    connection, fbTx, documentId, lineNo, productId,
                    null, request.LocationId, "NEW", "IN_STOCK", costPrice, cancellationToken,
                    null, null, 0, item.GoldWeight, laborCost, 0);

                await connection.ExecuteAsync(new CommandDefinition(
                    "UPDATE FINISHED_PRODUCTS SET GOLD_TYPE_ID=(SELECT ID FROM GOLD_TYPES WHERE COMPANY_ID=@companyId AND CODE=@code) WHERE ID=@productId",
                    new{companyId,code=item.GoldPurityCode,productId},fbTx,cancellationToken:cancellationToken));

                await FirebirdFinishedProductTransferService.InsertMovementAsync(
                    connection, fbTx, companyId, session.BranchId, productId,
                    "IN", ProductionStockDocumentTypes.FinishedReceipt, documentId, 1, costPrice,
                    request.LocationId, null, request.LocationId,
                    "NEW", "IN_STOCK", session.UserId, cancellationToken);
            }

            if (request.ProductionOrderId is > 0 && productionOrderRowVersion is not null)
            {
                await using var updateOrder = new FbCommand("""
                    UPDATE PRODUCTION_ORDERS
                       SET FINISHED_RECEIPT_DOCUMENT_ID = @DOC,
                           UPDATED_BY = @BY,
                           UPDATED_AT = CURRENT_TIMESTAMP,
                           ROW_VERSION = ROW_VERSION + 1
                     WHERE ID = @ID
                       AND STATUS_CODE = 'COMPLETED'
                       AND FINISHED_RECEIPT_DOCUMENT_ID IS NULL
                       AND ROW_VERSION = @VERSION
                    RETURNING ROW_VERSION
                    """, connection, fbTx);
                updateOrder.Parameters.Add("@DOC", FbDbType.BigInt).Value = documentId;
                updateOrder.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateOrder.Parameters.Add("@ID", FbDbType.BigInt).Value = request.ProductionOrderId.Value;
                updateOrder.Parameters.Add("@VERSION", FbDbType.BigInt).Value = productionOrderRowVersion.Value;
                var updated = await updateOrder.ExecuteScalarAsync(cancellationToken);
                if (updated is null or DBNull)
                    throw new InvalidOperationException(
                        $"Không gắn phiếu nhập cho lệnh {orderCode}. Dữ liệu lệnh đã thay đổi.");
            }

            if(request.ProductionOrderId is >0)
                await PostReceiptGoldSettlementsAsync(connection,fbTx,request,session,orderCode!,cancellationToken);
            var postedRequest=request with{Items=committedItems};
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO PRODUCTION_RECEIPT_REQUESTS(COMPANY_ID,BRANCH_ID,REQUEST_ID,REQUEST_HASH,DOCUMENT_ID,PAYLOAD)
                VALUES(@companyId,@branch,@id,@requestHash,@documentId,@payload)
                """,new{companyId,branch=session.BranchId,id=request.RequestId.ToString(),requestHash,documentId,payload=JsonSerializer.Serialize(postedRequest)},fbTx,cancellationToken:cancellationToken));
            await MarkCraftOrderReadyAsync(connection, fbTx, salesOrderId, cancellationToken);

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "MANUAL_FINISHED_RECEIPT_POSTED",
                session.UserId, session.Username, session.BranchId,
                "STOCK_DOCUMENT", documentId.ToString(), docCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new ManualFinishedReceiptResult(documentId, docCode, request.Items.Count,postedRequest);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("Mã thành phẩm bị trùng. Vui lòng kiểm tra lại danh sách.", ex);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task<long> RecordWorkerGoldDebtAsync(RecordWorkerGoldDebtRequest request, AuthenticatedSession session, CancellationToken cancellationToken=default)
        => throw new InvalidOperationException("Đối chiếu vàng được lưu cùng phiếu nhập thành phẩm. Mở phiếu nhập kho để tiếp tục.");

    private static async Task<long> InsertReceiptGoldDebtAsync(FbConnection connection,FbTransaction fbTx,
        RecordWorkerGoldDebtRequest request,AuthenticatedSession session,CancellationToken cancellationToken)
    {
        var workerType=request.WorkerType;
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var orderCode = await LoadProductionOrderCodeAsync(
                connection, fbTx, companyId, session.BranchId,
                request.ProductionOrderId, cancellationToken);

            if (workerType == WorkerGoldDebtWorkerTypes.ExternalWorker)
            {
                await using (var workerCmd = new FbCommand(
                    "SELECT COUNT(*) FROM EXTERNAL_WORKERS WHERE COMPANY_ID = @CID AND ID = @WID AND IS_ACTIVE = TRUE",
                    connection, fbTx))
                {
                    workerCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                    workerCmd.Parameters.Add("@WID", FbDbType.BigInt).Value = request.WorkerId;
                    if (Convert.ToInt32(await workerCmd.ExecuteScalarAsync(cancellationToken)) < 1)
                        throw new InvalidOperationException("Thợ ngoài không tồn tại hoặc đã ngưng hoạt động.");
                }
            }
            else
            {
                await using (var workerCmd = new FbCommand(
                    "SELECT COUNT(*) FROM PRODUCTION_ORDER_WORKERS WHERE PRODUCTION_ORDER_ID = @PO AND EMPLOYEE_ID = @EID",
                    connection, fbTx))
                {
                    workerCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = request.ProductionOrderId;
                    workerCmd.Parameters.Add("@EID", FbDbType.BigInt).Value = request.WorkerId;
                    if (Convert.ToInt32(await workerCmd.ExecuteScalarAsync(cancellationToken)) < 1)
                        throw new InvalidOperationException("Thợ không thuộc lệnh sản xuất này.");
                }
            }

            var purityCode = string.IsNullOrWhiteSpace(request.GoldPurityCode)
                ? null
                : request.GoldPurityCode.Trim();
            await using (var dupCmd = new FbCommand(
                """
                SELECT COUNT(*) FROM WORKER_GOLD_DEBTS
                 WHERE COMPANY_ID = @CID
                   AND PRODUCTION_ORDER_ID = @PO
                   AND ((GOLD_PURITY_CODE = @PURITY) OR (GOLD_PURITY_CODE IS NULL AND @PURITY IS NULL))
                """,
                connection, fbTx))
            {
                dupCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                dupCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = request.ProductionOrderId;
                dupCmd.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                    (object?)purityCode ?? DBNull.Value;
                if (Convert.ToInt32(await dupCmd.ExecuteScalarAsync(cancellationToken)) > 0)
                    throw new InvalidOperationException("Lệnh này đã được ghi công nợ vàng cho loại vàng này.");
            }

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "WORKER_GOLD_DEBT", session.BranchId, "NV", cancellationToken);

            long debtId;
            await using (var insert = new FbCommand("""
                INSERT INTO WORKER_GOLD_DEBTS
                    (COMPANY_ID, BRANCH_ID, CODE, WORKER_TYPE, WORKER_ID, WORKER_NAME,
                     GOLD_PURITY_CODE, WEIGHT_CHI, WEIGHT_ISSUED_CHI, WEIGHT_RECEIVED_CHI, WEIGHT_LOSS_CHI,
                     PRODUCTION_ORDER_ID, PRODUCTION_ORDER_CODE, STATUS_CODE, NOTES,
                     CREATED_BY, CREATED_AT)
                VALUES
                    (@CID, @BID, @CODE, @WT, @WID, @WNAME,
                     @PURITY, @W, @ISSUED, @RECEIVED, @LOSS,
                     @PO, @POCODE, 'OPEN', @NOTES,
                     @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insert.Parameters.Add("@WT", FbDbType.VarChar, 30).Value = workerType;
                insert.Parameters.Add("@WID", FbDbType.BigInt).Value = request.WorkerId;
                insert.Parameters.Add("@WNAME", FbDbType.VarChar, 200).Value = request.WorkerName.Trim();
                insert.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value =
                    (object?)purityCode ?? DBNull.Value;
                insert.Parameters.Add("@W", FbDbType.Decimal).Value = request.WeightChi;
                insert.Parameters.Add("@ISSUED", FbDbType.Decimal).Value = request.WeightIssuedChi;
                insert.Parameters.Add("@RECEIVED", FbDbType.Decimal).Value = request.WeightReceivedChi;
                insert.Parameters.Add("@LOSS", FbDbType.Decimal).Value = request.WeightLossChi;
                insert.Parameters.Add("@PO", FbDbType.BigInt).Value = request.ProductionOrderId;
                insert.Parameters.Add("@POCODE", FbDbType.VarChar, 40).Value = orderCode;
                insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    string.IsNullOrWhiteSpace(request.Notes) ? DBNull.Value : request.Notes.Trim();
                insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                debtId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "WORKER_GOLD_DEBT_RECORDED",
                session.UserId, session.Username, session.BranchId,
                "WORKER_GOLD_DEBT", debtId.ToString(), code, cancellationToken);


        return debtId;
    }

    public async Task<ProductionCancelResult> CancelAsync(
        long productionOrderId,
        long expectedRowVersion,
        string? reason,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (productionOrderId <= 0)
            throw new InvalidOperationException("Thiếu mã lệnh sản xuất.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            string code;
            string status;
            long rowVersion;

            await using (var select = new FbCommand("""
                SELECT CODE, STATUS_CODE, ROW_VERSION
                FROM PRODUCTION_ORDERS
                WHERE ID = @ID
                  AND COMPANY_ID = @CID
                  AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                select.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
                select.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                select.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await select.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy lệnh sản xuất.");
                code = reader.GetString(0);
                status = reader.GetString(1);
                rowVersion = reader.GetInt64(2);
                if (rowVersion != expectedRowVersion)
                    throw new InvalidOperationException("Lệnh đã được cập nhật bởi người khác. Vui lòng tải lại.");
            }

            if (string.Equals(status, ProductionOrderStatuses.Completed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Không hủy lệnh đã hoàn thành bằng thao tác thường.");
            if (string.Equals(status, ProductionOrderStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Lệnh đã bị hủy trước đó.");

            if (string.Equals(status, ProductionOrderStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
            {
                await using (var fpCmd = new FbCommand("""
                    SELECT COUNT(*) FROM FINISHED_PRODUCTS
                    WHERE PRODUCTION_ORDER_ID = @PO
                    """, connection, fbTx))
                {
                    fpCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
                    if (Convert.ToInt32(await fpCmd.ExecuteScalarAsync(cancellationToken)) > 0)
                        throw new InvalidOperationException(
                            "Không hủy lệnh đang làm vì đã có thành phẩm nhập kho.");
                }

                await using (var debtCmd = new FbCommand("""
                    SELECT COUNT(*) FROM PAYABLE_DEBTS
                    WHERE ORIGIN_DOCUMENT_TYPE = 'PRODUCTION_ORDER'
                      AND ORIGIN_DOCUMENT_ID = @PO
                    """, connection, fbTx))
                {
                    debtCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
                    if (Convert.ToInt32(await debtCmd.ExecuteScalarAsync(cancellationToken)) > 0)
                        throw new InvalidOperationException(
                            "Không hủy lệnh đang làm vì đã phát sinh công nợ tiền công.");
                }

                await using (var balCmd = new FbCommand("""
                    SELECT MATERIAL_ID, QUANTITY_ISSUED, QUANTITY_IN_PRODUCT,
                           QUANTITY_RETURNED, QUANTITY_SCRAP, QUANTITY_LOSS
                    FROM PRODUCTION_MATERIAL_BALANCES
                    WHERE PRODUCTION_ORDER_ID = @PO
                    """, connection, fbTx))
                {
                    balCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
                    await using var reader = await balCmd.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var issued = reader.GetDecimal(1);
                        var inProduct = reader.GetDecimal(2);
                        var returned = reader.GetDecimal(3);
                        var scrap = reader.GetDecimal(4);
                        var loss = reader.GetDecimal(5);
                        if (inProduct != 0
                            || !ProductionMaterialBalanceValidator.IsBalanced(
                                issued, inProduct, returned, scrap, loss)
                            || issued != returned + scrap + loss)
                        {
                            throw new InvalidOperationException(
                                "Không hủy lệnh đang làm vì NVL chưa đối soát về 0 (còn trong sản phẩm hoặc chưa cân bằng).");
                        }
                    }
                }
            }
            else if (!string.Equals(status, ProductionOrderStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Trạng thái lệnh không cho phép hủy.");
            }

            await using (var update = new FbCommand("""
                UPDATE PRODUCTION_ORDERS
                   SET STATUS_CODE = 'CANCELLED',
                       CANCELLED_BY = @BY,
                       CANCELLED_AT = CURRENT_TIMESTAMP,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP,
                       ROW_VERSION = ROW_VERSION + 1
                 WHERE ID = @ID
                   AND STATUS_CODE = @STATUS
                   AND ROW_VERSION = @VERSION
                RETURNING ROW_VERSION
                """, connection, fbTx))
            {
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
                update.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = status;
                update.Parameters.Add("@VERSION", FbDbType.BigInt).Value = expectedRowVersion;
                update.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                var result = await update.ExecuteScalarAsync(cancellationToken);
                if (result is null or DBNull)
                    throw new InvalidOperationException("Không thể hủy lệnh. Vui lòng tải lại dữ liệu.");
                rowVersion = Convert.ToInt64(result);
            }

            var reasonText = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PRODUCTION_ORDER_CANCELLED",
                session.UserId, session.Username, session.BranchId,
                "PRODUCTION_ORDER", productionOrderId.ToString(),
                reasonText is null ? code : $"{code}|{reasonText}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new ProductionCancelResult(productionOrderId, ProductionOrderStatuses.Cancelled, rowVersion);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void AddDraftHeaderParameters(
        FbCommand command,
        AuthenticatedSession session,
        ProductionOrderDraft draft)
    {
        command.Parameters.Add("@SOURCE", FbDbType.VarChar, 20).Value = draft.OrderSource.Trim().ToUpperInvariant();
        command.Parameters.Add("@SO", FbDbType.BigInt).Value =
            draft.SalesOrderId is > 0 ? draft.SalesOrderId.Value : DBNull.Value;
        command.Parameters.Add("@EXEC", FbDbType.VarChar, 20).Value = draft.ExecutionType.Trim().ToUpperInvariant();
        command.Parameters.Add("@EXT", FbDbType.BigInt).Value =
            draft.ExternalWorkerId is > 0 ? draft.ExternalWorkerId.Value : DBNull.Value;
        command.Parameters.Add("@ODATE", FbDbType.TimeStamp).Value = draft.OrderDate;
        command.Parameters.Add("@DUE", FbDbType.TimeStamp).Value =
            draft.DueDate is DateTime due ? due : DBNull.Value;
        command.Parameters.Add("@SRC", FbDbType.BigInt).Value = draft.SourceLocationId;
        command.Parameters.Add("@FIN", FbDbType.BigInt).Value = draft.FinishedLocationId;
        command.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
            string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
        command.Parameters.Add("@BY", FbDbType.BigInt).Value =
            session.UserId > 0 ? session.UserId : DBNull.Value;
    }

    private static async Task<(string Code, long RowVersion)> LockDraftOrderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long orderId,
        long? expectedRowVersion,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE, STATUS_CODE, ROW_VERSION
            FROM PRODUCTION_ORDERS
            WHERE ID = @ID
              AND COMPANY_ID = @CID
              AND BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy lệnh sản xuất.");
        var code = reader.GetString(0);
        var status = reader.GetString(1);
        var rowVersion = reader.GetInt64(2);
        if (!string.Equals(status, ProductionOrderStatuses.Draft, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chỉ sửa lệnh khi còn ở trạng thái bản nháp.");
        if (expectedRowVersion is > 0 && expectedRowVersion.Value != rowVersion)
            throw new InvalidOperationException("Lệnh đã được cập nhật bởi người khác. Vui lòng tải lại.");
        return (code, rowVersion);
    }

    private static async Task ValidateLocationsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long branchId,
        ProductionOrderDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM LOCATIONS
            WHERE ID IN (@SRC, @FIN)
              AND BRANCH_ID = @BID
              AND IS_ACTIVE = TRUE
            """, connection, transaction);
        command.Parameters.Add("@SRC", FbDbType.BigInt).Value = draft.SourceLocationId;
        command.Parameters.Add("@FIN", FbDbType.BigInt).Value = draft.FinishedLocationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 2)
            throw new InvalidOperationException("Kho xuất hoặc kho thành phẩm không hợp lệ.");
    }

    private static async Task ValidateSalesOrderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long branchId,
        ProductionOrderDraft draft,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(draft.OrderSource, ProductionOrderSources.SalesOrder, StringComparison.OrdinalIgnoreCase))
            return;
        if (draft.SalesOrderId is null or <= 0)
            return;

        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM SALES_ORDERS
            WHERE ID = @ID
              AND BRANCH_ID = @BID
              AND ORDER_TYPE = 'CUSTOM'
              AND STATUS_CODE IN ('DEPOSITED', 'CONFIRMED', 'IN_PRODUCTION')
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.SalesOrderId.Value;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Đơn đặt không hợp lệ, chưa thu cọc hoặc không phải đơn chế tác.");
    }

    private static async Task MarkCraftOrderInProductionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long? salesOrderId,
        CancellationToken cancellationToken)
    {
        if (salesOrderId is null or <= 0)
            return;

        await using var command = new FbCommand("""
            UPDATE SALES_ORDERS
               SET STATUS_CODE = 'IN_PRODUCTION',
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID
               AND ORDER_TYPE = 'CUSTOM'
               AND STATUS_CODE IN ('DEPOSITED', 'CONFIRMED', 'IN_PRODUCTION')
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkCraftOrderReadyAsync(
        FbConnection connection,
        FbTransaction transaction,
        long? salesOrderId,
        CancellationToken cancellationToken)
    {
        if (salesOrderId is null or <= 0)
            return;

        await using var command = new FbCommand("""
            UPDATE SALES_ORDERS
               SET STATUS_CODE = 'READY',
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID
               AND ORDER_TYPE = 'CUSTOM'
               AND STATUS_CODE = 'IN_PRODUCTION'
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ValidateWorkersAsync(
        FbConnection connection,
        FbTransaction transaction,
        ProductionOrderDraft draft,
        CancellationToken cancellationToken)
    {
        if (string.Equals(draft.ExecutionType, ProductionExecutionTypes.InHouse, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var employeeId in draft.EmployeeIds)
            {
                await using var command = new FbCommand("""
                    SELECT COUNT(*)
                    FROM HR_EMPLOYEES
                    WHERE ID = @ID
                      AND STATUS_CODE = 'WORKING'
                      AND IS_ACTIVE = TRUE
                    """, connection, transaction);
                command.Parameters.Add("@ID", FbDbType.BigInt).Value = employeeId;
                if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
                    throw new InvalidOperationException("Có nhân viên không còn ở trạng thái đang làm việc.");
            }
        }
        else if (draft.ExternalWorkerId is > 0)
        {
            await using var command = new FbCommand("""
                SELECT COUNT(*)
                FROM EXTERNAL_WORKERS
                WHERE ID = @ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND IS_ACTIVE = TRUE
                """, connection, transaction);
            command.Parameters.Add("@ID", FbDbType.BigInt).Value = draft.ExternalWorkerId.Value;
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
                throw new InvalidOperationException("Thợ ngoài không hợp lệ hoặc đã ngừng hoạt động.");
        }
    }

    private static async Task ReplaceLinesAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        IReadOnlyList<ProductionOrderLineDraft> lines,
        CancellationToken cancellationToken)
    {
        await using (var delete = new FbCommand(
            "DELETE FROM PRODUCTION_ORDER_LINES WHERE PRODUCTION_ORDER_ID = @ID",
            connection, transaction))
        {
            delete.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var line in lines)
        {
            await using var insert = new FbCommand("""
                INSERT INTO PRODUCTION_ORDER_LINES
                    (PRODUCTION_ORDER_ID, LINE_NUMBER, MODEL_CODE, MODEL_NAME, GOLD_TYPE_CODE, GOLD_TYPE_NAME,
                     PLANNED_QUANTITY, LABOR_COST_PER_UNIT,
                     ESTIMATED_WEIGHT_CHI, GOLD_UNIT_PRICE_PER_CHI, STONE_AMOUNT_PER_UNIT_VND,
                     DESCRIPTION, NOTES)
                VALUES
                    (@OID, @LN, @MCODE, @MNAME, @GTC, @GTN, @QTY, @LABOR,
                     @WEIGHT, @GPRICE, @STONE, @DESC, @NOTES)
                """, connection, transaction);
            insert.Parameters.Add("@OID", FbDbType.BigInt).Value = orderId;
            insert.Parameters.Add("@LN", FbDbType.Integer).Value = line.LineNumber;
            insert.Parameters.Add("@MCODE", FbDbType.VarChar, 40).Value =
                string.IsNullOrWhiteSpace(line.ModelCode) ? DBNull.Value : line.ModelCode.Trim();
            insert.Parameters.Add("@MNAME", FbDbType.VarChar, 200).Value = line.ModelName.Trim();
            insert.Parameters.Add("@GTC", FbDbType.VarChar, 30).Value = line.GoldTypeCode.Trim();
            insert.Parameters.Add("@GTN", FbDbType.VarChar, 100).Value = line.GoldTypeName.Trim();
            insert.Parameters.Add("@QTY", FbDbType.Integer).Value = line.PlannedQuantity;
            insert.Parameters.Add("@LABOR", FbDbType.Decimal).Value = line.LaborCostPerUnit;
            insert.Parameters.Add("@WEIGHT", FbDbType.Decimal).Value = line.EstimatedWeightChi;
            insert.Parameters.Add("@GPRICE", FbDbType.Decimal).Value = line.GoldUnitPricePerChi;
            insert.Parameters.Add("@STONE", FbDbType.Decimal).Value = line.StoneAmountPerUnitVnd;
            insert.Parameters.Add("@DESC", FbDbType.VarChar, 1000).Value =
                string.IsNullOrWhiteSpace(line.Description) ? DBNull.Value : line.Description.Trim();
            insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                string.IsNullOrWhiteSpace(line.Notes) ? DBNull.Value : line.Notes.Trim();
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task ReplaceWorkersAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        IReadOnlyList<long> employeeIds,
        CancellationToken cancellationToken)
    {
        // Thợ đã nghỉ việc phải được giữ lại trong lệnh: union với các worker
        // không còn đang làm việc hiện có trong DB cho lệnh này.
        var resignedIds = new HashSet<long>();
        await using (var load = new FbCommand("""
            SELECT POW.EMPLOYEE_ID
              FROM PRODUCTION_ORDER_WORKERS POW
              JOIN HR_EMPLOYEES E ON E.ID = POW.EMPLOYEE_ID
             WHERE POW.PRODUCTION_ORDER_ID = @ID
               AND NOT (E.STATUS_CODE = 'WORKING' AND E.IS_ACTIVE = TRUE)
            """, connection, transaction))
        {
            load.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                resignedIds.Add(reader.GetInt64(0));
        }

        var finalIds = employeeIds.Distinct().Concat(resignedIds).Distinct().ToList();

        await using (var delete = new FbCommand(
            "DELETE FROM PRODUCTION_ORDER_WORKERS WHERE PRODUCTION_ORDER_ID = @ID",
            connection, transaction))
        {
            delete.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var employeeId in finalIds)
        {
            await using var insert = new FbCommand("""
                INSERT INTO PRODUCTION_ORDER_WORKERS (PRODUCTION_ORDER_ID, EMPLOYEE_ID)
                VALUES (@OID, @EID)
                """, connection, transaction);
            insert.Parameters.Add("@OID", FbDbType.BigInt).Value = orderId;
            insert.Parameters.Add("@EID", FbDbType.BigInt).Value = employeeId;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task SyncAttachmentsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        string orderCode,
        long userId,
        IReadOnlyList<ProductionAttachmentDraft> attachments,
        CancellationToken cancellationToken,ProductionAttachmentCommit files)
    {
        var existingPaths = new List<string>();
        await using (var load = new FbCommand(
            "SELECT ID, STORED_PATH FROM PRODUCTION_ATTACHMENTS WHERE PRODUCTION_ORDER_ID = @ID",
            connection, transaction))
        {
            load.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
            await using var reader = await load.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                existingPaths.Add(reader.GetString(1));
        }

        var keptPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in attachments)
        {
            if (attachment.MarkDeleted)
            {
                if (attachment.Id is > 0)
                {
                    await using var delete = new FbCommand(
                        "DELETE FROM PRODUCTION_ATTACHMENTS WHERE ID = @ID AND PRODUCTION_ORDER_ID = @OID",
                        connection, transaction);
                    delete.Parameters.Add("@ID", FbDbType.BigInt).Value = attachment.Id.Value;
                    delete.Parameters.Add("@OID", FbDbType.BigInt).Value = orderId;
                    await delete.ExecuteNonQueryAsync(cancellationToken);
                }

                if (!string.IsNullOrWhiteSpace(attachment.StoredPath))
                    files.DeleteAfterCommit(attachment.StoredPath);
                files.DeleteAfterCommit(attachment.StagingPath);
                continue;
            }

            var storedPath = attachment.StoredPath;
            if (!string.IsNullOrWhiteSpace(attachment.StagingPath))
            {
                storedPath = await files.FinalizeAsync(
                    attachment.StagingPath, orderCode, cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(storedPath))
                continue;

            keptPaths.Add(storedPath);

            if (attachment.Id is > 0)
            {
                await using var update = new FbCommand("""
                    UPDATE PRODUCTION_ATTACHMENTS
                       SET ATTACHMENT_KIND = @KIND,
                           STORED_PATH = @PATH,
                           ORIGINAL_FILE_NAME = @ORIG,
                           MIME_TYPE = @MIME,
                           CAPTION = @CAP,
                           DISPLAY_ORDER = @ORD,
                           LINE_NUMBER = @LN
                     WHERE ID = @ID AND PRODUCTION_ORDER_ID = @OID
                    """, connection, transaction);
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = attachment.Id.Value;
                update.Parameters.Add("@OID", FbDbType.BigInt).Value = orderId;
                update.Parameters.Add("@KIND", FbDbType.VarChar, 30).Value = attachment.AttachmentKind.Trim().ToUpperInvariant();
                update.Parameters.Add("@PATH", FbDbType.VarChar, 500).Value = storedPath;
                update.Parameters.Add("@ORIG", FbDbType.VarChar, 260).Value = attachment.OriginalFileName.Trim();
                update.Parameters.Add("@MIME", FbDbType.VarChar, 100).Value =
                    string.IsNullOrWhiteSpace(attachment.MimeType) ? DBNull.Value : attachment.MimeType.Trim();
                update.Parameters.Add("@CAP", FbDbType.VarChar, 300).Value =
                    string.IsNullOrWhiteSpace(attachment.Caption) ? DBNull.Value : attachment.Caption.Trim();
                update.Parameters.Add("@ORD", FbDbType.Integer).Value = attachment.DisplayOrder;
                update.Parameters.Add("@LN", FbDbType.Integer).Value =
                    attachment.LineNumber is > 0 ? attachment.LineNumber.Value : DBNull.Value;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var insert = new FbCommand("""
                    INSERT INTO PRODUCTION_ATTACHMENTS
                        (PRODUCTION_ORDER_ID, ATTACHMENT_KIND, STORED_PATH, ORIGINAL_FILE_NAME,
                         MIME_TYPE, CAPTION, DISPLAY_ORDER, LINE_NUMBER, CREATED_BY)
                    VALUES
                        (@OID, @KIND, @PATH, @ORIG, @MIME, @CAP, @ORD, @LN, @BY)
                    """, connection, transaction);
                insert.Parameters.Add("@OID", FbDbType.BigInt).Value = orderId;
                insert.Parameters.Add("@KIND", FbDbType.VarChar, 30).Value = attachment.AttachmentKind.Trim().ToUpperInvariant();
                insert.Parameters.Add("@PATH", FbDbType.VarChar, 500).Value = storedPath;
                insert.Parameters.Add("@ORIG", FbDbType.VarChar, 260).Value = attachment.OriginalFileName.Trim();
                insert.Parameters.Add("@MIME", FbDbType.VarChar, 100).Value =
                    string.IsNullOrWhiteSpace(attachment.MimeType) ? DBNull.Value : attachment.MimeType.Trim();
                insert.Parameters.Add("@CAP", FbDbType.VarChar, 300).Value =
                    string.IsNullOrWhiteSpace(attachment.Caption) ? DBNull.Value : attachment.Caption.Trim();
                insert.Parameters.Add("@ORD", FbDbType.Integer).Value = attachment.DisplayOrder;
                insert.Parameters.Add("@LN", FbDbType.Integer).Value =
                    attachment.LineNumber is > 0 ? attachment.LineNumber.Value : DBNull.Value;
                insert.Parameters.Add("@BY", FbDbType.BigInt).Value = userId > 0 ? userId : DBNull.Value;
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var path in existingPaths)
        {
            if (!keptPaths.Contains(path))
                files.DeleteAfterCommit(path);
        }
    }

    private sealed record LockedOrderHeader(
        string Code,
        string StatusCode,
        long RowVersion,
        string ExecutionType,
        long? ExternalWorkerId,
        long? SalesOrderId,
        long SourceLocationId,
        long FinishedLocationId,
        long? MaterialIssueDocumentId,
        long? ReturnDocumentId,
        long? FinishedReceiptDocumentId);

    private sealed record MaterialBalanceRow(
        long Id,
        long IssueDocumentId,
        long IssueLineId,
        long MaterialId,
        string MaterialCode,
        string MaterialName,
        string MaterialType,
        string UnitCode,
        decimal QuantityIssued,
        decimal QuantityInProduct,
        decimal QuantityReturned,
        decimal QuantityScrap,
        decimal QuantityLoss,
        decimal UnitCostSnapshot,
        long? WarehouseGoldPriceId,
        decimal? WarehouseGoldPricePerChi);

    private sealed record QcUnitRow(
        long Id,
        long OrderLineId,
        int UnitNumber,
        string StatusCode,
        string ModelName,
        decimal? ActualGoldWeight,
        string? ActualWeightUnitCode,
        string? GoldPurityCode,
        decimal LaborCostSnapshot,
        string? DefectNotes,
        DateTime? LastQcAt,
        long? FinishedProductId,
        long RowVersion);

    private sealed record LockedQcUnit(
        long Id,
        long ProductionOrderId,
        string OrderCode,
        string StatusCode,
        long RowVersion,
        int AttemptCount,
        long? FinishedProductId);

    private sealed record UnitComponentRow(
        string ComponentKind,
        decimal QuantityUsed,
        decimal UnitCostSnapshot,
        decimal? GoldPricePerChiSnapshot);

    private static async Task<LockedOrderHeader> LockOrderHeaderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long orderId,
        long expectedRowVersion,
        CancellationToken cancellationToken,
        bool requireVersionMatch = true)
    {
        await using var command = new FbCommand("""
            SELECT CODE, STATUS_CODE, ROW_VERSION, EXECUTION_TYPE, EXTERNAL_WORKER_ID, SALES_ORDER_ID,
                   SOURCE_LOCATION_ID, FINISHED_LOCATION_ID,
                   MATERIAL_ISSUE_DOCUMENT_ID, RETURN_DOCUMENT_ID, FINISHED_RECEIPT_DOCUMENT_ID
            FROM PRODUCTION_ORDERS
            WHERE ID = @ID
              AND COMPANY_ID = @CID
              AND BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy lệnh sản xuất.");

        var header = new LockedOrderHeader(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5),
            reader.GetInt64(6),
            reader.GetInt64(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9),
            reader.IsDBNull(10) ? null : reader.GetInt64(10));

        if (requireVersionMatch && header.RowVersion != expectedRowVersion)
            throw new InvalidOperationException("Lệnh đã được cập nhật bởi người khác. Vui lòng tải lại.");
        return header;
    }

    private static async Task EnsureOrderHasLinesAndWorkersAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        LockedOrderHeader order,
        CancellationToken cancellationToken)
    {
        await using (var lineCmd = new FbCommand(
            "SELECT COUNT(*) FROM PRODUCTION_ORDER_LINES WHERE PRODUCTION_ORDER_ID = @ID",
            connection, transaction))
        {
            lineCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
            if (Convert.ToInt32(await lineCmd.ExecuteScalarAsync(cancellationToken)) < 1)
                throw new InvalidOperationException("Lệnh phải có ít nhất một dòng mẫu trước khi giao NVL.");
        }

        if (string.Equals(order.ExecutionType, ProductionExecutionTypes.InHouse, StringComparison.OrdinalIgnoreCase))
        {
            await using var workerCmd = new FbCommand(
                "SELECT COUNT(*) FROM PRODUCTION_ORDER_WORKERS WHERE PRODUCTION_ORDER_ID = @ID",
                connection, transaction);
            workerCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
            if (Convert.ToInt32(await workerCmd.ExecuteScalarAsync(cancellationToken)) < 1)
                throw new InvalidOperationException("Lệnh nội bộ phải gắn ít nhất một nhân viên trước khi giao NVL.");
        }
        else if (order.ExternalWorkerId is null or <= 0)
        {
            throw new InvalidOperationException("Lệnh thợ ngoài phải có thợ trước khi giao NVL.");
        }
    }

    private static async Task<IReadOnlyList<MaterialBalanceRow>> LoadMaterialBalancesLockedAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        var result = new List<MaterialBalanceRow>();
        await using var command = new FbCommand("""
            SELECT B.ID, B.ISSUE_DOCUMENT_ID, B.ISSUE_LINE_ID, B.MATERIAL_ID,
                   M.CODE, M.NAME, M.MATERIAL_TYPE, B.UNIT_CODE,
                   B.QUANTITY_ISSUED, B.QUANTITY_IN_PRODUCT, B.QUANTITY_RETURNED,
                   B.QUANTITY_SCRAP, B.QUANTITY_LOSS, B.UNIT_COST_SNAPSHOT,
                   B.WAREHOUSE_GOLD_PRICE_ID, B.WAREHOUSE_GOLD_PRICE_PER_CHI
            FROM PRODUCTION_MATERIAL_BALANCES B
            JOIN MATERIALS M ON M.ID = B.MATERIAL_ID
            WHERE B.PRODUCTION_ORDER_ID = @PO
            ORDER BY B.ID
            """, connection, transaction);
        command.Parameters.Add("@PO", FbDbType.BigInt).Value = orderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new MaterialBalanceRow(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetDecimal(8), reader.GetDecimal(9), reader.GetDecimal(10),
                reader.GetDecimal(11), reader.GetDecimal(12), reader.GetDecimal(13),
                reader.IsDBNull(14) ? null : reader.GetInt64(14),
                reader.IsDBNull(15) ? null : reader.GetDecimal(15)));
        }

        return result;
    }

    private static async Task<LockedQcUnit> LockQcUnitAsync(
        FbConnection connection,
        FbTransaction transaction,
        long qcUnitId,
        long expectedRowVersion,
        CancellationToken cancellationToken)
    {
        // Firebird: WITH LOCK chỉ dùng với một bảng vật lý — không JOIN.
        await using var command = new FbCommand("""
            SELECT U.ID,
                   U.PRODUCTION_ORDER_ID,
                   (SELECT O.CODE FROM PRODUCTION_ORDERS O WHERE O.ID = U.PRODUCTION_ORDER_ID),
                   U.STATUS_CODE,
                   U.ROW_VERSION,
                   (SELECT COUNT(*) FROM PRODUCTION_QC_ATTEMPTS A WHERE A.QC_UNIT_ID = U.ID),
                   U.FINISHED_PRODUCT_ID
            FROM PRODUCTION_QC_UNITS U
            WHERE U.ID = @ID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = qcUnitId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy đơn vị QC.");
        var unit = new LockedQcUnit(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt64(4),
            Convert.ToInt32(reader.GetValue(5)),
            reader.IsDBNull(6) ? null : reader.GetInt64(6));
        if (unit.RowVersion != expectedRowVersion)
            throw new InvalidOperationException("Đơn vị QC đã thay đổi. Vui lòng tải lại.");
        return unit;
    }

    private static async Task EnsureOrderInProgressForQcAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long orderId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT STATUS_CODE, RETURN_DOCUMENT_ID
            FROM PRODUCTION_ORDERS
            WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = orderId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy lệnh sản xuất.");
        if (!string.Equals(reader.GetString(0), ProductionOrderStatuses.InProgress, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chỉ QC khi lệnh đang làm.");
        if (reader.IsDBNull(1))
            throw new InvalidOperationException("Chưa ghi nhận nhận hàng chính — không thể QC.");
    }

    private static async Task<Dictionary<long, decimal>> LoadRemainingInProductAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        long? excludeQcUnitId,
        CancellationToken cancellationToken)
    {
        var remaining = new Dictionary<long, decimal>();
        await using var command = new FbCommand("""
            SELECT B.ID,
                   B.QUANTITY_IN_PRODUCT - COALESCE((
                       SELECT SUM(C.QUANTITY_USED)
                       FROM PRODUCTION_UNIT_COMPONENTS C
                       JOIN PRODUCTION_QC_UNITS U ON U.ID = C.QC_UNIT_ID
                       WHERE C.MATERIAL_BALANCE_ID = B.ID
                         AND U.STATUS_CODE = 'PASSED'
                         AND (@EXCLUDE = 0 OR U.ID <> @EXCLUDE)), 0)
            FROM PRODUCTION_MATERIAL_BALANCES B
            WHERE B.PRODUCTION_ORDER_ID = @PO
            """, connection, transaction);
        command.Parameters.Add("@PO", FbDbType.BigInt).Value = orderId;
        command.Parameters.Add("@EXCLUDE", FbDbType.BigInt).Value = excludeQcUnitId ?? 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            remaining[reader.GetInt64(0)] = reader.GetDecimal(1);
        return remaining;
    }

    private static async Task EnsureAllocationWithinInProductAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT B.ID, B.QUANTITY_IN_PRODUCT,
                   COALESCE((SELECT SUM(C.QUANTITY_USED)
                             FROM PRODUCTION_UNIT_COMPONENTS C
                             JOIN PRODUCTION_QC_UNITS U ON U.ID = C.QC_UNIT_ID
                             WHERE C.MATERIAL_BALANCE_ID = B.ID
                               AND U.STATUS_CODE = 'PASSED'), 0)
            FROM PRODUCTION_MATERIAL_BALANCES B
            WHERE B.PRODUCTION_ORDER_ID = @PO
            """, connection, transaction);
        command.Parameters.Add("@PO", FbDbType.BigInt).Value = orderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var inProduct = reader.GetDecimal(1);
            var used = reader.GetDecimal(2);
            if (used > inProduct)
                throw new InvalidOperationException(
                    $"Tổng phân bổ cấu thành vượt lượng vào thành phẩm (balance #{reader.GetInt64(0)}).");
            if (used != inProduct)
                throw new InvalidOperationException(
                    $"Phân bổ cấu thành chưa khớp lượng vào thành phẩm (balance #{reader.GetInt64(0)}).");
        }
    }

    private static async Task EnsureFinishedLocationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long locationId,
        long branchId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM LOCATIONS
            WHERE ID = @ID AND BRANCH_ID = @BID AND IS_ACTIVE = TRUE
              AND INVENTORY_SCOPE = 'FINISHED_GOODS'
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho thành phẩm không hợp lệ (cần INVENTORY_SCOPE = FINISHED_GOODS).");
    }

    private static async Task<IReadOnlyList<QcUnitRow>> LoadQcUnitsForCompleteAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        var result = new List<QcUnitRow>();
        await using var command = new FbCommand("""
            SELECT U.ID, U.ORDER_LINE_ID, U.UNIT_NUMBER, U.STATUS_CODE, L.MODEL_NAME,
                   U.ACTUAL_GOLD_WEIGHT, U.ACTUAL_WEIGHT_UNIT_CODE, U.GOLD_PURITY_CODE,
                   U.LABOR_COST_SNAPSHOT, U.DEFECT_NOTES, U.LAST_QC_AT,
                   U.FINISHED_PRODUCT_ID, U.ROW_VERSION
            FROM PRODUCTION_QC_UNITS U
            JOIN PRODUCTION_ORDER_LINES L ON L.ID = U.ORDER_LINE_ID
            WHERE U.PRODUCTION_ORDER_ID = @PO
            ORDER BY L.LINE_NUMBER, U.UNIT_NUMBER
            """, connection, transaction);
        command.Parameters.Add("@PO", FbDbType.BigInt).Value = orderId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new QcUnitRow(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2), reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetDecimal(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDateTime(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                reader.GetInt64(12)));
        }

        return result;
    }

    private static async Task<int> LoadPlannedQuantityTotalAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COALESCE(SUM(PLANNED_QUANTITY), 0)
            FROM PRODUCTION_ORDER_LINES
            WHERE PRODUCTION_ORDER_ID = @PO
            """, connection, transaction);
        command.Parameters.Add("@PO", FbDbType.BigInt).Value = orderId;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<decimal> LoadLaborTotalAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COALESCE(SUM(PLANNED_QUANTITY * LABOR_COST_PER_UNIT), 0)
            FROM PRODUCTION_ORDER_LINES
            WHERE PRODUCTION_ORDER_ID = @PO
            """, connection, transaction);
        command.Parameters.Add("@PO", FbDbType.BigInt).Value = orderId;
        return Convert.ToDecimal(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<IReadOnlyList<UnitComponentRow>> LoadUnitComponentsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long qcUnitId,
        CancellationToken cancellationToken)
    {
        var result = new List<UnitComponentRow>();
        await using var command = new FbCommand("""
            SELECT COMPONENT_KIND, QUANTITY_USED, UNIT_COST_SNAPSHOT, GOLD_PRICE_PER_CHI_SNAPSHOT
            FROM PRODUCTION_UNIT_COMPONENTS
            WHERE QC_UNIT_ID = @ID
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = qcUnitId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new UnitComponentRow(
                reader.GetString(0),
                reader.GetDecimal(1),
                reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetDecimal(3)));
        }

        return result;
    }

    private static async Task<ProductionCompleteResult> LoadCompletedResultAsync(
        FbConnection connection,
        FbTransaction transaction,
        long productionOrderId,
        LockedOrderHeader order,
        CancellationToken cancellationToken)
    {
        if (order.FinishedReceiptDocumentId is null or <= 0)
            throw new InvalidOperationException("Lệnh đã hoàn thành nhưng thiếu phiếu nhập thành phẩm.");

        string docCode;
        await using (var docCmd = new FbCommand(
            "SELECT CODE FROM STOCK_DOCUMENTS WHERE ID = @ID", connection, transaction))
        {
            docCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = order.FinishedReceiptDocumentId.Value;
            docCode = Convert.ToString(await docCmd.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
        }

        int productCount;
        await using (var cnt = new FbCommand(
            "SELECT COUNT(*) FROM PRODUCTION_QC_UNITS WHERE PRODUCTION_ORDER_ID = @PO AND FINISHED_PRODUCT_ID IS NOT NULL",
            connection, transaction))
        {
            cnt.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
            productCount = Convert.ToInt32(await cnt.ExecuteScalarAsync(cancellationToken));
        }

        long? debtId = null;
        decimal labor = 0;
        await using (var debt = new FbCommand("""
            SELECT FIRST 1 ID, ORIGINAL_AMOUNT_VND
            FROM PAYABLE_DEBTS
            WHERE ORIGIN_DOCUMENT_TYPE = 'PRODUCTION_ORDER'
              AND ORIGIN_DOCUMENT_ID = @PO
            """, connection, transaction))
        {
            debt.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
            await using var reader = await debt.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                debtId = reader.GetInt64(0);
                labor = reader.GetDecimal(1);
            }
        }

        if (labor == 0)
        {
            await using var laborCmd = new FbCommand("""
                SELECT COALESCE(SUM(LABOR_COST_SNAPSHOT), 0)
                FROM PRODUCTION_QC_UNITS
                WHERE PRODUCTION_ORDER_ID = @PO AND STATUS_CODE = 'PASSED'
                """, connection, transaction);
            laborCmd.Parameters.Add("@PO", FbDbType.BigInt).Value = productionOrderId;
            labor = Convert.ToDecimal(await laborCmd.ExecuteScalarAsync(cancellationToken));
        }

        return new ProductionCompleteResult(
            productionOrderId, order.Code, order.FinishedReceiptDocumentId.Value, docCode,
            productCount, debtId, labor, order.RowVersion);
    }

    private static void ValidateManualFinishedProductItem(ManualFinishedProductItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (string.IsNullOrWhiteSpace(item.ProductName))
            throw new InvalidOperationException("Tên sản phẩm không được để trống.");
        if (item.TotalWeight < 0 || item.GoldWeight < 0 || item.StoneWeight < 0)
            throw new InvalidOperationException($"Trọng lượng của '{item.ProductName}' không được âm.");

        var moneyFields = new (string Label, decimal? Value)[]
        {
            ("Giá vốn", item.CostPrice),
            ("Giá bán", item.SalePrice),
            ("Công bán", item.SaleLaborCost),
            ("Công nhập", item.PurchaseLaborCost ?? item.CraftLaborCost)
        };
        foreach (var (label, value) in moneyFields)
        {
            if (value < 0)
                throw new InvalidOperationException($"{label} của '{item.ProductName}' không được âm.");
        }
    }

    private static string ResolveManualProductCodePrefix(string? productGroup)
    {
        var name = (productGroup ?? string.Empty).Trim().ToUpperInvariant();
        if (name.StartsWith("NHẪN", StringComparison.Ordinal) || name.StartsWith("NHAN", StringComparison.Ordinal))
            return "N";
        if (name.StartsWith("LẮC", StringComparison.Ordinal) || name.StartsWith("LAC", StringComparison.Ordinal))
            return "L";
        if (name.StartsWith("DÂY", StringComparison.Ordinal) || name.StartsWith("DAY", StringComparison.Ordinal))
            return "D";
        if (name.StartsWith("BÔNG", StringComparison.Ordinal) || name.StartsWith("BONG", StringComparison.Ordinal))
            return "B";
        if (name.StartsWith("VÒNG", StringComparison.Ordinal) || name.StartsWith("VONG", StringComparison.Ordinal))
            return "V";
        if (name.Length > 0 && name[0] is >= 'A' and <= 'Z')
            return name[0].ToString();
        return "SP";
    }

    private static async Task<long?> ResolveFinishedProductGroupIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string? productGroup,
        CancellationToken cancellationToken)
    {
        var name = (productGroup ?? string.Empty).Trim();
        if (name.Length == 0)
            return null;

        try
        {
            await using var command = new FbCommand("""
                SELECT FIRST 1 ID
                FROM FINISHED_PRODUCT_GROUPS
                WHERE COMPANY_ID = @CID AND IS_ACTIVE = TRUE
                  AND (UPPER(TRIM(NAME)) = @NAME OR UPPER(TRIM(CODE)) = @NAME)
                """, connection, transaction);
            command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            command.Parameters.Add("@NAME", FbDbType.VarChar, 100).Value = name.ToUpperInvariant();
            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            return scalar is null or DBNull ? null : Convert.ToInt64(scalar);
        }
        catch (FbException)
        {
            // Catalog nhóm thành phẩm chưa có (migration 61) — bỏ qua, không chặn nhập kho.
            return null;
        }
    }

    private static async Task<string> LoadProductionOrderCodeAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long productionOrderId,
        CancellationToken cancellationToken)
    {
        var link = await LoadProductionOrderLinkAsync(
            connection, transaction, companyId, branchId, productionOrderId, cancellationToken);
        return link.Code;
    }

    private static async Task<ProductionOrderReceiptLink> LoadProductionOrderLinkAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long productionOrderId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE, STATUS_CODE, SALES_ORDER_ID, FINISHED_RECEIPT_DOCUMENT_ID, ROW_VERSION
            FROM PRODUCTION_ORDERS
            WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = productionOrderId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy lệnh sản xuất.");

        return new ProductionOrderReceiptLink(
            Convert.ToString(reader.GetValue(0)) ?? string.Empty,
            Convert.ToString(reader.GetValue(1)) ?? string.Empty,
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.GetInt64(4));
    }

    private sealed record ProductionOrderReceiptLink(
        string Code,
        string StatusCode,
        long? SalesOrderId,
        long? FinishedReceiptDocumentId,
        long RowVersion);

    private static bool IsMissingTableViolation(FbException ex, string tableName) =>
        (ex.Message?.Contains("Table unknown", StringComparison.OrdinalIgnoreCase) ?? false)
        && (ex.Message?.Contains(tableName, StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsUniqueViolation(FbException ex) =>
        ex.ErrorCode == 335544665
        || (ex.Message?.Contains("UQ_", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.Message?.Contains("violation of PRIMARY or UNIQUE", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsSalesOrderUniqueViolation(FbException ex) =>
        ex.ErrorCode == 335544665
        || (ex.Message?.Contains("UQ_PO_SALES_ORDER", StringComparison.OrdinalIgnoreCase) ?? false);

    private static bool IsLegacyNotNullViolation(FbException ex)
    {
        var message = ex.Message ?? string.Empty;
        if (!message.Contains("validation error", StringComparison.OrdinalIgnoreCase)
            && !message.Contains("*** null ***", StringComparison.OrdinalIgnoreCase))
            return false;

        return message.Contains("SALES_ORDER_ID", StringComparison.OrdinalIgnoreCase)
            || message.Contains("SALES_ORDER_ITEM_ID", StringComparison.OrdinalIgnoreCase)
            || message.Contains("CUSTOMER_ID", StringComparison.OrdinalIgnoreCase)
            || message.Contains("DESIGN_ID", StringComparison.OrdinalIgnoreCase)
            || message.Contains("BOM_ID", StringComparison.OrdinalIgnoreCase)
            || message.Contains("PRODUCT_NAME", StringComparison.OrdinalIgnoreCase)
            || message.Contains("\"QUANTITY\"", StringComparison.OrdinalIgnoreCase);
    }
}
