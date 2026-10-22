using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Production;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Sales;

public sealed class FirebirdCraftOrderApplicationService : ICraftOrderApplicationService
{
    private readonly FirebirdFinancialSettlementService _settlements = new();
    private readonly IProductionAttachmentStore _attachmentStore;

    public FirebirdCraftOrderApplicationService(IProductionAttachmentStore attachmentStore)
    {
        _attachmentStore = attachmentStore ?? throw new ArgumentNullException(nameof(attachmentStore));
    }

    public async Task<IReadOnlyList<CraftCustomerOption>> SearchCustomersAsync(
        string? searchText,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = new List<CraftCustomerOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.ID, C.CODE, C.NAME, C.PHONE
            FROM CUSTOMERS C
            WHERE C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND C.IS_ACTIVE = TRUE
              AND C.IS_WALK_IN = FALSE
              AND (@HAS_SEARCH = 0
                   OR C.CODE CONTAINING @SEARCH
                   OR C.NAME CONTAINING @SEARCH
                   OR C.PHONE CONTAINING @SEARCH)
            ORDER BY C.NAME, C.CODE
            ROWS 100
            """, connection);

        var search = searchText?.Trim() ?? string.Empty;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CraftCustomerOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                IsWalkIn: false));
        }

        return result;
    }

    public async Task<CraftOrderHeader?> GetHeaderAsync(
        long salesOrderId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (salesOrderId <= 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT O.ID, O.CODE, O.STATUS_CODE, C.ID, C.CODE, C.NAME, C.PHONE, O.GRAND_TOTAL,
                   COALESCE((
                       SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                       FROM ORDER_PAYMENTS P
                       WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE
                   ), 0),
                   CASE WHEN O.FINISHED_GOODS_ISSUE_DOCUMENT_ID IS NULL THEN 0 ELSE 1 END
            FROM SALES_ORDERS O
            JOIN CUSTOMERS C ON C.ID = O.CUSTOMER_ID
            WHERE O.ID = @ID
              AND O.BRANCH_ID = @BID
              AND O.ORDER_TYPE = 'CUSTOM'
            """, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CraftOrderHeader(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetDecimal(7),
            reader.GetDecimal(8),
            reader.GetInt32(9) == 1);
    }

    public async Task<IReadOnlyList<CraftOrderLineItem>> GetLinesAsync(
        long salesOrderId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (salesOrderId <= 0)
            return Array.Empty<CraftOrderLineItem>();

        var result = new List<CraftOrderLineItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var hasSampleDetails = await HasCraftSampleDetailsAsync(connection, null, cancellationToken);
        var lineSql = hasSampleDetails
            ? """
            SELECT I.LINE_NUMBER, I.PRODUCT_NAME, COALESCE(I.GOLD_PURITY, ''),
                   I.QUANTITY, COALESCE(I.ESTIMATED_WEIGHT, 0),
                   COALESCE(I.GOLD_UNIT_PRICE, 0), COALESCE(I.LABOR_AMOUNT, 0),
                   COALESCE(I.STONE_AMOUNT, 0), COALESCE(I.LINE_TOTAL, 0),
                   I.MODEL_CODE, I.DESCRIPTION, I.NOTES
            FROM SALES_ORDER_ITEMS I
            JOIN SALES_ORDERS O ON O.ID = I.ORDER_ID
            WHERE I.ORDER_ID = @ID
              AND O.BRANCH_ID = @BID
              AND O.ORDER_TYPE = 'CUSTOM'
            ORDER BY I.LINE_NUMBER
            """
            : """
            SELECT I.LINE_NUMBER, I.PRODUCT_NAME, COALESCE(I.GOLD_PURITY, ''),
                   I.QUANTITY, COALESCE(I.ESTIMATED_WEIGHT, 0),
                   COALESCE(I.GOLD_UNIT_PRICE, 0), COALESCE(I.LABOR_AMOUNT, 0),
                   COALESCE(I.STONE_AMOUNT, 0), COALESCE(I.LINE_TOTAL, 0),
                   CAST(NULL AS VARCHAR(40)), CAST(NULL AS VARCHAR(1000)), CAST(NULL AS VARCHAR(1000))
            FROM SALES_ORDER_ITEMS I
            JOIN SALES_ORDERS O ON O.ID = I.ORDER_ID
            WHERE I.ORDER_ID = @ID
              AND O.BRANCH_ID = @BID
              AND O.ORDER_TYPE = 'CUSTOM'
            ORDER BY I.LINE_NUMBER
            """;
        await using var command = new FbCommand(lineSql, connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CraftOrderLineItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                Convert.ToInt32(reader.GetValue(3)),
                reader.GetDecimal(4),
                reader.GetDecimal(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                Array.Empty<CraftOrderAttachmentItem>()));
        }

