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
                          AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE)) AS LastDebtDate,
                       -- Công nợ 2 chiều: số dư hiện tại (không lọc theo kỳ tìm kiếm).
                       COALESCE((SELECT SUM(R.REMAINING_AMOUNT_VND) FROM RECEIVABLE_DEBTS R
                                 WHERE R.CUSTOMER_ID = C.ID AND R.PARTY_TYPE = 'CUSTOMER'
                                   AND R.STATUS_CODE <> 'CANCELLED' AND R.BRANCH_ID = @BID), 0)
                           AS ReceivableDebtsRemainingVnd,
                       COALESCE((SELECT SUM(D.REMAINING_AMOUNT_VND) FROM PAYABLE_DEBTS D
                                 WHERE D.CUSTOMER_ID = C.ID AND D.PARTY_TYPE = 'CUSTOMER'
                                   AND D.STATUS_CODE <> 'CANCELLED' AND D.BRANCH_ID = @BID), 0)
                           AS PayableRemainingVnd
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
                    BID = session.BranchId,
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
            // Giữ lại KH có số dư ở bất kỳ chiều nào (phải thu đơn hàng, phải thu
            // RECEIVABLE_DEBTS, hoặc DN đang nợ KH).
            if (row.TotalIncurredVnd <= 0 && row.PaidVnd <= 0
                && row.ReceivableDebtsRemainingVnd <= 0 && row.PayableRemainingVnd <= 0)
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
                status,
                ReceivableRemainingVnd: remaining + row.ReceivableDebtsRemainingVnd,
                PayableRemainingVnd: row.PayableRemainingVnd));
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
                       MAX(D.CREATED_AT) AS LastDebtDate,
                       -- Công nợ 2 chiều: NCC nợ DN (số dư hiện tại, không lọc theo kỳ).
                       COALESCE((SELECT SUM(R.REMAINING_AMOUNT_VND) FROM RECEIVABLE_DEBTS R
                                 WHERE R.SUPPLIER_ID = S.ID AND R.PARTY_TYPE = 'SUPPLIER'
                                   AND R.STATUS_CODE <> 'CANCELLED' AND R.BRANCH_ID = @BID), 0)
                           AS ReceivableRemainingVnd
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
                    OR COALESCE((SELECT SUM(R.REMAINING_AMOUNT_VND) FROM RECEIVABLE_DEBTS R
                                 WHERE R.SUPPLIER_ID = S.ID AND R.PARTY_TYPE = 'SUPPLIER'
                                   AND R.STATUS_CODE <> 'CANCELLED' AND R.BRANCH_ID = @BID), 0) > 0
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
                status,
                ReceivableRemainingVnd: row.ReceivableRemainingVnd,
                PayableRemainingVnd: row.RemainingVnd));
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

        var entries = new List<(DateTime Date, string Code, string Desc, decimal Receipt, decimal Payment,
            string? SourceType, long? SourceId, string Direction,
            decimal Original, decimal Paid, decimal Remaining, bool CanSettle, string? StatusCode)>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var orderRows = await connection.QueryAsync<CustomerOrderLedgerRow>(
            new CommandDefinition("""
                SELECT O.ID AS OrderId, O.ORDER_DATE AS OrderDate, O.CODE AS Code,
                       O.GRAND_TOTAL AS GrandTotal, O.STATUS_CODE AS StatusCode
                FROM SALES_ORDERS O
                WHERE O.CUSTOMER_ID = @CID
                  AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                ORDER BY O.ORDER_DATE, O.ID
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken));
        // Tổng đã thu theo từng đơn (cùng ngữ nghĩa cột Thu/Chi trong sổ: refund làm tăng số dư nợ).
        var paidPerOrder = (await connection.QueryAsync<OrderPaidRow>(
            new CommandDefinition("""
                SELECT P.ORDER_ID AS OrderId,
                       SUM(CASE WHEN UPPER(COALESCE(P.PAYMENT_TYPE, '')) = 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END) AS PaidTotal
                FROM ORDER_PAYMENTS P
                JOIN SALES_ORDERS O ON O.ID = P.ORDER_ID
                WHERE O.CUSTOMER_ID = @CID AND P.IS_VOIDED = FALSE
                GROUP BY P.ORDER_ID
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken))
            ).ToDictionary(r => r.OrderId, r => r.PaidTotal);
        foreach (var row in orderRows)
        {
            var paid = paidPerOrder.TryGetValue(row.OrderId, out var p) ? p : 0m;
            var remaining = row.GrandTotal - paid;
            entries.Add((
                row.OrderDate,
                row.Code,
                "Đơn hàng " + row.StatusCode,
                0m,
                row.GrandTotal,
                DebtLedgerSourceTypes.SalesOrder,
                (long?)row.OrderId,
                DebtLedgerDirections.Receive,
                row.GrandTotal,
                paid,
                remaining,
                remaining > 0,
                row.StatusCode));
        }

        var paymentRows = await connection.QueryAsync<CustomerPaymentLedgerRow>(
            new CommandDefinition("""
                SELECT P.ID AS PaymentId, P.PAYMENT_DATE AS PaymentDate, O.CODE AS OrderCode, P.PAYMENT_TYPE AS PaymentType,
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
                isRefund ? row.Amount : 0m,
                DebtLedgerSourceTypes.OrderPayment,
                (long?)row.PaymentId,
                DebtLedgerDirections.Receive,
                row.Amount,
                row.Amount,
                0m,
                false, // dòng thanh toán đã ghi nhận: không hiện nút Thu/Chi
                null));
        }

        // Spec D: khoản phải thu KH ngoài đơn hàng (vd. bồi thường hao hụt chế tác).
        var receivableRows = await connection.QueryAsync<ReceivableDebtLedgerRow>(
            new CommandDefinition("""
                SELECT D.ID AS DebtId, D.CREATED_AT AS CreatedAt, D.CODE AS Code,
                       D.DEBT_KIND AS DebtKind,
                       D.ORIGIN_DOCUMENT_TYPE AS OriginDocumentType,
                       D.ORIGIN_DOCUMENT_ID AS OriginDocumentId,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd,
                       D.COLLECTED_AMOUNT_VND AS CollectedAmountVnd,
                       D.REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       D.STATUS_CODE AS StatusCode,
                       COALESCE(D.NOTES, '') AS Notes
                FROM RECEIVABLE_DEBTS D
                WHERE D.CUSTOMER_ID = @CID
                  AND D.PARTY_TYPE = 'CUSTOMER'
                  AND D.STATUS_CODE <> 'CANCELLED'
                ORDER BY D.CREATED_AT, D.ID
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken));
        foreach (var row in receivableRows)
        {
            entries.Add((
                row.CreatedAt,
                row.Code,
                BuildReceivableDebtDescription(row.DebtKind, row.Code, row.Notes),
                0m,
                row.OriginalAmountVnd,
                DebtLedgerSourceTypes.ReceivableDebt,
                (long?)row.DebtId,
                DebtLedgerDirections.Receive,
                row.OriginalAmountVnd,
                row.CollectedAmountVnd,
                row.RemainingAmountVnd,
                row.RemainingAmountVnd > 0,
                row.StatusCode));
        }

        var offsetRows = await connection.QueryAsync<CustomerOffsetLedgerRow>(
            new CommandDefinition("""
                SELECT O.CHECKOUT_ID AS CheckoutId, C.DOCUMENT_DATE AS DocumentDate, C.CODE AS Code, O.AMOUNT_VND AS AmountVnd
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
                0m,
                DebtLedgerSourceTypes.SalesCheckoutOffset,
                (long?)row.CheckoutId,
                DebtLedgerDirections.Receive,
                row.AmountVnd,
                row.AmountVnd,
                0m,
                false, // dòng bù trừ: không hiện nút Thu/Chi
                null));
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
                balance,
                entry.SourceType,
                entry.SourceId,
                entry.Direction,
                entry.Original,
                entry.Paid,
                entry.Remaining,
                entry.CanSettle,
                StatusCode: entry.StatusCode));
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

        var entries = new List<(DateTime Date, string Code, string Desc, decimal Receipt, decimal Payment,
            string? SourceType, long? SourceId, string Direction,
            decimal Original, decimal Paid, decimal Remaining, bool CanSettle, string? StatusCode)>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var debtRows = await connection.QueryAsync<SupplierDebtLedgerRow>(
            new CommandDefinition("""
                SELECT D.ID AS DebtId,
                       D.CREATED_AT AS CreatedAt,
                       COALESCE(SD.CODE, D.CODE) AS DocCode,
                       D.DEBT_KIND AS DebtKind,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd,
                       D.PAID_AMOUNT_VND AS PaidAmountVnd,
                       D.REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       D.STATUS_CODE AS StatusCode,
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
                row.OriginalAmountVnd,
                DebtLedgerSourceTypes.PayableDebt,
                (long?)row.DebtId,
                DebtLedgerDirections.Pay,
                row.OriginalAmountVnd,
                row.PaidAmountVnd,
                row.RemainingAmountVnd,
                row.RemainingAmountVnd > 0,
                row.StatusCode));
        }

        // Spec D: NCC nợ DN (RECEIVABLE_DEBTS party=SUPPLIER) — dòng phải thu,
        // làm giảm số dư ròng "DN nợ NCC" nên ghi vào cột Thu.
        var receivableRows = await connection.QueryAsync<ReceivableDebtLedgerRow>(
            new CommandDefinition("""
                SELECT D.ID AS DebtId, D.CREATED_AT AS CreatedAt, D.CODE AS Code,
                       D.DEBT_KIND AS DebtKind,
                       D.ORIGIN_DOCUMENT_TYPE AS OriginDocumentType,
                       D.ORIGIN_DOCUMENT_ID AS OriginDocumentId,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd,
                       D.COLLECTED_AMOUNT_VND AS CollectedAmountVnd,
                       D.REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       D.STATUS_CODE AS StatusCode,
                       COALESCE(D.NOTES, '') AS Notes
                FROM RECEIVABLE_DEBTS D
                WHERE D.SUPPLIER_ID = @SID
                  AND D.PARTY_TYPE = 'SUPPLIER'
                  AND D.STATUS_CODE <> 'CANCELLED'
                  AND D.BRANCH_ID = @BID
                ORDER BY D.CREATED_AT, D.ID
                """,
                new { SID = supplierId, BID = session.BranchId },
                cancellationToken: cancellationToken));
        foreach (var row in receivableRows)
        {
            entries.Add((
                row.CreatedAt,
                row.Code,
                BuildReceivableDebtDescription(row.DebtKind, row.Code, row.Notes),
                row.OriginalAmountVnd,
                0m,
                DebtLedgerSourceTypes.ReceivableDebt,
                (long?)row.DebtId,
                DebtLedgerDirections.Receive,
                row.OriginalAmountVnd,
                row.CollectedAmountVnd,
                row.RemainingAmountVnd,
                row.RemainingAmountVnd > 0,
                row.StatusCode));
        }

        var paymentRows = await connection.QueryAsync<SupplierPaymentLedgerRow>(
            new CommandDefinition("""
                SELECT P.ID AS PaymentId, P.PAYMENT_DATE AS PaymentDate, D.CODE AS DebtCode,
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
                0m,
                DebtLedgerSourceTypes.PayableDebtPayment,
                (long?)row.PaymentId,
                DebtLedgerDirections.Pay,
                row.AmountVnd,
                row.AmountVnd,
                0m,
                false, // dòng thanh toán đã ghi nhận: không hiện nút Thu/Chi
                null));
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
                balance,
                entry.SourceType,
                entry.SourceId,
                entry.Direction,
                entry.Original,
                entry.Paid,
                entry.Remaining,
                entry.CanSettle,
                StatusCode: entry.StatusCode));
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

    /// <summary>NCC nợ DN: các khoản phải thu mở trên RECEIVABLE_DEBTS (party=SUPPLIER).</summary>
    public async Task<IReadOnlyList<ReceivableDebtListItem>> GetSupplierReceivableDebtsAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<ReceivableDebtListItem>(
            new CommandDefinition("""
                SELECT D.ID AS Id, D.CODE AS Code, D.PARTY_TYPE AS PartyType,
                       D.SUPPLIER_ID AS PartyId, D.DEBT_KIND AS DebtKind,
                       D.ORIGIN_DOCUMENT_TYPE AS OriginDocumentType,
                       D.ORIGIN_DOCUMENT_ID AS OriginDocumentId,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd,
                       D.COLLECTED_AMOUNT_VND AS CollectedAmountVnd,
                       D.REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       D.STATUS_CODE AS StatusCode, D.CREATED_AT AS CreatedAt,
                       D.NOTES AS Notes
                FROM RECEIVABLE_DEBTS D
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

    /// <summary>DN nợ KH: các khoản phải trả mở trên PAYABLE_DEBTS (party=CUSTOMER).</summary>
    public async Task<IReadOnlyList<PayableDebtListItem>> GetCustomerOpenPayablesAsync(
        long customerId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<PayableDebtListItem>(
            new CommandDefinition("""
                SELECT D.ID AS Id, D.CODE AS Code, D.PARTY_TYPE AS PartyType, COALESCE(C.NAME, '') AS PartyName,
                       D.DEBT_KIND AS DebtKind, D.ORIGIN_DOCUMENT_TYPE AS OriginDocumentType,
                       D.ORIGIN_DOCUMENT_ID AS OriginDocumentId,
                       D.ORIGINAL_AMOUNT_VND AS OriginalAmountVnd, D.PAID_AMOUNT_VND AS PaidAmountVnd,
                       D.REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       D.STATUS_CODE AS StatusCode, D.CREATED_AT AS CreatedAt
                FROM PAYABLE_DEBTS D
                LEFT JOIN CUSTOMERS C ON C.ID = D.CUSTOMER_ID
                WHERE D.CUSTOMER_ID = @CID
                  AND D.PARTY_TYPE = 'CUSTOMER'
                  AND D.STATUS_CODE IN ('OPEN', 'PARTIAL')
                  AND D.BRANCH_ID = @BID
                ORDER BY D.CREATED_AT, D.ID
                """,
                new { CID = customerId, BID = session.BranchId },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public Task PayCustomerFifoAsync(
        long customerId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        PayPayablePartyFifoAsync(
            PayablePartyTypes.Customer, customerId, allocations, notes, session, cancellationToken);

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

            // Spec C: re-check từng đơn sau khi lock — nếu đơn đã bị thu bởi
            // người dùng khác (snapshot lúc UI đọc đã cũ) thì báo lỗi rõ ràng.
            var applications = new List<SettlementApplicationDraft>();
            var left = amount;
            foreach (var order in openOrders)
            {
                if (left <= 0)
                    break;
                var fresh = await connection.QuerySingleOrDefaultAsync<OpenOrderRecheckRow>(
                    new CommandDefinition("""
                        SELECT O.ID AS Id, O.CODE AS Code,
                               O.GRAND_TOTAL - COALESCE((
                                   SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                                   FROM ORDER_PAYMENTS P
                                   WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0) AS Remaining
                        FROM SALES_ORDERS O
                        WHERE O.ID = @OID
                        WITH LOCK
                        """,
                        new { OID = order.OrderId },
                        fbTx,
                        cancellationToken: cancellationToken));
                if (fresh is null || fresh.Remaining <= 0)
                    throw new InvalidOperationException(
                        $"Đơn hàng {(fresh?.Code ?? "#" + order.OrderId)} đã được thanh toán hết bởi người dùng khác. Vui lòng tải lại và thử lại.");
                var planned = Math.Min(left, order.Remaining);
                if (fresh.Remaining < planned)
                    throw new InvalidOperationException(
                        $"Đơn hàng {fresh.Code} vừa được thanh toán một phần bởi người dùng khác. Vui lòng tải lại và thử lại.");
                applications.Add(new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.SalesOrder, order.OrderId, planned));
                left -= planned;
            }

            // Thu toàn bộ = 1 settlement + 1 phiếu thu duy nhất cho tổng tiền (spec B).
            var settlement = await _settlements.PostBulkSettlementInTransactionAsync(
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

    /// <summary>
    /// Thu gộp tiền NCC nợ DN: lock FIFO các khoản RECEIVABLE_DEBTS mở của NCC,
    /// tạo 1 settlement + 1 phiếu thu duy nhất, phân bổ N dòng application,
    /// ghi RECEIVABLE_DEBT_COLLECTIONS và cập nhật từng khoản nợ.
    /// Không bù trừ với chiều phải trả — thu và chi là 2 lần bấm riêng.
    /// </summary>
    public async Task CollectSupplierPaymentAsync(
        SupplierCollectPaymentRequest request,
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

            var openDebtRows = await connection.QueryAsync<OpenReceivableDebtRow>(
                new CommandDefinition("""
                    SELECT ID AS Id,
                           REMAINING_AMOUNT_VND AS Remaining
                    FROM RECEIVABLE_DEBTS
                    WHERE SUPPLIER_ID = @SID
                      AND PARTY_TYPE = 'SUPPLIER'
                      AND STATUS_CODE IN ('OPEN', 'PARTIAL')
                      AND BRANCH_ID = @BID
                    ORDER BY CREATED_AT, ID
                    WITH LOCK
                    """,
                    new { SID = request.SupplierId, BID = session.BranchId },
                    fbTx,
                    cancellationToken: cancellationToken));
            var openDebts = new List<(long DebtId, decimal Remaining)>();
            foreach (var row in openDebtRows)
            {
                if (row.Remaining > 0)
                    openDebts.Add((row.Id, row.Remaining));
            }

            var totalOpen = openDebts.Sum(x => x.Remaining);
            if (amount > totalOpen)
                throw new InvalidOperationException($"Số thu ({amount:N0}) vượt tổng còn nợ ({totalOpen:N0}).");

            var applications = new List<SettlementApplicationDraft>();
            var left = amount;
            foreach (var debt in openDebts)
            {
                if (left <= 0)
                    break;
                var apply = Math.Min(left, debt.Remaining);
                applications.Add(new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.ReceivableDebt, debt.DebtId, apply));
                left -= apply;
            }

            // Thu toàn bộ = 1 settlement + 1 phiếu thu duy nhất cho tổng tiền (spec B).
            var settlement = await _settlements.PostBulkSettlementInTransactionAsync(
                connection, fbTx, companyId, session.BranchId,
                new FinancialSettlementDraft(
                    SettlementTypes.Receipt,
                    SettlementPartyTypes.Supplier,
                    request.SupplierId,
                    DateTime.Now,
                    allocations,
                    applications,
                    request.Notes),
                session.UserId,
                cancellationToken);

            foreach (var application in applications)
            {
                await ApplyReceivableDebtCollectionAsync(
                    connection, fbTx, application.TargetId, application.AmountVnd,
                    request.Notes, settlement.SettlementId, settlement.Code,
                    session.UserId, cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "SUPPLIER_RECEIVABLE_COLLECTED",
                session.UserId, session.Username, session.BranchId,
                "SUPPLIER", request.SupplierId.ToString(),
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

            // Chi toàn bộ = 1 settlement + 1 phiếu chi duy nhất cho tổng tiền (spec B).
            var settlement = await _settlements.PostBulkSettlementInTransactionAsync(
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

    /// <summary>
    /// Tất toán lẻ đúng 1 khoản nguồn (nút Thu/Chi trên từng dòng lịch sử):
    /// 1 settlement + 1 phiếu thu/chi + cập nhật khoản nợ trong 1 transaction.
    /// Tái dùng PostBulkSettlementInTransactionAsync (1 application) và các helper
    /// ApplyReceivableDebtCollectionAsync / ApplyPayableDebtPaymentAsync đã có.
    /// </summary>
    public async Task SettleSingleLedgerEntryAsync(
        SingleLedgerSettleRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var sourceType = (request.SourceType ?? string.Empty).Trim().ToUpperInvariant();
        if (sourceType is not (
            DebtLedgerSourceTypes.SalesOrder or
            DebtLedgerSourceTypes.ReceivableDebt or
            DebtLedgerSourceTypes.PayableDebt))
            throw new InvalidOperationException("Nguồn công nợ không hỗ trợ tất toán lẻ.");
        if (request.SourceId <= 0)
            throw new InvalidOperationException("Thiếu id khoản công nợ cần tất toán.");

        var allocations = request.Allocations ?? Array.Empty<PaymentAllocationDraft>();
        var amount = allocations.Sum(x => x.AmountVnd);
        var errors = PaymentAllocationValidator.Validate(allocations, amount);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        if (amount <= 0)
            throw new InvalidOperationException("Số tiền thanh toán phải > 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

            var (settlement, auditAction, partyType, partyId) = sourceType switch
            {
                DebtLedgerSourceTypes.SalesOrder => await CollectSingleSalesOrderAsync(
                    connection, fbTx, companyId, request.SourceId,
                    allocations, amount, notes, session, cancellationToken),
                DebtLedgerSourceTypes.ReceivableDebt => await CollectSingleReceivableDebtAsync(
                    connection, fbTx, companyId, request.SourceId,
                    allocations, amount, notes, session, cancellationToken),
                _ => await PaySinglePayableDebtAsync(
                    connection, fbTx, companyId, request.SourceId,
                    allocations, amount, notes, session, cancellationToken)
            };

            await AuditLogWriter.WriteAsync(
                connection, fbTx, auditAction,
                session.UserId, session.Username, session.BranchId,
                partyType, partyId.ToString(),
                $"{{\"settlementId\":{settlement.SettlementId},\"amount\":{amount},\"sourceId\":{request.SourceId}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task SettleDebtAllocationsAsync(
        DebtAllocationSettleRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var lines = request.Lines ?? Array.Empty<DebtAllocationSettleLine>();
        if (lines.Count == 0)
            throw new InvalidOperationException("Phải có ít nhất một khoản công nợ cần tất toán.");

        foreach (var line in lines)
        {
            var st = (line.SourceType ?? string.Empty).Trim().ToUpperInvariant();
            if (st is not (
                DebtLedgerSourceTypes.SalesOrder or
                DebtLedgerSourceTypes.ReceivableDebt or
                DebtLedgerSourceTypes.PayableDebt))
                throw new InvalidOperationException($"Nguồn công nợ không hỗ trợ: {line.SourceType}.");
            if (line.SourceId <= 0)
                throw new InvalidOperationException("Thiếu id khoản công nợ cần tất toán.");
            if (line.AmountVnd <= 0)
                throw new InvalidOperationException("Số tiền tất toán mỗi dòng phải > 0.");
        }

        var totalLines = lines.Sum(x => x.AmountVnd);
        var totalPayment = request.CashAmountVnd + request.BankTransferAmountVnd + request.CardAmountVnd;
        if (totalPayment <= 0)
            throw new InvalidOperationException("Tổng thanh toán phải > 0.");
        if (totalPayment != totalLines)
            throw new InvalidOperationException(
                $"Tổng thanh toán ({totalPayment:N0}) phải bằng tổng phân bổ ({totalLines:N0}).");

        // Dựng danh sách PaymentAllocationDraft từ 3 ô phương thức.
        var methodAllocations = new List<PaymentAllocationDraft>();
        if (request.CashAmountVnd > 0)
        {
            if (request.FundId is not > 0)
                throw new InvalidOperationException("Tiền mặt phải chọn quỹ.");
            methodAllocations.Add(new PaymentAllocationDraft(
                PaymentMethods.Cash, request.CashAmountVnd, request.FundId, null, null));
        }
        if (request.BankTransferAmountVnd > 0)
        {
            if (request.BankAccountId is not > 0)
                throw new InvalidOperationException("Chuyển khoản phải chọn tài khoản ngân hàng.");
            methodAllocations.Add(new PaymentAllocationDraft(
                PaymentMethods.BankTransfer, request.BankTransferAmountVnd, null, request.BankAccountId, null));
        }
        if (request.CardAmountVnd > 0)
        {
            if (request.BankAccountId is not > 0)
                throw new InvalidOperationException("Thẻ phải chọn tài khoản ngân hàng.");
            methodAllocations.Add(new PaymentAllocationDraft(
                PaymentMethods.Card, request.CardAmountVnd, null, request.BankAccountId, null));
        }
        var errors = PaymentAllocationValidator.Validate(methodAllocations, totalPayment).ToList();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();

            // Chia phương thức thanh toán cho từng dòng theo thứ tự (ưu tiên tiền mặt trước).
            // Mỗi dòng tạo 1 settlement riêng, tất cả trong 1 transaction.
            var remainingCash = request.CashAmountVnd;
            var remainingBank = request.BankTransferAmountVnd;
            var remainingCard = request.CardAmountVnd;

            foreach (var line in lines)
            {
                var lineAllocations = new List<PaymentAllocationDraft>();
                var need = line.AmountVnd;

                var takeCash = Math.Min(need, remainingCash);
                if (takeCash > 0)
                {
                    lineAllocations.Add(new PaymentAllocationDraft(
                        PaymentMethods.Cash, takeCash, request.FundId, null, null));
                    remainingCash -= takeCash;
                    need -= takeCash;
                }
                var takeBank = Math.Min(need, remainingBank);
                if (takeBank > 0)
                {
                    lineAllocations.Add(new PaymentAllocationDraft(
                        PaymentMethods.BankTransfer, takeBank, null, request.BankAccountId, null));
                    remainingBank -= takeBank;
                    need -= takeBank;
                }
                var takeCard = Math.Min(need, remainingCard);
                if (takeCard > 0)
                {
                    lineAllocations.Add(new PaymentAllocationDraft(
                        PaymentMethods.Card, takeCard, null, request.BankAccountId, null));
                    remainingCard -= takeCard;
                    need -= takeCard;
                }

                if (need != 0)
                    throw new InvalidOperationException("Lỗi chia phương thức thanh toán cho các dòng.");

                var sourceType = (line.SourceType ?? string.Empty).Trim().ToUpperInvariant();
                var (settlement, auditAction, partyType, partyId) = sourceType switch
                {
                    DebtLedgerSourceTypes.SalesOrder => await CollectSingleSalesOrderAsync(
                        connection, fbTx, companyId, line.SourceId,
                        lineAllocations, line.AmountVnd, notes, session, cancellationToken),
                    DebtLedgerSourceTypes.ReceivableDebt => await CollectSingleReceivableDebtAsync(
                        connection, fbTx, companyId, line.SourceId,
                        lineAllocations, line.AmountVnd, notes, session, cancellationToken),
                    _ => await PaySinglePayableDebtAsync(
                        connection, fbTx, companyId, line.SourceId,
                        lineAllocations, line.AmountVnd, notes, session, cancellationToken)
                };

                await AuditLogWriter.WriteAsync(
                    connection, fbTx, auditAction,
                    session.UserId, session.Username, session.BranchId,
                    partyType, partyId.ToString(),
                    $"{{\"settlementId\":{settlement.SettlementId},\"amount\":{line.AmountVnd},\"sourceId\":{line.SourceId}}}",
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<OpenCustomerReceivableLine>> GetCustomerOpenReceivablesAsync(
        long customerId,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (customerId <= 0)
            return Array.Empty<OpenCustomerReceivableLine>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, null!, cancellationToken);
            var rows = await connection.QueryAsync<OpenCustomerReceivableLine>(
                new CommandDefinition("""
                    SELECT O.ID AS OrderId,
                           O.CODE AS OrderCode,
                           O.GRAND_TOTAL - COALESCE((
                               SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                               FROM ORDER_PAYMENTS P
                               WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0) AS RemainingAmountVnd,
                           O.ORDER_DATE AS OrderDate
                    FROM SALES_ORDERS O
                    WHERE O.COMPANY_ID = @CID
                      AND O.BRANCH_ID = @BID
                      AND O.CUSTOMER_ID = @CUST
                      AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                      AND O.GRAND_TOTAL - COALESCE((
                          SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                          FROM ORDER_PAYMENTS P
                          WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0) > 0
                    ORDER BY O.ORDER_DATE, O.ID
                    """,
                    new
                    {
                        CID = companyId,
                        BID = session.BranchId,
                        CUST = customerId
                    },
                    cancellationToken: cancellationToken));
            return rows.AsList();
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private async Task<(FinancialSettlementResult Settlement, string AuditAction, string PartyType, long PartyId)> CollectSingleSalesOrderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long orderId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal amount,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var order = await connection.QuerySingleOrDefaultAsync<SingleOrderLockRow>(
            new CommandDefinition("""
                SELECT O.CUSTOMER_ID AS CustomerId, O.CODE AS Code,
                       O.GRAND_TOTAL - COALESCE((
                           SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                           FROM ORDER_PAYMENTS P
                           WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0) AS Remaining
                FROM SALES_ORDERS O
                WHERE O.ID = @ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                WITH LOCK
                """,
                new { ID = orderId },
                transaction,
                cancellationToken: cancellationToken));
        if (order is null)
            throw new InvalidOperationException($"Không tìm thấy đơn hàng #{orderId}.");
        // Spec C: re-check sau khi lock.
        if (order.Remaining <= 0)
            throw new InvalidOperationException($"Đơn hàng {order.Code} đã được thanh toán hết bởi người dùng khác. Vui lòng tải lại.");
        if (amount > order.Remaining)
            throw new InvalidOperationException($"Số thu ({amount:N0}) vượt số còn nợ của đơn {order.Code} ({order.Remaining:N0}).");

        var settlement = await _settlements.PostBulkSettlementInTransactionAsync(
            connection, transaction, companyId, session.BranchId,
            new FinancialSettlementDraft(
                SettlementTypes.Receipt,
                SettlementPartyTypes.Customer,
                order.CustomerId,
                DateTime.Now,
                allocations,
                [new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.SalesOrder, orderId, amount)],
                notes),
            session.UserId,
            cancellationToken);

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
                    OID = orderId,
                    AMT = amount,
                    METHOD = ResolveOrderPaymentMethod(allocations),
                    REF = settlement.Code,
                    NOTES = (object?)notes ?? DBNull.Value,
                    BY = session.UserId > 0 ? (object)session.UserId : DBNull.Value
                },
                transaction,
                cancellationToken: cancellationToken));

        return (settlement, "SALES_ORDER_SINGLE_COLLECTED", SettlementPartyTypes.Customer, order.CustomerId);
    }

    private async Task<(FinancialSettlementResult Settlement, string AuditAction, string PartyType, long PartyId)> CollectSingleReceivableDebtAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long debtId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal amount,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var debt = await connection.QuerySingleOrDefaultAsync<SingleDebtLockRow>(
            new CommandDefinition("""
                SELECT PARTY_TYPE AS PartyType,
                       COALESCE(CUSTOMER_ID, SUPPLIER_ID, EXTERNAL_WORKER_ID) AS PartyId,
                       REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       STATUS_CODE AS StatusCode
                FROM RECEIVABLE_DEBTS
                WHERE ID = @ID
                WITH LOCK
                """,
                new { ID = debtId },
                transaction,
                cancellationToken: cancellationToken));
        if (debt is null)
            throw new InvalidOperationException($"Không tìm thấy khoản phải thu #{debtId}.");
        if (debt.StatusCode is ReceivableDebtStatuses.Paid or ReceivableDebtStatuses.Cancelled)
            throw new InvalidOperationException($"Khoản phải thu #{debtId} đã đóng ({debt.StatusCode}).");
        if (debt.RemainingAmountVnd <= 0)
            throw new InvalidOperationException($"Khoản phải thu #{debtId} đã được thu hết bởi người dùng khác. Vui lòng tải lại.");
        if (amount > debt.RemainingAmountVnd)
            throw new InvalidOperationException($"Số thu ({amount:N0}) vượt số còn phải thu của #{debtId} ({debt.RemainingAmountVnd:N0}).");

        var settlement = await _settlements.PostBulkSettlementInTransactionAsync(
            connection, transaction, companyId, session.BranchId,
            new FinancialSettlementDraft(
                SettlementTypes.Receipt,
                debt.PartyType,
                debt.PartyId,
                DateTime.Now,
                allocations,
                [new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.ReceivableDebt, debtId, amount)],
                notes),
            session.UserId,
            cancellationToken);

        await ApplyReceivableDebtCollectionAsync(
            connection, transaction, debtId, amount,
            notes, settlement.SettlementId, settlement.Code,
            session.UserId, cancellationToken);

        return (settlement, "RECEIVABLE_DEBT_SINGLE_COLLECTED", debt.PartyType, debt.PartyId);
    }

    private async Task<(FinancialSettlementResult Settlement, string AuditAction, string PartyType, long PartyId)> PaySinglePayableDebtAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long debtId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        decimal amount,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var debt = await connection.QuerySingleOrDefaultAsync<SingleDebtLockRow>(
            new CommandDefinition("""
                SELECT PARTY_TYPE AS PartyType,
                       COALESCE(CUSTOMER_ID, SUPPLIER_ID, EXTERNAL_WORKER_ID) AS PartyId,
                       REMAINING_AMOUNT_VND AS RemainingAmountVnd,
                       STATUS_CODE AS StatusCode
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
        if (debt.RemainingAmountVnd <= 0)
            throw new InvalidOperationException($"Công nợ #{debtId} đã được thanh toán hết bởi người dùng khác. Vui lòng tải lại.");
        if (amount > debt.RemainingAmountVnd)
            throw new InvalidOperationException($"Số thanh toán ({amount:N0}) vượt số còn nợ của #{debtId} ({debt.RemainingAmountVnd:N0}).");

        var settlement = await _settlements.PostBulkSettlementInTransactionAsync(
            connection, transaction, companyId, session.BranchId,
            new FinancialSettlementDraft(
                SettlementTypes.Payment,
                debt.PartyType,
                debt.PartyId,
                DateTime.Now,
                allocations,
                [new SettlementApplicationDraft(
                    SettlementApplicationTargetTypes.PayableDebt, debtId, amount)],
                notes),
            session.UserId,
            cancellationToken);

        await ApplyPayableDebtPaymentAsync(
            connection, transaction, debtId, amount,
            notes, settlement.Code, session.UserId, cancellationToken);

        return (settlement, "PAYABLE_DEBT_SINGLE_PAID", debt.PartyType, debt.PartyId);
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
        // Spec C: re-check sau khi lock — khoản nợ có thể đã được thanh toán
        // bởi người dùng khác giữa lúc UI đọc và lúc transaction này bắt đầu.
        if (debt.RemainingAmountVnd <= 0)
            throw new InvalidOperationException($"Công nợ #{debtId} đã được thanh toán hết bởi người dùng khác.");
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

    /// <summary>
    /// Ghi nhận thu một khoản phải thu: lock row, re-check số dư (spec C),
    /// insert RECEIVABLE_DEBT_COLLECTIONS và cập nhật RECEIVABLE_DEBTS.
    /// </summary>
    private static async Task ApplyReceivableDebtCollectionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long debtId,
        decimal collected,
        string? notes,
        long settlementId,
        string settlementCode,
        long userId,
        CancellationToken cancellationToken)
    {
        var debt = await connection.QuerySingleOrDefaultAsync<ReceivableDebtLockRow>(
            new CommandDefinition("""
                SELECT REMAINING_AMOUNT_VND AS RemainingAmountVnd, STATUS_CODE AS StatusCode
                FROM RECEIVABLE_DEBTS
                WHERE ID = @ID
                WITH LOCK
                """,
                new { ID = debtId },
                transaction,
                cancellationToken: cancellationToken));
        if (debt is null)
            throw new InvalidOperationException($"Không tìm thấy khoản phải thu #{debtId}.");

        if (debt.StatusCode is ReceivableDebtStatuses.Paid or ReceivableDebtStatuses.Cancelled)
            throw new InvalidOperationException($"Khoản phải thu #{debtId} đã đóng ({debt.StatusCode}).");
        // Spec C: re-check sau khi lock — khoản nợ có thể đã được thu
        // bởi người dùng khác giữa lúc UI đọc và lúc transaction này bắt đầu.
        if (debt.RemainingAmountVnd <= 0)
            throw new InvalidOperationException($"Khoản phải thu #{debtId} đã được thu hết bởi người dùng khác.");
        if (collected > debt.RemainingAmountVnd)
            throw new InvalidOperationException($"Số thu vượt số còn phải thu của #{debtId}.");

        await connection.ExecuteAsync(
            new CommandDefinition("""
                INSERT INTO RECEIVABLE_DEBT_COLLECTIONS
                    (DEBT_ID, COLLECTION_DATE, AMOUNT_VND, SETTLEMENT_ID, NOTES, CREATED_BY, CREATED_AT)
                VALUES
                    (@DEBT, CURRENT_TIMESTAMP, @AMT, @SID, @NOTES, @BY, CURRENT_TIMESTAMP)
                """,
                new
                {
                    DEBT = debtId,
                    AMT = collected,
                    SID = settlementId,
                    NOTES = string.IsNullOrWhiteSpace(notes) ? settlementCode : $"{settlementCode}: {notes}",
                    BY = userId > 0 ? (object)userId : DBNull.Value
                },
                transaction,
                cancellationToken: cancellationToken));

        var newRemaining = debt.RemainingAmountVnd - collected;
        var newStatus = newRemaining == 0 ? ReceivableDebtStatuses.Paid : ReceivableDebtStatuses.Partial;
        await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE RECEIVABLE_DEBTS
                   SET COLLECTED_AMOUNT_VND = COLLECTED_AMOUNT_VND + @AMT,
                       REMAINING_AMOUNT_VND = @REM,
                       STATUS_CODE = @STATUS,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                """,
                new
                {
                    AMT = collected,
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

    private static string BuildReceivableDebtDescription(
        string debtKind,
        string debtCode,
        string notes)
    {
        var kindLabel = debtKind.Equals(ReceivableDebtKinds.LossCompensation, StringComparison.OrdinalIgnoreCase)
            ? "Bồi thường hao hụt"
            : debtKind.Equals(ReceivableDebtKinds.Sales, StringComparison.OrdinalIgnoreCase)
                ? "Bán hàng"
                : debtKind;
        var desc = $"Phải thu {kindLabel} ({debtCode})";
        return string.IsNullOrWhiteSpace(notes) ? desc : $"{desc} — {notes}";
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
        DateTime? LastDebtDate,
        decimal ReceivableDebtsRemainingVnd,
        decimal PayableRemainingVnd);

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
        DateTime? LastDebtDate,
        decimal ReceivableRemainingVnd);

    private sealed record CustomerOrderLedgerRow(
        long OrderId,
        DateTime OrderDate,
        string Code,
        decimal GrandTotal,
        string StatusCode);

    private sealed record OrderPaidRow(long OrderId, decimal PaidTotal);

    private sealed record CustomerPaymentLedgerRow(
        long PaymentId,
        DateTime PaymentDate,
        string OrderCode,
        string PaymentType,
        decimal Amount,
        string PaymentMethod);

    private sealed record CustomerOffsetLedgerRow(
        long CheckoutId,
        DateTime DocumentDate,
        string Code,
        decimal AmountVnd);

    private sealed record ReceivableDebtLedgerRow(
        long DebtId,
        DateTime CreatedAt,
        string Code,
        string DebtKind,
        string OriginDocumentType,
        long OriginDocumentId,
        decimal OriginalAmountVnd,
        decimal CollectedAmountVnd,
        decimal RemainingAmountVnd,
        string StatusCode,
        string Notes);

    private sealed record SupplierDebtLedgerRow(
        long DebtId,
        DateTime CreatedAt,
        string DocCode,
        string DebtKind,
        decimal OriginalAmountVnd,
        decimal PaidAmountVnd,
        decimal RemainingAmountVnd,
        string StatusCode,
        string StockCode,
        string SourceType,
        string DebtCode);

    private sealed record SupplierPaymentLedgerRow(
        long PaymentId,
        DateTime PaymentDate,
        string DebtCode,
        decimal AmountVnd,
        string Notes);

    private sealed record OpenOrderRow(long Id, decimal Remaining);

    private sealed record OpenOrderRecheckRow(long Id, string Code, decimal Remaining);

    private sealed record OpenPartyDebtRow(long Id, decimal Remaining);

    private sealed record OpenReceivableDebtRow(long Id, decimal Remaining);

    private sealed record PayableDebtLockRow(decimal RemainingAmountVnd, string StatusCode);

    private sealed record SingleOrderLockRow(long CustomerId, string Code, decimal Remaining);

    private sealed record SingleDebtLockRow(string PartyType, long PartyId, decimal RemainingAmountVnd, string StatusCode);

    private sealed record ReceivableDebtLockRow(decimal RemainingAmountVnd, string StatusCode);
}
