using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

// Dapper migration: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record đích (vd. C.ID AS CustomerId).
// Các write-path transaction phức tạp vẫn dùng chung một FbTransaction (fbTx)
// cho cả Dapper lẫn các service ADO.NET cũ (FinancialSettlementService, AuditLogWriter).
public sealed class FirebirdDebtWorkbenchRepository : IDebtWorkbenchRepository
{
    private const int OverdueDays = 30;

    private readonly FirebirdFinancialSettlementService _settlements = new();

    public async Task<DebtWorkbenchSummary> GetSummaryAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var customers = await SearchCustomerDebtsAsync(
            new CustomerDebtSearchFilter(string.Empty, 0, string.Empty, null, null),
            session,
            cancellationToken);
        var suppliers = await SearchSupplierDebtsAsync(
            new SupplierDebtSearchFilter(string.Empty, string.Empty, null, null),
            session,
            cancellationToken);

        var customerRemaining = customers.Sum(x => x.RemainingVnd);
        var customerPaid = customers.Sum(x => x.PaidVnd);
        var customerTotal = customers.Sum(x => x.TotalIncurredVnd);
        var customerOverdue = customers.Where(x => x.StatusCode == DebtDisplayStatuses.Overdue).ToList();

        var supplierRemaining = suppliers.Sum(x => x.RemainingVnd);
        var supplierPaid = suppliers.Sum(x => x.PaidVnd);
        var supplierTotal = suppliers.Sum(x => x.TotalIncurredVnd);
        var supplierOverdue = suppliers.Where(x => x.StatusCode == DebtDisplayStatuses.Overdue).ToList();