        var attachmentsByLine = new Dictionary<int, List<CraftOrderAttachmentItem>>();
        if (hasSampleDetails)
        {
            await using var attachmentCommand = new FbCommand("""
            SELECT ID, LINE_NUMBER, ATTACHMENT_KIND, STORED_PATH, ORIGINAL_FILE_NAME,
                   MIME_TYPE, CAPTION, DISPLAY_ORDER
            FROM CRAFT_ORDER_ATTACHMENTS
            WHERE SALES_ORDER_ID = @ID
            ORDER BY LINE_NUMBER, DISPLAY_ORDER, ID
            """, connection);
            attachmentCommand.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
            await using var attachmentReader = await attachmentCommand.ExecuteReaderAsync(cancellationToken);
            while (await attachmentReader.ReadAsync(cancellationToken))
            {
                var lineNumber = attachmentReader.GetInt32(1);
                if (!attachmentsByLine.TryGetValue(lineNumber, out var list))
                {
                    list = [];
                    attachmentsByLine[lineNumber] = list;
                }
                list.Add(new CraftOrderAttachmentItem(
                    attachmentReader.GetInt64(0),
                    attachmentReader.GetString(2),
                    attachmentReader.GetString(3),
                    attachmentReader.GetString(4),
                    attachmentReader.IsDBNull(5) ? null : attachmentReader.GetString(5),
                    attachmentReader.IsDBNull(6) ? null : attachmentReader.GetString(6),
                    attachmentReader.GetInt32(7)));
            }
        }

