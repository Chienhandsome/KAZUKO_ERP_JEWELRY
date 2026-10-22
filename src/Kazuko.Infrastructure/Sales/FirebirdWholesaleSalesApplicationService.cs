using System.Globalization;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Core.Sales;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Finance;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Sales;

public sealed class FirebirdWholesaleSalesApplicationService : IWholesaleSalesApplicationService
{
    private const int ProductSearchLimit = 20;
    private readonly FirebirdFinancialSettlementService _settlements = new();

    private const string ProductFrom = """
        FROM FINISHED_PRODUCTS F
        JOIN LOCATIONS L ON L.ID = F.LOCATION_ID
                        AND L.BRANCH_ID = @BRANCH_ID
                        AND L.INVENTORY_SCOPE IN ('FINISHED_GOODS', 'SALES_FLOOR')
                        AND L.IS_ACTIVE = TRUE
        JOIN WAREHOUSE_GOLD_PRICES W ON W.COMPANY_ID = F.COMPANY_ID
                                    AND W.IS_ACTIVE = TRUE
                                    AND W.SELL_PRICE_PER_CHI > 0
                                    AND W.ID = COALESCE(
                                        (SELECT FIRST 1 P.ID FROM WAREHOUSE_GOLD_PRICES P
                                          WHERE P.COMPANY_ID = F.COMPANY_ID
                                            AND P.IS_ACTIVE = TRUE
                                            AND F.GOLD_TYPE_ID IS NOT NULL
                                            AND P.GOLD_TYPE_ID = F.GOLD_TYPE_ID),
                                        (SELECT MIN(P.ID) FROM WAREHOUSE_GOLD_PRICES P
                                          WHERE P.COMPANY_ID = F.COMPANY_ID
                                            AND P.IS_ACTIVE = TRUE
                                            AND UPPER(TRIM(P.GOLD_TYPE_CODE)) = UPPER(TRIM(COALESCE(F.GOLD_PURITY, '')))))
        """;

    private const string ProductSelect = """
        F.ID AS FinishedProductId, F.CODE AS ProductCode, F.PRODUCT_NAME AS ProductName,
        L.ID AS SourceLocationId, L.NAME AS LocationName,
        COALESCE(F.GOLD_PURITY, '') AS GoldPurity, F.ROW_VERSION AS RowVersion,
        F.TOTAL_WEIGHT AS TotalWeightChi, F.STONE_WEIGHT AS StoneWeightChi, F.GOLD_WEIGHT AS GoldWeightChi,
        W.SELL_PRICE_PER_CHI AS SellPricePerChi, F.LABOR_COST AS LaborCost,
        F.NON_GOLD_MATERIAL_COST AS NonGoldMaterialCost
        """;

    public async Task<IReadOnlyList<WholesaleLocationOption>> GetSourceLocationsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<WholesaleLocationOption>();

        var rows = await connection.QueryAsync<WholesaleLocationRow>(
            new CommandDefinition("""
                SELECT L.ID AS Id, L.CODE AS Code, L.NAME AS Name,
                       L.INVENTORY_SCOPE AS InventoryScope,
                       CASE WHEN L.INVENTORY_SCOPE = 'FINISHED_GOODS' THEN TRUE ELSE FALSE END AS IsDefault
                FROM LOCATIONS L
                JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @COMPANY_ID
                WHERE L.BRANCH_ID = @BRANCH_ID
                  AND L.IS_ACTIVE = TRUE
                  AND L.INVENTORY_SCOPE IN ('FINISHED_GOODS', 'SALES_FLOOR')
                ORDER BY CASE L.INVENTORY_SCOPE WHEN 'FINISHED_GOODS' THEN 0 ELSE 1 END, L.NAME, L.ID
                """,
                new { COMPANY_ID = companyId.Value, BRANCH_ID = session.BranchId },
                cancellationToken: cancellationToken));