        return new DebtWorkbenchSummary(
            customerTotal,
            customerPaid,
            customerRemaining,
            supplierTotal,
            supplierPaid,
            supplierRemaining,
            customerOverdue.Sum(x => x.RemainingVnd),
            customerOverdue.Count,
            supplierOverdue.Sum(x => x.RemainingVnd),
            supplierOverdue.Count);
    }

    public async Task<IReadOnlyList<CustomerDebtRow>> SearchCustomerDebtsAsync(
        CustomerDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var rows = await connection.QueryAsync<CustomerDebtSearchRow>(
            new CommandDefinition("""
                SELECT C.ID AS CustomerId, C.CODE AS Code, C.NAME AS Name,
                       COALESCE(C.PHONE, '') AS Phone, G.NAME AS GroupName, COALESCE(C.ADDRESS, '') AS Address,
                       COALESCE((SELECT SUM(O.GRAND_TOTAL) FROM SALES_ORDERS O
                                 WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                                   AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
                                   AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE)), 0) AS TotalIncurredVnd,
                       COALESCE((SELECT SUM(COALESCE((
                                    SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                                    FROM ORDER_PAYMENTS P
                                    WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                                    + COALESCE((
                                    SELECT SUM(X.AMOUNT_VND)
                                    FROM SALES_CHECKOUT_OFFSETS X
                                    WHERE X.SALES_ORDER_ID = O.ID), 0))
                                 FROM SALES_ORDERS O
                                 WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                                   AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
                                   AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE)), 0) AS PaidVnd,
                       (SELECT MAX(O.ORDER_DATE) FROM SALES_ORDERS O
                        WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                          AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
                          AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE)) AS LastDebtDate
                FROM CUSTOMERS C
                JOIN CUSTOMER_GROUPS G ON G.ID = C.CUSTOMER_GROUP_ID
                WHERE C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND C.IS_ACTIVE = TRUE
                  AND (@GROUP_ID = 0 OR C.CUSTOMER_GROUP_ID = @GROUP_ID)
                  AND (@HAS_SEARCH = 0
                       OR CAST(C.CODE AS VARCHAR(40) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR C.NAME CONTAINING @SEARCH
                       OR COALESCE(C.PHONE, '') CONTAINING @SEARCH)
                ORDER BY C.NAME, C.CODE
                """,
                new
                {
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search,
                    GROUP_ID = filter.GroupId,
                    HAS_FROM = filter.FromDate.HasValue ? 1 : 0,
                    FROM_DATE = filter.FromDate ?? DateTime.Today,
                    HAS_TO = filter.ToDate.HasValue ? 1 : 0,
                    TO_DATE = filter.ToDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.Today.AddDays(1).AddTicks(-1)
                },
                cancellationToken: cancellationToken));

        var result = new List<CustomerDebtRow>();
        foreach (var row in rows)
        {
            if (row.TotalIncurredVnd <= 0 && row.PaidVnd <= 0)
                continue;
            var remaining = row.TotalIncurredVnd - row.PaidVnd;
            if (remaining < 0) remaining = 0;
            var status = ResolveStatus(remaining, row.LastDebtDate);
            if (!MatchesStatusFilter(filter.StatusCode, status))
                continue;

            result.Add(new CustomerDebtRow(
                row.CustomerId,
                row.Code,
                row.Name,
                row.Phone,
                row.GroupName,
                row.Address,
                row.TotalIncurredVnd,
                row.PaidVnd,
                remaining,
                row.LastDebtDate,
                status));
        }

        return result;
    }

    public async Task<IReadOnlyList<SupplierDebtRow>> SearchSupplierDebtsAsync(
        SupplierDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var rows = await connection.QueryAsync<SupplierDebtSearchRow>(
            new CommandDefinition("""
                SELECT S.ID AS SupplierId, S.CODE AS Code, S.NAME AS Name,
                       COALESCE(S.PHONE, '') AS Phone, COALESCE(G.NAME, '') AS GroupName,
                       COALESCE(S.ADDRESS, '') AS Address,
                       COALESCE(SUM(D.ORIGINAL_AMOUNT_VND), 0) AS TotalIncurredVnd,
                       COALESCE(SUM(D.PAID_AMOUNT_VND), 0) AS PaidVnd,
                       COALESCE(SUM(D.REMAINING_AMOUNT_VND), 0) AS RemainingVnd,
                       MAX(D.CREATED_AT) AS LastDebtDate
                FROM SUPPLIERS S
                LEFT JOIN SUPPLIER_GROUPS G ON G.ID = S.SUPPLIER_GROUP_ID
                LEFT JOIN PAYABLE_DEBTS D ON D.SUPPLIER_ID = S.ID
                     AND D.PARTY_TYPE = 'SUPPLIER'
                     AND D.STATUS_CODE <> 'CANCELLED'
                     AND D.BRANCH_ID = @BID
                     AND (@HAS_FROM = 0 OR D.CREATED_AT >= @FROM_DATE)
                     AND (@HAS_TO = 0 OR D.CREATED_AT <= @TO_DATE)
                WHERE S.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND S.IS_ACTIVE = TRUE
                  AND (@HAS_SEARCH = 0
                       OR CAST(S.CODE AS VARCHAR(40) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR S.NAME CONTAINING @SEARCH
                       OR COALESCE(S.PHONE, '') CONTAINING @SEARCH)
                GROUP BY S.ID, S.CODE, S.NAME, S.PHONE, G.NAME, S.ADDRESS
                HAVING COALESCE(SUM(D.ORIGINAL_AMOUNT_VND), 0) > 0
                    OR COALESCE(SUM(D.REMAINING_AMOUNT_VND), 0) > 0
                ORDER BY S.NAME, S.CODE
                """,
                new
                {
                    BID = session.BranchId,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search,
                    HAS_FROM = filter.FromDate.HasValue ? 1 : 0,
                    FROM_DATE = filter.FromDate?.Date ?? DateTime.Today,
                    HAS_TO = filter.ToDate.HasValue ? 1 : 0,
                    // Inclusive end-of-day so debts created "hôm nay" vẫn hiện khi lọc đến ngày hôm nay.
                    TO_DATE = filter.ToDate?.Date.AddDays(1).AddTicks(-1) ?? DateTime.Today.AddDays(1).AddTicks(-1)
                },
                cancellationToken: cancellationToken));

        var result = new List<SupplierDebtRow>();
        foreach (var row in rows)
        {
            var status = ResolveStatus(row.RemainingVnd, row.LastDebtDate);
            if (!MatchesStatusFilter(filter.StatusCode, status))
                continue;

            result.Add(new SupplierDebtRow(
                row.SupplierId,
                row.Code,
                row.Name,
                row.Phone,
                row.GroupName,
                row.Address,
                row.TotalIncurredVnd,
                row.PaidVnd,
                row.RemainingVnd,
                row.LastDebtDate,
                status));
        }

        return result;
    }

    public async Task<IReadOnlyList<DebtLedgerEntry>> GetCustomerLedgerAsync(
        long customerId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var entries = new List<(DateTime Date, string Code, string Desc, decimal Receipt, decimal Payment)>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var orderRows = await connection.QueryAsync<CustomerOrderLedgerRow>(
            new CommandDefinition("""
                SELECT O.ORDER_DATE AS OrderDate, O.CODE AS Code, O.GRAND_TOTAL AS GrandTotal, O.STATUS_CODE AS StatusCode
                FROM SALES_ORDERS O
                WHERE O.CUSTOMER_ID = @CID
                  AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                ORDER BY O.ORDER_DATE, O.ID
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken));
        foreach (var row in orderRows)
        {
            entries.Add((
                row.OrderDate,
                row.Code,
                "Đơn hàng " + row.StatusCode,
                0m,
                row.GrandTotal));
        }

        var paymentRows = await connection.QueryAsync<CustomerPaymentLedgerRow>(
            new CommandDefinition("""
                SELECT P.PAYMENT_DATE AS PaymentDate, O.CODE AS OrderCode, P.PAYMENT_TYPE AS PaymentType,
                       P.AMOUNT AS Amount, P.PAYMENT_METHOD AS PaymentMethod
                FROM ORDER_PAYMENTS P
                JOIN SALES_ORDERS O ON O.ID = P.ORDER_ID
                WHERE O.CUSTOMER_ID = @CID AND P.IS_VOIDED = FALSE
                ORDER BY P.PAYMENT_DATE, P.ID
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken));
        foreach (var row in paymentRows)
        {
            var isRefund = string.Equals(row.PaymentType, "REFUND", StringComparison.OrdinalIgnoreCase);
            entries.Add((
                row.PaymentDate,
                row.OrderCode,
                row.PaymentType + " / " + row.PaymentMethod,
                isRefund ? 0m : row.Amount,
                isRefund ? row.Amount : 0m));
        }

        var offsetRows = await connection.QueryAsync<CustomerOffsetLedgerRow>(
            new CommandDefinition("""
                SELECT C.DOCUMENT_DATE AS DocumentDate, C.CODE AS Code, O.AMOUNT_VND AS AmountVnd
                FROM SALES_CHECKOUT_OFFSETS O
                JOIN SALES_CHECKOUTS C ON C.ID = O.CHECKOUT_ID
                JOIN SALES_ORDERS SO ON SO.ID = O.SALES_ORDER_ID
                WHERE SO.CUSTOMER_ID = @CID
                ORDER BY C.DOCUMENT_DATE, C.ID
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken));
        foreach (var row in offsetRows)
        {
            entries.Add((
                row.DocumentDate,
                row.Code,
                "Bù trừ vàng cũ",
                row.AmountVnd,
                0m));
        }

        decimal balance = 0;
        var result = new List<DebtLedgerEntry>();
        foreach (var entry in entries.OrderBy(x => x.Date).ThenBy(x => x.Code))
        {
            balance += entry.Payment - entry.Receipt;
            result.Add(new DebtLedgerEntry(
                entry.Date,
                entry.Code,
                entry.Desc,
                entry.Receipt,
                entry.Payment,
                balance));
        }

        return result;
    }

    public async Task<IReadOnlyList<DebtLedgerEntry>> GetSupplierLedgerAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var entries = new List<(DateTime Date, string Code, string Desc, decimal Receipt, decimal Payment)>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var debtRows = await connection.QueryAsync<SupplierDebtLedgerRow>(
            new CommandDefinition("""
                SELECT D.CREATED_AT AS CreatedAt,
                       COALESCE(SD.CODE, D.CODE) AS DocCode,
                       D.DEBT_KIND AS DebtKind,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd,
                       COALESCE(SD.CODE, '') AS StockCode,
                       COALESCE(SD.SOURCE_TYPE, '') AS SourceType,
                       D.CODE AS DebtCode
                FROM PAYABLE_DEBTS D
                LEFT JOIN STOCK_DOCUMENTS SD
                  ON D.ORIGIN_DOCUMENT_TYPE = 'STOCK_DOCUMENT'
                 AND SD.ID = D.ORIGIN_DOCUMENT_ID
                WHERE D.SUPPLIER_ID = @SID
                  AND D.PARTY_TYPE = 'SUPPLIER'
                  AND D.STATUS_CODE <> 'CANCELLED'
                  AND D.BRANCH_ID = @BID
                ORDER BY D.CREATED_AT, D.ID
                """,
                new { SID = supplierId, BID = session.BranchId },
                cancellationToken: cancellationToken));
        foreach (var row in debtRows)
        {
            var displayCode = string.IsNullOrWhiteSpace(row.StockCode) ? row.DebtCode : row.StockCode;
            var desc = BuildSupplierDebtDescription(row.DebtKind, row.StockCode, row.DebtCode, row.SourceType);
            entries.Add((
                row.CreatedAt,
                displayCode,
                desc,
                0m,
                row.OriginalAmountVnd));
        }

        var paymentRows = await connection.QueryAsync<SupplierPaymentLedgerRow>(
            new CommandDefinition("""
                SELECT P.PAYMENT_DATE AS PaymentDate, D.CODE AS DebtCode,
                       P.AMOUNT_VND AS AmountVnd, COALESCE(P.NOTES, '') AS Notes
                FROM PAYABLE_DEBT_PAYMENTS P
                JOIN PAYABLE_DEBTS D ON D.ID = P.DEBT_ID
                WHERE D.SUPPLIER_ID = @SID
                  AND D.PARTY_TYPE = 'SUPPLIER'
                  AND D.BRANCH_ID = @BID
                ORDER BY P.PAYMENT_DATE, P.ID
                """,
                new { SID = supplierId, BID = session.BranchId },
                cancellationToken: cancellationToken));
        foreach (var row in paymentRows)
        {
            entries.Add((
                row.PaymentDate,
                row.DebtCode,
                string.IsNullOrWhiteSpace(row.Notes) ? "Thanh toán công nợ" : row.Notes,
                row.AmountVnd,
                0m));
        }

        decimal balance = 0;
        var result = new List<DebtLedgerEntry>();
        foreach (var entry in entries.OrderBy(x => x.Date).ThenBy(x => x.Code))
        {
            balance += entry.Payment - entry.Receipt;
            result.Add(new DebtLedgerEntry(
                entry.Date,
                entry.Code,
                entry.Desc,
                entry.Receipt,
                entry.Payment,
                balance));
        }

        return result;
    }

    public async Task<IReadOnlyList<PayableDebtListItem>> GetSupplierOpenPayablesAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PayableDebtListItem>(
            new CommandDefinition("""
                SELECT D.ID AS Id, D.CODE AS Code, D.PARTY_TYPE AS PartyType, COALESCE(S.NAME, '') AS PartyName,
                       D.DEBT_KIND AS DebtKind, D.ORIGIN_DOCUMENT_TYPE AS OriginDocumentType,
                       D.ORIGIN_DOCUMENT_ID AS OriginDocumentId,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd, D.PAID_AMOUNT_VND AS PaidAmountVnd,
                       D.REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       D.STATUS_CODE AS StatusCode, D.CREATED_AT AS CreatedAt
                FROM PAYABLE_DEBTS D
                LEFT JOIN SUPPLIERS S ON S.ID = D.SUPPLIER_ID
                WHERE D.SUPPLIER_ID = @SID
                  AND D.PARTY_TYPE = 'SUPPLIER'
                  AND D.STATUS_CODE IN ('OPEN', 'PARTIAL')
                  AND D.BRANCH_ID = @BID
                ORDER BY D.CREATED_AT, D.ID
                """,
                new { SID = supplierId, BID = session.BranchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task CollectCustomerPaymentAsync(
        CustomerCollectPaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var allocations = request.Allocations ?? Array.Empty<PaymentAllocationDraft>();
        var amount = allocations.Sum(x => x.AmountVnd);
        var errors = PaymentAllocationValidator.Validate(allocations, amount);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        if (amount <= 0)
            throw new InvalidOperationException("Số tiền thu phải > 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var openOrderRows = await connection.QueryAsync<OpenOrderRow>(
                new CommandDefinition("""
                    SELECT O.ID AS Id,
                           O.GRAND_TOTAL - COALESCE((
                               SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                               FROM ORDER_PAYMENTS P
                               WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0) AS Remaining
                    FROM SALES_ORDERS O
                    WHERE O.CUSTOMER_ID = @CID
                      AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                    ORDER BY O.ORDER_DATE, O.ID
                    WITH LOCK
                    """,
                    new { CID = request.CustomerId },
                    fbTx,
                    cancellationToken: cancellationToken));
            var openOrders = new List<(long OrderId, decimal Remaining)>();
            foreach (var row in openOrderRows)
            {
                if (row.Remaining > 0)
                    openOrders.Add((row.Id, row.Remaining));
            }

            var totalOpen = openOrders.Sum(x => x.Remaining);
            if (amount > totalOpen)
                throw new InvalidOperationException($"Số thu ({amount:N0}) vượt tổng còn nợ ({totalOpen:N0}).");

            var applications = new List<SettlementApplicationDraft>();
            var left = amount;
            foreach (var order in openOrders)
            {
                if (left <= 0)
                    break;
                var apply = Math.Min(left, order.Remaining);
                applications.Add(new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.SalesOrder, order.OrderId, apply));
                left -= apply;
            }

            var settlement = await _settlements.PostInTransactionAsync(
                connection, fbTx, companyId, session.BranchId,
                new FinancialSettlementDraft(
                    SettlementTypes.Receipt,
                    SettlementPartyTypes.Customer,
                    request.CustomerId,
                    DateTime.Now,
                    allocations,
                    applications,
                    request.Notes),
                session.UserId,
                cancellationToken);

            var orderMethod = ResolveOrderPaymentMethod(allocations);
            foreach (var application in applications)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        INSERT INTO ORDER_PAYMENTS
                            (ORDER_ID, PAYMENT_DATE, PAYMENT_TYPE, AMOUNT, PAYMENT_METHOD,
                             REFERENCE_NO, NOTES, IS_VOIDED, CREATED_BY, CREATED_AT)
                        VALUES
                            (@OID, CURRENT_TIMESTAMP, 'PAYMENT', @AMT, @METHOD,
                             @REF, @NOTES, FALSE, @BY, CURRENT_TIMESTAMP)
                        """,
                        new
                        {
                            OID = application.TargetId,
                            AMT = application.AmountVnd,
                            METHOD = orderMethod,
                            REF = settlement.Code,
                            NOTES = (object?)request.Notes ?? DBNull.Value,
                            BY = session.UserId > 0 ? (object)session.UserId : DBNull.Value
                        },
                        fbTx,
                        cancellationToken: cancellationToken));
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "CUSTOMER_DEBT_COLLECTED",
                session.UserId, session.Username, session.BranchId,
                "CUSTOMER", request.CustomerId.ToString(),
                $"{{\"settlementId\":{settlement.SettlementId},\"amount\":{amount},\"vouchers\":{settlement.CashVoucherIds.Count},\"orders\":{applications.Count}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public Task PaySupplierFifoAsync(
        long supplierId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        PayPayablePartyFifoAsync(
            PayablePartyTypes.Supplier, supplierId, allocations, notes, session, cancellationToken);

    public async Task PayPayablePartyFifoAsync(
        string partyType,
        long partyId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var normalizedParty = (partyType ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedParty is not (
            PayablePartyTypes.Supplier or
            PayablePartyTypes.Customer or
            PayablePartyTypes.ExternalWorker))
            throw new InvalidOperationException("PARTY_TYPE không hợp lệ.");

        var amount = allocations.Sum(x => x.AmountVnd);
        var errors = PaymentAllocationValidator.Validate(allocations, amount);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        if (amount <= 0)
            throw new InvalidOperationException("Số tiền chi phải > 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var open = await LoadOpenPartyDebtsForUpdateAsync(
                connection, fbTx, normalizedParty, partyId, session.BranchId, cancellationToken);
            var totalOpen = open.Sum(x => x.Remaining);
            if (amount > totalOpen)
                throw new InvalidOperationException($"Số chi ({amount:N0}) vượt tổng còn nợ ({totalOpen:N0}).");

            var applications = new List<SettlementApplicationDraft>();
            var left = amount;
            foreach (var debt in open)
            {
                if (left <= 0)
                    break;
                var apply = Math.Min(left, debt.Remaining);
                applications.Add(new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.PayableDebt, debt.Id, apply));
                left -= apply;
            }

            var settlement = await _settlements.PostInTransactionAsync(
                connection, fbTx, companyId, session.BranchId,
                new FinancialSettlementDraft(
                    SettlementTypes.Payment,
                    normalizedParty,
                    partyId,
                    DateTime.Now,
                    allocations,
                    applications,
                    notes),
                session.UserId,
                cancellationToken);

            foreach (var application in applications)
            {
                await ApplyPayableDebtPaymentAsync(
                    connection, fbTx, application.TargetId, application.AmountVnd,
                    notes, settlement.Code, session.UserId, cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PAYABLE_PARTY_FIFO_PAID",
                session.UserId, session.Username, session.BranchId,
                normalizedParty, partyId.ToString(),
                $"{{\"settlementId\":{settlement.SettlementId},\"amount\":{amount},\"vouchers\":{settlement.CashVoucherIds.Count},\"debts\":{applications.Count}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task ApplyPayableDebtPaymentAsync(
        FbConnection connection,
        FbTransaction transaction,
        long debtId,
        decimal paid,
        string? notes,
        string settlementCode,
        long userId,
        CancellationToken cancellationToken)
    {
        var debt = await connection.QuerySingleOrDefaultAsync<PayableDebtLockRow>(
            new CommandDefinition("""
                SELECT REMAINING_AMOUNT_VND AS RemainingAmountVnd, STATUS_CODE AS StatusCode
                FROM PAYABLE_DEBTS
                WHERE ID = @ID
                WITH LOCK
                """,
                new { ID = debtId },
                transaction,
                cancellationToken: cancellationToken));
        if (debt is null)
            throw new InvalidOperationException($"Không tìm thấy công nợ #{debtId}.");

        if (debt.StatusCode is PayableDebtStatuses.Paid or PayableDebtStatuses.Cancelled)
            throw new InvalidOperationException($"Công nợ #{debtId} đã đóng ({debt.StatusCode}).");
        if (paid > debt.RemainingAmountVnd)
            throw new InvalidOperationException($"Số thanh toán vượt số còn nợ của #{debtId}.");

        await connection.ExecuteAsync(
            new CommandDefinition("""
                INSERT INTO PAYABLE_DEBT_PAYMENTS (DEBT_ID, PAYMENT_DATE, AMOUNT_VND, NOTES, CREATED_BY, CREATED_AT)
                VALUES (@DEBT, CURRENT_TIMESTAMP, @AMT, @NOTES, @BY, CURRENT_TIMESTAMP)
                """,
                new
                {
                    DEBT = debtId,
                    AMT = paid,
                    NOTES = string.IsNullOrWhiteSpace(notes) ? settlementCode : $"{settlementCode}: {notes}",
                    BY = userId > 0 ? (object)userId : DBNull.Value
                },
                transaction,
                cancellationToken: cancellationToken));

        var newRemaining = debt.RemainingAmountVnd - paid;
        var newStatus = newRemaining == 0 ? PayableDebtStatuses.Paid : PayableDebtStatuses.Partial;
        await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE PAYABLE_DEBTS
                   SET PAID_AMOUNT_VND = PAID_AMOUNT_VND + @PAID,
                       REMAINING_AMOUNT_VND = @REM,
                       STATUS_CODE = @STATUS,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                """,
                new
                {
                    PAID = paid,
                    REM = newRemaining,
                    STATUS = newStatus,
                    BY = userId > 0 ? (object)userId : DBNull.Value,
                    ID = debtId
                },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<List<(long Id, decimal Remaining)>> LoadOpenPartyDebtsForUpdateAsync(
        FbConnection connection,
        FbTransaction transaction,
        string partyType,
        long partyId,
        long branchId,
        CancellationToken cancellationToken)
    {
        var partyColumn = partyType switch
        {
            PayablePartyTypes.Supplier => "SUPPLIER_ID",
            PayablePartyTypes.Customer => "CUSTOMER_ID",
            PayablePartyTypes.ExternalWorker => "EXTERNAL_WORKER_ID",
            _ => throw new InvalidOperationException("PARTY_TYPE không hợp lệ.")
        };

        var rows = await connection.QueryAsync<OpenPartyDebtRow>(
            new CommandDefinition($"""
                SELECT ID AS Id, REMAINING_AMOUNT_VND AS Remaining
                FROM PAYABLE_DEBTS
                WHERE {partyColumn} = @PID
                  AND PARTY_TYPE = @PTYPE
                  AND STATUS_CODE IN ('OPEN', 'PARTIAL')
                  AND BRANCH_ID = @BID
                ORDER BY CREATED_AT, ID
                WITH LOCK
                """,
                new { PID = partyId, PTYPE = partyType, BID = branchId },
                transaction,
                cancellationToken: cancellationToken));
        return rows.Select(x => (x.Id, x.Remaining)).ToList();
    }

    private static string ResolveOrderPaymentMethod(IReadOnlyList<PaymentAllocationDraft> allocations)
    {
        var methods = allocations
            .Select(x => (x.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (methods.Count != 1)
            return "MIXED";
        return methods[0] == PaymentMethods.BankTransfer ? "BANK" : methods[0];
    }

    private static string BuildSupplierDebtDescription(
        string debtKind,
        string stockCode,
        string debtCode,
        string sourceType)
    {
        var kindLabel = debtKind.Equals("LABOR", StringComparison.OrdinalIgnoreCase)
            ? "Tiền công"
            : "Nhập kho NVL";
        var sourceLabel = sourceType.Equals("OLD_GOLD_CUSTOMER", StringComparison.OrdinalIgnoreCase)
            ? "vàng cũ KH"
            : sourceType.Equals("SUPPLIER", StringComparison.OrdinalIgnoreCase)
                ? "NCC"
                : string.Empty;

        if (!string.IsNullOrWhiteSpace(stockCode))
        {
            return string.IsNullOrWhiteSpace(sourceLabel)
                ? $"{kindLabel} — phiếu {stockCode} ({debtCode})"
                : $"{kindLabel} ({sourceLabel}) — phiếu {stockCode} ({debtCode})";
        }

        return string.IsNullOrWhiteSpace(sourceLabel)
            ? $"Phát sinh {kindLabel}"
            : $"Phát sinh {kindLabel} ({sourceLabel})";
    }

    private static string ResolveStatus(decimal remaining, DateTime? lastDebtDate)
    {
        if (remaining <= 0)
            return DebtDisplayStatuses.Paid;
        if (lastDebtDate.HasValue && lastDebtDate.Value.Date < DateTime.Today.AddDays(OverdueDays * -1))
            return DebtDisplayStatuses.Overdue;
        return DebtDisplayStatuses.Normal;
    }

    private static bool MatchesStatusFilter(string? filter, string status)
    {
        var normalized = (filter ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 0 || normalized == status;
    }

    // Raw row cho SearchCustomerDebtsAsync: cột tiền tệ giữ nguyên decimal, post-process
    // (remaining, status, lọc status) vẫn làm ở C# như bản cũ.
    private sealed record CustomerDebtSearchRow(
        long CustomerId,
        string Code,
        string Name,
        string Phone,
        string GroupName,
        string Address,
        decimal TotalIncurredVnd,
        decimal PaidVnd,
        DateTime? LastDebtDate);

    private sealed record SupplierDebtSearchRow(
        long SupplierId,
        string Code,
        string Name,
        string Phone,
        string GroupName,
        string Address,
        decimal TotalIncurredVnd,
        decimal PaidVnd,
        decimal RemainingVnd,
        DateTime? LastDebtDate);

    private sealed record CustomerOrderLedgerRow(
        DateTime OrderDate,
        string Code,
        decimal GrandTotal,
        string StatusCode);

    private sealed record CustomerPaymentLedgerRow(
        DateTime PaymentDate,
        string OrderCode,
        string PaymentType,
        decimal Amount,
        string PaymentMethod);

    private sealed record CustomerOffsetLedgerRow(
        DateTime DocumentDate,
        string Code,
        decimal AmountVnd);

    private sealed record SupplierDebtLedgerRow(
        DateTime CreatedAt,
        string DocCode,
        string DebtKind,
        decimal OriginalAmountVnd,
        string StockCode,
        string SourceType,
        string DebtCode);

    private sealed record SupplierPaymentLedgerRow(
        DateTime PaymentDate,
        string DebtCode,
        decimal AmountVnd,
        string Notes);

    private sealed record OpenOrderRow(long Id, decimal Remaining);

    private sealed record OpenPartyDebtRow(long Id, decimal Remaining);

    private sealed record PayableDebtLockRow(decimal RemainingAmountVnd, string StatusCode);
}