        return result.Select(line => line with
        {
            Attachments = attachmentsByLine.TryGetValue(line.LineNumber, out var list)
                ? list
                : Array.Empty<CraftOrderAttachmentItem>()
        }).ToList();
    }

    public async Task<CraftOrderCreateResult> CreateWithDepositAsync(
        CraftOrderDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var errors = CraftOrderValidator.Validate(draft);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var quoted = CraftOrderValidator.QuotedTotal(draft.Lines);
        var deposit = draft.DepositAllocations.Sum(x => x.AmountVnd);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;
        var finalizedAttachmentPaths = new List<string>();

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            await EnsureNamedCustomerAsync(connection, fbTx, companyId, draft.CustomerId, cancellationToken);
            var hasSampleDetails = await HasCraftSampleDetailsAsync(connection, fbTx, cancellationToken);

            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "CRAFT_ORDER", session.BranchId, "DH", cancellationToken);

            long orderId;
            await using (var insert = new FbCommand("""
                INSERT INTO SALES_ORDERS
                    (COMPANY_ID, BRANCH_ID, CODE, ORDER_TYPE, CUSTOMER_ID, ORDER_DATE,
                     SALES_PERSON_ID, STATUS_CODE, SUBTOTAL, DISCOUNT_AMOUNT, TAX_AMOUNT,
                     GRAND_TOTAL, NOTES, CREATED_BY, UPDATED_BY, CREATED_AT, UPDATED_AT)
                VALUES
                    (@CID, @BID, @CODE, @OTYPE, @CUSTOMER, @DATE,
                     @SALESPERSON, @STATUS, @SUBTOTAL, 0, 0,
                     @TOTAL, @NOTES, @BY, @BY, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insert.Parameters.Add("@CODE", FbDbType.VarChar, 50).Value = code;
                insert.Parameters.Add("@OTYPE", FbDbType.VarChar, 20).Value = SalesOrderTypes.Custom;
                insert.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = draft.CustomerId;
                insert.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.OrderDate.Date;
                insert.Parameters.Add("@SALESPERSON", FbDbType.BigInt).Value =
                    draft.SalespersonId is > 0 ? draft.SalespersonId.Value : session.UserId;
                insert.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = CraftOrderStatuses.Deposited;
                insert.Parameters.Add("@SUBTOTAL", FbDbType.Decimal).Value = quoted;
                insert.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = quoted;
                insert.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    (object?)draft.Notes?.Trim() ?? DBNull.Value;
                insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                orderId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            var lineNumber = 0;
            foreach (var line in draft.Lines)
            {
                lineNumber++;
                var lineTotal = CraftOrderValidator.LineTotal(line);
                var insertLineSql = hasSampleDetails
                    ? """
                    INSERT INTO SALES_ORDER_ITEMS
                        (ORDER_ID, LINE_NUMBER, PRODUCT_NAME, GOLD_PURITY, QUANTITY, ESTIMATED_WEIGHT,
                         GOLD_UNIT_PRICE, LABOR_AMOUNT, STONE_AMOUNT, LINE_SUBTOTAL, LINE_TOTAL,
                         MODEL_CODE, DESCRIPTION, NOTES,
                         FINISHED_PRODUCT_ID, IS_PRICE_OVERRIDE)
                    VALUES
                        (@ORDER_ID, @LINE, @NAME, @PURITY, @QTY, @WEIGHT,
                         @GOLD_PRICE, @LABOR, @STONE, @TOTAL, @TOTAL,
                         @MODEL, @DESCRIPTION, @NOTES,
                         NULL, FALSE)
                    """
                    : """
                    INSERT INTO SALES_ORDER_ITEMS
                        (ORDER_ID, LINE_NUMBER, PRODUCT_NAME, GOLD_PURITY, QUANTITY, ESTIMATED_WEIGHT,
                         GOLD_UNIT_PRICE, LABOR_AMOUNT, STONE_AMOUNT, LINE_SUBTOTAL, LINE_TOTAL,
                         FINISHED_PRODUCT_ID, IS_PRICE_OVERRIDE)
                    VALUES
                        (@ORDER_ID, @LINE, @NAME, @PURITY, @QTY, @WEIGHT,
                         @GOLD_PRICE, @LABOR, @STONE, @TOTAL, @TOTAL,
                         NULL, FALSE)
                    """;
                await using var insertLine = new FbCommand(insertLineSql, connection, fbTx);
                insertLine.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
                insertLine.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNumber;
                insertLine.Parameters.Add("@NAME", FbDbType.VarChar, 255).Value = line.ModelName.Trim();
                insertLine.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value = line.GoldPurity.Trim();
                insertLine.Parameters.Add("@QTY", FbDbType.Integer).Value = line.Quantity;
                insertLine.Parameters.Add("@WEIGHT", FbDbType.Decimal).Value = line.EstimatedWeightChi;
                insertLine.Parameters.Add("@GOLD_PRICE", FbDbType.Decimal).Value = line.GoldUnitPricePerChi;
                insertLine.Parameters.Add("@LABOR", FbDbType.Decimal).Value = line.LaborAmountPerUnitVnd;
                insertLine.Parameters.Add("@STONE", FbDbType.Decimal).Value = line.StoneAmountPerUnitVnd;
                insertLine.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = lineTotal;
                if (hasSampleDetails)
                {
                    insertLine.Parameters.Add("@MODEL", FbDbType.VarChar, 40).Value =
                        string.IsNullOrWhiteSpace(line.ModelCode) ? DBNull.Value : line.ModelCode.Trim();
                    insertLine.Parameters.Add("@DESCRIPTION", FbDbType.VarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(line.Description) ? DBNull.Value : line.Description.Trim();
                    insertLine.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                        string.IsNullOrWhiteSpace(line.Notes) ? DBNull.Value : line.Notes.Trim();
                }
                await insertLine.ExecuteNonQueryAsync(cancellationToken);

                if (!hasSampleDetails)
                    continue;

                var displayOrder = 0;
                foreach (var attachment in line.Attachments ?? Array.Empty<CraftOrderAttachmentDraft>())
                {
                    var storedPath = attachment.StoredPath;
                    if (!string.IsNullOrWhiteSpace(attachment.StagingPath))
                    {
                        storedPath = await _attachmentStore.FinalizeAsync(
                            attachment.StagingPath, code, cancellationToken);
                        finalizedAttachmentPaths.Add(storedPath);
                    }
                    if (string.IsNullOrWhiteSpace(storedPath))
                        continue;

                    await using var insertAttachment = new FbCommand("""
                        INSERT INTO CRAFT_ORDER_ATTACHMENTS
                            (SALES_ORDER_ID, LINE_NUMBER, ATTACHMENT_KIND, STORED_PATH,
                             ORIGINAL_FILE_NAME, MIME_TYPE, CAPTION, DISPLAY_ORDER,
                             CREATED_BY, CREATED_AT)
                        VALUES
                            (@ORDER_ID, @LINE, @KIND, @PATH,
                             @ORIGINAL, @MIME, @CAPTION, @DISPLAY_ORDER,
                             @BY, CURRENT_TIMESTAMP)
                        """, connection, fbTx);
                    insertAttachment.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
                    insertAttachment.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNumber;
                    insertAttachment.Parameters.Add("@KIND", FbDbType.VarChar, 30).Value = attachment.AttachmentKind;
                    insertAttachment.Parameters.Add("@PATH", FbDbType.VarChar, 500).Value = storedPath;
                    insertAttachment.Parameters.Add("@ORIGINAL", FbDbType.VarChar, 260).Value = attachment.OriginalFileName;
                    insertAttachment.Parameters.Add("@MIME", FbDbType.VarChar, 100).Value =
                        string.IsNullOrWhiteSpace(attachment.MimeType) ? DBNull.Value : attachment.MimeType;
                    insertAttachment.Parameters.Add("@CAPTION", FbDbType.VarChar, 300).Value =
                        string.IsNullOrWhiteSpace(attachment.Caption) ? DBNull.Value : attachment.Caption;
                    insertAttachment.Parameters.Add("@DISPLAY_ORDER", FbDbType.Integer).Value = displayOrder++;
                    insertAttachment.Parameters.Add("@BY", FbDbType.BigInt).Value =
                        session.UserId > 0 ? session.UserId : DBNull.Value;
                    await insertAttachment.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            var settlement = await _settlements.PostInTransactionAsync(
                connection, fbTx, companyId, session.BranchId,
                new FinancialSettlementDraft(
                    SettlementTypes.Receipt,
                    SettlementPartyTypes.Customer,
                    draft.CustomerId,
                    draft.OrderDate.Date,
                    draft.DepositAllocations,
                    [new SettlementApplicationDraft(
                        SettlementApplicationTargetTypes.SalesOrder, orderId, deposit)],
                    $"Đặt cọc đơn đặt {code}"),
                session.UserId > 0 ? session.UserId : null,
                cancellationToken);

            await InsertOrderPaymentsAsync(
                connection, fbTx, orderId, draft.OrderDate.Date, settlement,
                code, $"Đặt cọc {code}", session, cancellationToken);

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "CRAFT_ORDER_CREATED",
                session.UserId, session.Username, session.BranchId,
                "SALES_ORDER", orderId.ToString(), code, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new CraftOrderCreateResult(
                orderId, code, CraftOrderStatuses.Deposited, quoted, deposit,
                settlement.SettlementId, settlement.Code);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var path in finalizedAttachmentPaths)
                _attachmentStore.TryDeleteManagedFile(path);
            throw;
        }
    }

    public async Task<IReadOnlyList<CraftOrderListItem>> SearchOrdersAsync(
        CraftOrderSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var result = new List<CraftOrderListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT O.ID, O.CODE, O.ORDER_DATE, O.STATUS_CODE, C.ID, C.NAME, C.PHONE,
                   O.GRAND_TOTAL,
                   COALESCE((
                       SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                       FROM ORDER_PAYMENTS P
                       WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE
                   ), 0),
                   CASE WHEN O.FINISHED_GOODS_ISSUE_DOCUMENT_ID IS NULL THEN 0 ELSE 1 END
            FROM SALES_ORDERS O
            JOIN CUSTOMERS C ON C.ID = O.CUSTOMER_ID
            WHERE O.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND O.BRANCH_ID = @BID
              AND O.ORDER_TYPE = 'CUSTOM'
              AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
              AND (@HAS_TO = 0 OR O.ORDER_DATE < @TO_DATE)
              AND (@HAS_STATUS = 0 OR O.STATUS_CODE = @STATUS)
              AND (@HAS_SEARCH = 0
                   OR O.CODE CONTAINING @SEARCH
                   OR C.NAME CONTAINING @SEARCH
                   OR C.PHONE CONTAINING @SEARCH)
            ORDER BY O.ORDER_DATE DESC, O.ID DESC
            ROWS 200
            """, connection);

        var search = filter.SearchText?.Trim() ?? string.Empty;
        var status = filter.StatusCode?.Trim().ToUpperInvariant() ?? string.Empty;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_FROM", FbDbType.Integer).Value = filter.FromDate.HasValue ? 1 : 0;
        command.Parameters.Add("@FROM_DATE", FbDbType.TimeStamp).Value =
            filter.FromDate?.Date ?? (object)DBNull.Value;
        command.Parameters.Add("@HAS_TO", FbDbType.Integer).Value = filter.ToDate.HasValue ? 1 : 0;
        command.Parameters.Add("@TO_DATE", FbDbType.TimeStamp).Value =
            filter.ToDate.HasValue ? filter.ToDate.Value.Date.AddDays(1) : DBNull.Value;
        command.Parameters.Add("@HAS_STATUS", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = status;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CraftOrderListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetInt32(9) == 1));
        }

        return result;
    }

    public async Task<CraftOrderDeliverResult> DeliverAsync(
        CraftOrderDeliverDraft draft,
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
            var locked = await LockCraftOrderAsync(
                connection, fbTx, companyId, session.BranchId, draft.SalesOrderId, cancellationToken);

            if (locked.Status is CraftOrderStatuses.Cancelled or CraftOrderStatuses.Completed)
                throw new InvalidOperationException("Đơn đã hoàn tất hoặc hủy — không giao.");
            if (!string.Equals(locked.Status, CraftOrderStatuses.Ready, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Chỉ giao đơn ở trạng thái READY (đã hoàn tất SX).");
            if (locked.HasFinishedGoodsIssue)
                throw new InvalidOperationException("Đơn đã xuất thành phẩm — không giao lần hai.");

            var remaining = Math.Max(0m, locked.GrandTotalVnd - locked.PaidAmountVnd);
            var errors = CraftOrderValidator.ValidateDeliver(draft, remaining);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            // One issue document completes the order: never silently deliver only
            // the products from the largest location group.
            await using (var count = new FbCommand("""
                SELECT COALESCE(SUM(QUANTITY), 0) FROM SALES_ORDER_ITEMS WHERE ORDER_ID = @ORDER_ID
                """, connection, fbTx))
            {
                count.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = draft.SalesOrderId;
                var expected = Convert.ToDecimal(await count.ExecuteScalarAsync(cancellationToken));
                if (expected <= 0 || draft.FinishedProductIds.Distinct().Count() != expected)
                    throw new InvalidOperationException("Phải nhập kho và giao đủ các món của đơn đặt trong một lần.");
            }

            var issueDocumentId = await FinishedGoodsPostingSupport.PostCraftOrderIssueAsync(
                connection, fbTx, companyId, session,
                new FinishedGoodsIssueDraft(
                    draft.SalesOrderId,
                    draft.DocumentDate,
                    draft.SourceLocationId,
                    draft.DeliveredByName,
                    draft.ReceivedByName,
                    draft.Notes,
                    draft.FinishedProductIds.Select(id => new FinishedGoodsIssueLineDraft(id)).ToList()),
                cancellationToken);

            var collections = draft.CollectionAllocations ?? Array.Empty<PaymentAllocationDraft>();
            var collectSum = collections.Sum(x => x.AmountVnd);
            if (remaining > 0)
            {
                await PostRemainingSettlementAsync(
                    connection, fbTx, companyId, session,
                    draft.SalesOrderId, locked.OrderCode, locked.CustomerId,
                    draft.DocumentDate.Date, collections, collectSum,
                    draft.ReceivableAmountVnd, draft.Notes, cancellationToken);
            }

            await MarkCraftOrderCompletedAsync(
                connection, fbTx, companyId, session, draft.SalesOrderId, cancellationToken);

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "CRAFT_ORDER_DELIVERED",
                session.UserId, session.Username, session.BranchId,
                "SALES_ORDER", draft.SalesOrderId.ToString(), locked.OrderCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new CraftOrderDeliverResult(
                draft.SalesOrderId,
                locked.OrderCode,
                CraftOrderStatuses.Completed,
                issueDocumentId,
                collectSum,
                draft.ReceivableAmountVnd);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CraftOrderCollectResult> CollectRemainingAsync(
        CraftOrderCollectDraft draft,
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
            var locked = await LockCraftOrderAsync(
                connection, fbTx, companyId, session.BranchId, draft.SalesOrderId, cancellationToken);

            if (locked.Status is CraftOrderStatuses.Cancelled or CraftOrderStatuses.Completed)
                throw new InvalidOperationException("Đơn đã hoàn tất hoặc hủy — không thu thêm.");
            if (!locked.HasFinishedGoodsIssue)
                throw new InvalidOperationException("Đơn chưa giao hàng — dùng nút Giao hàng.");

            var remaining = Math.Max(0m, locked.GrandTotalVnd - locked.PaidAmountVnd);
            var errors = CraftOrderValidator.ValidateCollectRemaining(remaining, draft);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            var collections = draft.CollectionAllocations ?? Array.Empty<PaymentAllocationDraft>();
            var collectSum = collections.Sum(x => x.AmountVnd);
            long? settlementId = null;
            string? settlementCode = null;

            if (remaining > 0)
            {
                var settlement = await PostRemainingSettlementAsync(
                    connection, fbTx, companyId, session,
                    draft.SalesOrderId, locked.OrderCode, locked.CustomerId,
                    draft.PaymentDate.Date, collections, collectSum,
                    draft.ReceivableAmountVnd, draft.Notes, cancellationToken);
                settlementId = settlement?.SettlementId;
                settlementCode = settlement?.Code;
            }

            await MarkCraftOrderCompletedAsync(
                connection, fbTx, companyId, session, draft.SalesOrderId, cancellationToken);

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "CRAFT_ORDER_COLLECTED",
                session.UserId, session.Username, session.BranchId,
                "SALES_ORDER", draft.SalesOrderId.ToString(), locked.OrderCode, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new CraftOrderCollectResult(
                draft.SalesOrderId,
                locked.OrderCode,
                CraftOrderStatuses.Completed,
                collectSum,
                draft.ReceivableAmountVnd,
                settlementId,
                settlementCode);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private async Task<FinancialSettlementResult?> PostRemainingSettlementAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        AuthenticatedSession session,
        long salesOrderId,
        string orderCode,
        long customerId,
        DateTime paymentDate,
        IReadOnlyList<PaymentAllocationDraft> collections,
        decimal collectSum,
        decimal receivableAmountVnd,
        string? notes,
        CancellationToken cancellationToken)
    {
        FinancialSettlementResult? settlement = null;
        if (collectSum > 0)
        {
            settlement = await _settlements.PostInTransactionAsync(
                connection, transaction, companyId, session.BranchId,
                new FinancialSettlementDraft(
                    SettlementTypes.Receipt,
                    SettlementPartyTypes.Customer,
                    customerId,
                    paymentDate,
                    collections,
                    [new SettlementApplicationDraft(
                        SettlementApplicationTargetTypes.SalesOrder, salesOrderId, collectSum)],
                    string.IsNullOrWhiteSpace(notes)
                        ? $"Thu phần còn đơn đặt {orderCode}"
                        : notes.Trim()),
                session.UserId > 0 ? session.UserId : null,
                cancellationToken);

            await InsertOrderPaymentsAsync(
                connection, transaction, salesOrderId, paymentDate, settlement,
                orderCode, $"Thu phần còn {orderCode}", session, cancellationToken);
        }

        if (receivableAmountVnd > 0)
        {
            var debtCode = await StockPostingSupport.NextDocumentCodeAsync(
                connection, transaction, "RECEIVABLE_DEBT", session.BranchId, "PTU", cancellationToken);
            await using var insertDebt = new FbCommand("""
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
                """, connection, transaction);
            insertDebt.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            insertDebt.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            insertDebt.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = debtCode;
            insertDebt.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = customerId;
            insertDebt.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = salesOrderId;
            insertDebt.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = receivableAmountVnd;
            insertDebt.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                $"Công nợ đơn đặt {orderCode}";
            insertDebt.Parameters.Add("@BY", FbDbType.BigInt).Value =
                session.UserId > 0 ? session.UserId : DBNull.Value;
            await insertDebt.ExecuteNonQueryAsync(cancellationToken);
        }

        return settlement;
    }

    private static async Task MarkCraftOrderCompletedAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        AuthenticatedSession session,
        long salesOrderId,
        CancellationToken cancellationToken)
    {
        await using var update = new FbCommand("""
            UPDATE SALES_ORDERS
               SET STATUS_CODE = @STATUS,
                   UPDATED_BY = @BY,
                   UPDATED_AT = CURRENT_TIMESTAMP
             WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
            """, connection, transaction);
        update.Parameters.Add("@STATUS", FbDbType.VarChar, 30).Value = CraftOrderStatuses.Completed;
        update.Parameters.Add("@BY", FbDbType.BigInt).Value =
            session.UserId > 0 ? session.UserId : DBNull.Value;
        update.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
        update.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        update.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CraftOrderLockInfo> LockCraftOrderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long salesOrderId,
        CancellationToken cancellationToken)
    {
        await using var lockCmd = new FbCommand("""
            SELECT O.CODE, O.STATUS_CODE, O.CUSTOMER_ID, O.GRAND_TOTAL,
                   COALESCE((
                       SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                       FROM ORDER_PAYMENTS P
                       WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE
                   ), 0),
                   CASE WHEN O.FINISHED_GOODS_ISSUE_DOCUMENT_ID IS NULL THEN 0 ELSE 1 END
            FROM SALES_ORDERS O
            WHERE O.ID = @ID
              AND O.COMPANY_ID = @CID
              AND O.BRANCH_ID = @BID
              AND O.ORDER_TYPE = 'CUSTOM'
            WITH LOCK
            """, connection, transaction);
        lockCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = salesOrderId;
        lockCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        lockCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await lockCmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy đơn đặt.");

        return new CraftOrderLockInfo(
            reader.GetString(0),
            reader.GetString(1).Trim().ToUpperInvariant(),
            reader.GetInt64(2),
            reader.GetDecimal(3),
            reader.GetDecimal(4),
            reader.GetInt32(5) == 1);
    }

    private static async Task<bool> HasCraftSampleDetailsAsync(
        FbConnection connection,
        FbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT
                (SELECT COUNT(*)
                   FROM RDB$RELATION_FIELDS
                  WHERE RDB$RELATION_NAME = 'SALES_ORDER_ITEMS'
                    AND RDB$FIELD_NAME IN ('MODEL_CODE', 'DESCRIPTION', 'NOTES')),
                (SELECT COUNT(*)
                   FROM RDB$RELATIONS
                  WHERE RDB$RELATION_NAME = 'CRAFT_ORDER_ATTACHMENTS')
            FROM RDB$DATABASE
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
               && Convert.ToInt32(reader.GetValue(0)) == 3
               && Convert.ToInt32(reader.GetValue(1)) == 1;
    }

    private sealed record CraftOrderLockInfo(
        string OrderCode,
        string Status,
        long CustomerId,
        decimal GrandTotalVnd,
        decimal PaidAmountVnd,
        bool HasFinishedGoodsIssue);

    private static async Task EnsureNamedCustomerAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long customerId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT IS_WALK_IN, IS_ACTIVE
            FROM CUSTOMERS
            WHERE ID = @ID AND COMPANY_ID = @CID
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = customerId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy khách hàng.");
        var isWalkIn = reader.GetBoolean(0);
        var active = !reader.IsDBNull(1) && reader.GetBoolean(1);
        if (!active)
            throw new InvalidOperationException("Khách hàng không còn hoạt động.");
        if (isWalkIn)
            throw new InvalidOperationException("Đơn đặt không dùng khách lẻ. Chọn khách có hồ sơ.");
    }

    private static async Task InsertOrderPaymentsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long orderId,
        DateTime paymentDate,
        FinancialSettlementResult settlement,
        string orderCode,
        string paymentNotes,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var allocationRows = new List<(long Id, string Method, decimal Amount, string? Reference, long VoucherId)>();
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
            {
                allocationRows.Add((
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetDecimal(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt64(4)));
            }
        }

        if (allocationRows.Count == 0)
            throw new InvalidOperationException($"Không ghi được dòng thanh toán cho đơn {orderCode}.");

        foreach (var row in allocationRows)
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
            insert.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
            insert.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = paymentDate;
            insert.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = row.Amount;
            insert.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value = row.Method;
            insert.Parameters.Add("@REFERENCE", FbDbType.VarChar, 100).Value =
                (object?)row.Reference ?? settlement.Code;
            insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = paymentNotes;
            insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
                session.UserId > 0 ? session.UserId : DBNull.Value;
            insert.Parameters.Add("@SETTLEMENT", FbDbType.BigInt).Value = settlement.SettlementId;
            insert.Parameters.Add("@ALLOCATION", FbDbType.BigInt).Value = row.Id;
            insert.Parameters.Add("@VOUCHER", FbDbType.BigInt).Value = row.VoucherId;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