        return rows.Select(x => new WholesaleLocationOption(
            x.Id, x.Code, x.Name, x.InventoryScope, x.IsDefault)).ToList();
    }

    public async Task<WholesaleProductOption?> FindAvailableProductAsync(
        string productCode,
        long? sourceLocationId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var code = (productCode ?? string.Empty).Trim().ToUpperInvariant();
        if (code.Length == 0)
            return null;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return null;

        var row = await connection.QuerySingleOrDefaultAsync<WholesaleProductRow>(
            new CommandDefinition($"""
                SELECT FIRST 1 {ProductSelect}
                {ProductFrom}
                WHERE F.COMPANY_ID = @COMPANY_ID
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
                  AND (@HAS_LOCATION = 0 OR F.LOCATION_ID = @LOCATION_ID)
                  AND (UPPER(TRIM(F.CODE)) = @CODE
                       OR UPPER(TRIM(COALESCE(F.BARCODE_VALUE, ''))) = @CODE
                       OR UPPER(TRIM(COALESCE(F.QR_VALUE, ''))) = @CODE)
                ORDER BY F.ID
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    HAS_LOCATION = sourceLocationId is > 0 ? 1 : 0,
                    LOCATION_ID = sourceLocationId ?? 0L,
                    CODE = code
                },
                cancellationToken: cancellationToken));

        return row is null ? null : MapProduct(row);
    }

    public async Task<IReadOnlyList<WholesaleProductOption>> SearchAvailableProductsAsync(
        string searchText,
        long? sourceLocationId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var search = (searchText ?? string.Empty).Trim();
        if (search.Length == 0)
            return Array.Empty<WholesaleProductOption>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var companyId = await ResolveCompanyIdAsync(connection, session.BranchId, cancellationToken);
        if (companyId is null)
            return Array.Empty<WholesaleProductOption>();

        var rows = await connection.QueryAsync<WholesaleProductRow>(
            new CommandDefinition($"""
                SELECT FIRST {ProductSearchLimit} {ProductSelect}
                {ProductFrom}
                WHERE F.COMPANY_ID = @COMPANY_ID
                  AND F.BRANCH_ID = @BRANCH_ID
                  AND F.STATUS_CODE IN ('IN_STOCK', 'DISPLAYED')
                  AND (@HAS_LOCATION = 0 OR F.LOCATION_ID = @LOCATION_ID)
                  AND (F.CODE CONTAINING @SEARCH OR F.PRODUCT_NAME CONTAINING @SEARCH)
                ORDER BY F.CODE, F.ID
                """,
                new
                {
                    COMPANY_ID = companyId.Value,
                    BRANCH_ID = session.BranchId,
                    HAS_LOCATION = sourceLocationId is > 0 ? 1 : 0,
                    LOCATION_ID = sourceLocationId ?? 0L,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));

        return rows.Select(MapProduct).ToList();
    }

    public async Task<WholesaleSaleResult> PostAsync(
        WholesaleSaleDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (draft.FinishedProductIds is null || draft.Allocations is null)
            throw new InvalidOperationException("Dữ liệu bán sỉ không đầy đủ.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var identity = await ReloadIdentityAsync(connection, transaction, session, cancellationToken);
            var customer = await LoadCustomerAsync(
                connection, transaction, identity.CompanyId, draft.CustomerId, cancellationToken);
            await EnsureSalespersonAsync(
                connection, transaction, identity.CompanyId, session.BranchId, draft.SalespersonId, cancellationToken);
            await EnsureSourceLocationAsync(
                connection, transaction, identity.CompanyId, session.BranchId, draft.SourceLocationId, cancellationToken);

            var preparedLines = new List<PreparedWholesaleLine>(draft.FinishedProductIds.Count);
            foreach (var productId in draft.FinishedProductIds.Distinct())
            {
                var line = await LoadProductForPostAsync(
                    connection, transaction, identity.CompanyId, session.BranchId,
                    draft.SourceLocationId, productId, cancellationToken);
                preparedLines.Add(line);
            }

            var grandTotal = preparedLines.Sum(x => x.LineTotalVnd);
            var errors = WholesaleSaleValidator.Validate(
                draft, grandTotal, customer.IsWalkIn,
                identity.CanPostWholesale, identity.CanApproveDebt, identity.CanChangeSalesperson,
                session.UserId);
            if (errors.Count > 0)
                throw new InvalidOperationException(string.Join(" ", errors));

            var paid = draft.Allocations.Sum(x => x.AmountVnd);
            var orderCode = await StockPostingSupport.NextDocumentCodeAsync(
                connection, transaction, "WHOLESALE_SALE", session.BranchId, "BSI", cancellationToken);

            long orderId;
            await using (var insertOrder = new FbCommand("""
                INSERT INTO SALES_ORDERS
                    (COMPANY_ID, BRANCH_ID, CODE, ORDER_TYPE, CUSTOMER_ID, ORDER_DATE,
                     SALES_PERSON_ID, STATUS_CODE, SUBTOTAL, DISCOUNT_AMOUNT, TAX_AMOUNT,
                     GRAND_TOTAL, NOTES, CREATED_BY, UPDATED_BY)
                VALUES
                    (@CID, @BID, @CODE, @TYPE, @CUSTOMER, @DATE,
                     @SALESPERSON, 'COMPLETED', @SUBTOTAL, 0, 0,
                     @TOTAL, @NOTES, @BY, @BY)
                RETURNING ID
                """, connection, transaction))
            {
                insertOrder.Parameters.Add("@CID", FbDbType.BigInt).Value = identity.CompanyId;
                insertOrder.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertOrder.Parameters.Add("@CODE", FbDbType.VarChar, 50).Value = orderCode;
                insertOrder.Parameters.Add("@TYPE", FbDbType.VarChar, 30).Value = SalesOrderTypes.Wholesale;
                insertOrder.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = draft.CustomerId;
                insertOrder.Parameters.Add("@DATE", FbDbType.TimeStamp).Value = draft.DocumentDate;
                insertOrder.Parameters.Add("@SALESPERSON", FbDbType.BigInt).Value = draft.SalespersonId;
                insertOrder.Parameters.Add("@SUBTOTAL", FbDbType.Decimal).Value = grandTotal;
                insertOrder.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = grandTotal;
                insertOrder.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
                    string.IsNullOrWhiteSpace(draft.Notes) ? DBNull.Value : draft.Notes.Trim();
                insertOrder.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
                orderId = Convert.ToInt64(await insertOrder.ExecuteScalarAsync(cancellationToken));
            }

            var lineNumber = 0;
            foreach (var line in preparedLines)
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
                         FALSE, NULL)
                    """, connection, transaction);
                insertLine.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
                insertLine.Parameters.Add("@LINE", FbDbType.Integer).Value = lineNumber;
                insertLine.Parameters.Add("@NAME", FbDbType.VarChar, 255).Value = line.ProductName;
                insertLine.Parameters.Add("@PURITY", FbDbType.VarChar, 30).Value = line.GoldPurity;
                insertLine.Parameters.Add("@WEIGHT", FbDbType.Decimal).Value = line.GoldWeightChi;
                insertLine.Parameters.Add("@GOLD_PRICE", FbDbType.Decimal).Value = line.SellPricePerChi;
                insertLine.Parameters.Add("@LABOR", FbDbType.Decimal).Value = line.LaborCost;
                insertLine.Parameters.Add("@STONE", FbDbType.Decimal).Value = line.NonGoldMaterialCost;
                insertLine.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = line.LineTotalVnd;
                insertLine.Parameters.Add("@PRODUCT", FbDbType.BigInt).Value = line.FinishedProductId;
                insertLine.Parameters.Add("@SOURCE_GOLD", FbDbType.Decimal).Value = line.SellPricePerChi;
                insertLine.Parameters.Add("@APPLIED_GOLD", FbDbType.Decimal).Value = line.SellPricePerChi;
                insertLine.Parameters.Add("@SOURCE_LABOR", FbDbType.Decimal).Value = line.LaborCost;
                insertLine.Parameters.Add("@APPLIED_LABOR", FbDbType.Decimal).Value = line.LaborCost;
                insertLine.Parameters.Add("@SOURCE_STONE", FbDbType.Decimal).Value = line.NonGoldMaterialCost;
                insertLine.Parameters.Add("@APPLIED_STONE", FbDbType.Decimal).Value = line.NonGoldMaterialCost;
                await insertLine.ExecuteNonQueryAsync(cancellationToken);
            }

            var issueDocumentId = await FinishedGoodsPostingSupport.PostWholesaleIssueAsync(
                connection, transaction, identity.CompanyId, session, orderId,
                draft.SourceLocationId, draft.DocumentDate,
                preparedLines.Select(x => x.FinishedProductId).ToArray(),
                cancellationToken);

            if (paid > 0)
            {
                var settlement = await _settlements.PostInTransactionAsync(
                    connection, transaction, identity.CompanyId, session.BranchId,
                    new FinancialSettlementDraft(
                        SettlementTypes.Receipt,
                        SettlementPartyTypes.Customer,
                        draft.CustomerId,
                        draft.DocumentDate.Date,
                        draft.Allocations,
                        [new SettlementApplicationDraft(
                            SettlementApplicationTargetTypes.SalesOrder, orderId, paid)],
                        string.IsNullOrWhiteSpace(draft.Notes)
                            ? $"Thu bán sỉ {orderCode}"
                            : draft.Notes.Trim()),
                    session.UserId,
                    cancellationToken);

                await InsertOrderPaymentsAsync(
                    connection, transaction, orderId, draft.DocumentDate, settlement,
                    orderCode, $"Thu bán sỉ {orderCode}", session, cancellationToken);
            }

            if (draft.ReceivableAmountVnd > 0)
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
                insertDebt.Parameters.Add("@CID", FbDbType.BigInt).Value = identity.CompanyId;
                insertDebt.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insertDebt.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = debtCode;
                insertDebt.Parameters.Add("@CUSTOMER", FbDbType.BigInt).Value = draft.CustomerId;
                insertDebt.Parameters.Add("@ORDER_ID", FbDbType.BigInt).Value = orderId;
                insertDebt.Parameters.Add("@AMOUNT", FbDbType.Decimal).Value = draft.ReceivableAmountVnd;
                insertDebt.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value = $"Công nợ bán sỉ {orderCode}";
                insertDebt.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
                await insertDebt.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, transaction, "WHOLESALE_SALE_POSTED",
                session.UserId, session.Username, session.BranchId,
                "SALES_ORDER", orderId.ToString(),
                $"{{\"code\":\"{orderCode}\",\"issueDocumentId\":{issueDocumentId}," +
                $"\"grandTotalVnd\":{grandTotal.ToString(CultureInfo.InvariantCulture)}," +
                $"\"paidAmountVnd\":{paid.ToString(CultureInfo.InvariantCulture)}," +
                $"\"receivableAmountVnd\":{draft.ReceivableAmountVnd.ToString(CultureInfo.InvariantCulture)}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new WholesaleSaleResult(
                orderId, orderCode, issueDocumentId, grandTotal, paid, draft.ReceivableAmountVnd);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static WholesaleProductOption MapProduct(WholesaleProductRow row)
    {
        var goldWeight = row.GoldWeightChi > 0 ? row.GoldWeightChi : row.TotalWeightChi;
        var lineTotal = WarehouseIssuePricing.CalculateFinishedProductIssueValue(
            goldWeight, row.SellPricePerChi, row.LaborCost, row.NonGoldMaterialCost);
        return new WholesaleProductOption(
            row.FinishedProductId, row.ProductCode, row.ProductName,
            row.SourceLocationId, row.LocationName, row.GoldPurity, row.RowVersion,
            row.TotalWeightChi, row.StoneWeightChi, goldWeight,
            row.SellPricePerChi, row.LaborCost, row.NonGoldMaterialCost, lineTotal);
    }

    private static async Task<PreparedWholesaleLine> LoadProductForPostAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long sourceLocationId,
        long finishedProductId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT F.CODE, F.PRODUCT_NAME, COALESCE(F.GOLD_PURITY, ''),
                   F.GOLD_WEIGHT, F.TOTAL_WEIGHT, F.LABOR_COST, F.NON_GOLD_MATERIAL_COST,
                   F.STATUS_CODE, F.LOCATION_ID, F.GOLD_TYPE_ID, F.ROW_VERSION
            FROM FINISHED_PRODUCTS F
            WHERE F.ID = @ID AND F.COMPANY_ID = @CID AND F.BRANCH_ID = @BID
            WITH LOCK
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = finishedProductId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException($"Không tìm thấy thành phẩm #{finishedProductId}.");

        var code = reader.GetString(0);
        var name = reader.GetString(1);
        var purity = reader.GetString(2);
        var goldWeight = reader.GetDecimal(3) > 0 ? reader.GetDecimal(3) : reader.GetDecimal(4);
        var labor = reader.GetDecimal(5);
        var nonGold = reader.GetDecimal(6);
        var status = reader.GetString(7);
        var locationId = reader.IsDBNull(8) ? (long?)null : reader.GetInt64(8);
        var goldTypeId = reader.IsDBNull(9) ? (long?)null : reader.GetInt64(9);

        if (status is not ("IN_STOCK" or "DISPLAYED"))
            throw new InvalidOperationException($"Thành phẩm '{code}' không ở trạng thái có thể bán sỉ.");
        if (locationId != sourceLocationId)
            throw new InvalidOperationException($"Thành phẩm '{code}' không còn ở kho xuất đã chọn.");

        var price = await FirebirdWarehouseGoldPricingSupport.ResolveAsync(
            connection, transaction, companyId, goldTypeId, purity, cancellationToken);
        var sell = FirebirdWarehouseGoldPricingSupport.EnsurePositiveSell(price).SellPricePerChi;
        var total = WarehouseIssuePricing.CalculateFinishedProductIssueValue(
            goldWeight, sell, labor, nonGold);
        return new PreparedWholesaleLine(
            finishedProductId, code, name, purity, goldWeight, sell, labor, nonGold, total);
    }

    private static async Task<IdentityContext> ReloadIdentityAsync(
        FbConnection connection,
        FbTransaction transaction,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT U.COMPANY_ID, R.CODE,
                   MAX(CASE WHEN P.CODE = @WHOLESALE AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @DEBT AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END),
                   MAX(CASE WHEN P.CODE = @SALESPERSON AND RP.IS_ALLOWED = TRUE THEN 1 ELSE 0 END)
            FROM USERS U
            JOIN ROLES R ON R.ID = U.ROLE_ID AND R.IS_ACTIVE = TRUE
            JOIN BRANCHES B ON B.ID = @BID AND B.COMPANY_ID = U.COMPANY_ID AND B.IS_ACTIVE = TRUE
            LEFT JOIN ROLE_PERMISSIONS RP ON RP.ROLE_ID = R.ID
            LEFT JOIN PERMISSIONS P ON P.ID = RP.PERMISSION_ID
            WHERE U.ID = @UID AND U.IS_ACTIVE = TRUE
              AND (U.DEFAULT_BRANCH_ID = @BID OR EXISTS (
                  SELECT 1 FROM USER_SCOPES S WHERE S.USER_ID = U.ID
                    AND S.SCOPE_TYPE = 'BRANCH' AND S.SCOPE_ID = @BID AND S.CAN_ACCESS = TRUE))
            GROUP BY U.COMPANY_ID, R.CODE
            """, connection, transaction);
        command.Parameters.Add("@UID", FbDbType.BigInt).Value = session.UserId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@WHOLESALE", FbDbType.VarChar, 80).Value = SalesPermissions.WholesalePost;
        command.Parameters.Add("@DEBT", FbDbType.VarChar, 80).Value = SalesPermissions.DebtApprove;
        command.Parameters.Add("@SALESPERSON", FbDbType.VarChar, 80).Value = SalesPermissions.SalespersonChange;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Phiên người dùng không còn hợp lệ cho chi nhánh hiện tại.");

        var isAdmin = string.Equals(reader.GetString(1).Trim(), "ADMIN", StringComparison.OrdinalIgnoreCase);
        var companyId = reader.GetInt64(0);
        var legacy = new SalesPermissionSnapshot(false, false, isAdmin || Allowed(reader, 3),
            isAdmin || Allowed(reader, 4), false, false, isAdmin || Allowed(reader, 2));
        await reader.CloseAsync();
        var rights = await GroupSalesPermissions.RestrictAsync(connection, transaction, session.UserId, legacy, cancellationToken);
        return new IdentityContext(companyId, rights.CanPostWholesale, rights.CanApproveDebt, rights.CanChangeSalesperson);
    }

    private static async Task<(long Id, bool IsWalkIn)> LoadCustomerAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long customerId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT ID, IS_WALK_IN, IS_ACTIVE
            FROM CUSTOMERS
            WHERE ID = @ID AND COMPANY_ID = @CID
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = customerId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Không tìm thấy khách hàng.");
        if (reader.IsDBNull(2) || !reader.GetBoolean(2))
            throw new InvalidOperationException("Khách hàng không còn hoạt động.");
        var walkIn = reader.GetBoolean(1);
        return (reader.GetInt64(0), walkIn);
    }

    private static async Task EnsureSalespersonAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long salespersonId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM USERS U
            WHERE U.ID = @ID AND U.COMPANY_ID = @CID AND U.IS_ACTIVE = TRUE
              AND (U.DEFAULT_BRANCH_ID = @BID OR EXISTS (
                  SELECT 1 FROM USER_SCOPES S WHERE S.USER_ID = U.ID
                    AND S.SCOPE_TYPE = 'BRANCH' AND S.SCOPE_ID = @BID AND S.CAN_ACCESS = TRUE))
            """, connection, transaction);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = salespersonId;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Nhân viên bán hàng không hợp lệ.");
    }

    private static async Task EnsureSourceLocationAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        long locationId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*)
            FROM LOCATIONS L
            JOIN BRANCHES B ON B.ID = L.BRANCH_ID AND B.COMPANY_ID = @CID
            WHERE L.ID = @ID AND L.BRANCH_ID = @BID AND L.IS_ACTIVE = TRUE
              AND L.INVENTORY_SCOPE IN ('FINISHED_GOODS', 'SALES_FLOOR')
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = locationId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 1)
            throw new InvalidOperationException("Kho xuất bán sỉ không hợp lệ.");
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
            insert.Parameters.Add("@BY", FbDbType.BigInt).Value = session.UserId;
            insert.Parameters.Add("@SETTLEMENT", FbDbType.BigInt).Value = settlement.SettlementId;
            insert.Parameters.Add("@ALLOCATION", FbDbType.BigInt).Value = row.Id;
            insert.Parameters.Add("@VOUCHER", FbDbType.BigInt).Value = row.VoucherId;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<long?> ResolveCompanyIdAsync(
        FbConnection connection,
        long branchId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand(
            "SELECT COMPANY_ID FROM BRANCHES WHERE ID = @ID AND IS_ACTIVE = TRUE",
            connection);
        command.Parameters.Add("@ID", FbDbType.BigInt).Value = branchId;
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? null : Convert.ToInt64(value);
    }

    private static bool Allowed(FbDataReader reader, int ordinal) =>
        !reader.IsDBNull(ordinal) && Convert.ToInt32(reader.GetValue(ordinal)) == 1;

    private sealed record IdentityContext(
        long CompanyId,
        bool CanPostWholesale,
        bool CanApproveDebt,
        bool CanChangeSalesperson);

    private sealed record PreparedWholesaleLine(
        long FinishedProductId,
        string ProductCode,
        string ProductName,
        string GoldPurity,
        decimal GoldWeightChi,
        decimal SellPricePerChi,
        decimal LaborCost,
        decimal NonGoldMaterialCost,
        decimal LineTotalVnd);

    private sealed class WholesaleLocationRow
    {
        public long Id { get; init; }
        public string Code { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string InventoryScope { get; init; } = string.Empty;
        public bool IsDefault { get; init; }
    }

    private sealed class WholesaleProductRow
    {
        public long FinishedProductId { get; init; }
        public string ProductCode { get; init; } = string.Empty;
        public string ProductName { get; init; } = string.Empty;
        public long SourceLocationId { get; init; }
        public string LocationName { get; init; } = string.Empty;
        public string GoldPurity { get; init; } = string.Empty;
        public long RowVersion { get; init; }
        public decimal TotalWeightChi { get; init; }
        public decimal StoneWeightChi { get; init; }
        public decimal GoldWeightChi { get; init; }
        public decimal SellPricePerChi { get; init; }
        public decimal LaborCost { get; init; }
        public decimal NonGoldMaterialCost { get; init; }
    }
}
