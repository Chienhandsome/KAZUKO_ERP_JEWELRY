using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdDebtWorkbenchRepository : IDebtWorkbenchRepository
{
    private const int OverdueDays = 30;

    private readonly IPayableDebtRepository _payables = new FirebirdPayableDebtRepository();

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

        var result = new List<CustomerDebtRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.ID, C.CODE, C.NAME, COALESCE(C.PHONE, ''), G.NAME, COALESCE(C.ADDRESS, ''),
                   COALESCE((SELECT SUM(O.GRAND_TOTAL) FROM SALES_ORDERS O
                             WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                               AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
                               AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE)), 0),
                   COALESCE((SELECT SUM(COALESCE((
                                SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                                FROM ORDER_PAYMENTS P
                                WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0))
                             FROM SALES_ORDERS O
                             WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                               AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
                               AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE)), 0),
                   (SELECT MAX(O.ORDER_DATE) FROM SALES_ORDERS O
                    WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                      AND (@HAS_FROM = 0 OR O.ORDER_DATE >= @FROM_DATE)
                      AND (@HAS_TO = 0 OR O.ORDER_DATE <= @TO_DATE))
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
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;
        command.Parameters.Add("@GROUP_ID", FbDbType.BigInt).Value = filter.GroupId;
        command.Parameters.Add("@HAS_FROM", FbDbType.Integer).Value = filter.FromDate.HasValue ? 1 : 0;
        command.Parameters.Add("@FROM_DATE", FbDbType.TimeStamp).Value = filter.FromDate ?? DateTime.Today;
        command.Parameters.Add("@HAS_TO", FbDbType.Integer).Value = filter.ToDate.HasValue ? 1 : 0;
        command.Parameters.Add("@TO_DATE", FbDbType.TimeStamp).Value = filter.ToDate ?? DateTime.Today;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var incurred = reader.GetDecimal(6);
            var paid = reader.GetDecimal(7);
            if (incurred <= 0 && paid <= 0)
                continue;
            var remaining = incurred - paid;
            if (remaining < 0) remaining = 0;
            var lastDate = reader.IsDBNull(8) ? (DateTime?)null : reader.GetDateTime(8);
            var status = ResolveStatus(remaining, lastDate);
            if (!MatchesStatusFilter(filter.StatusCode, status))
                continue;

            result.Add(new CustomerDebtRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                incurred,
                paid,
                remaining,
                lastDate,
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

        var result = new List<SupplierDebtRow>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT S.ID, S.CODE, S.NAME, COALESCE(S.PHONE, ''), COALESCE(G.NAME, ''), COALESCE(S.ADDRESS, ''),
                   COALESCE(SUM(D.ORIGINAL_AMOUNT_VND), 0),
                   COALESCE(SUM(D.PAID_AMOUNT_VND), 0),
                   COALESCE(SUM(D.REMAINING_AMOUNT_VND), 0),
                   MAX(D.CREATED_AT)
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
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;
        command.Parameters.Add("@HAS_FROM", FbDbType.Integer).Value = filter.FromDate.HasValue ? 1 : 0;
        command.Parameters.Add("@FROM_DATE", FbDbType.TimeStamp).Value = filter.FromDate ?? DateTime.Today;
        command.Parameters.Add("@HAS_TO", FbDbType.Integer).Value = filter.ToDate.HasValue ? 1 : 0;
        command.Parameters.Add("@TO_DATE", FbDbType.TimeStamp).Value = filter.ToDate ?? DateTime.Today;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var remaining = reader.GetDecimal(8);
            var lastDate = reader.IsDBNull(9) ? (DateTime?)null : reader.GetDateTime(9);
            var status = ResolveStatus(remaining, lastDate);
            if (!MatchesStatusFilter(filter.StatusCode, status))
                continue;

            result.Add(new SupplierDebtRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDecimal(6),
                reader.GetDecimal(7),
                remaining,
                lastDate,
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

        await using (var orderCmd = new FbCommand("""
            SELECT O.ORDER_DATE, O.CODE, O.GRAND_TOTAL, O.STATUS_CODE
            FROM SALES_ORDERS O
            WHERE O.CUSTOMER_ID = @CID
              AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
            ORDER BY O.ORDER_DATE, O.ID
            """, connection))
        {
            orderCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = customerId;
            await using var reader = await orderCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add((
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    "Đơn hàng " + reader.GetString(3),
                    0m,
                    reader.GetDecimal(2)));
            }
        }

        await using (var payCmd = new FbCommand("""
            SELECT P.PAYMENT_DATE, O.CODE, P.PAYMENT_TYPE, P.AMOUNT, P.PAYMENT_METHOD
            FROM ORDER_PAYMENTS P
            JOIN SALES_ORDERS O ON O.ID = P.ORDER_ID
            WHERE O.CUSTOMER_ID = @CID AND P.IS_VOIDED = FALSE
            ORDER BY P.PAYMENT_DATE, P.ID
            """, connection))
        {
            payCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = customerId;
            await using var reader = await payCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var type = reader.GetString(2);
                var amount = reader.GetDecimal(3);
                var isRefund = string.Equals(type, "REFUND", StringComparison.OrdinalIgnoreCase);
                entries.Add((
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    type + " / " + reader.GetString(4),
                    isRefund ? 0m : amount,
                    isRefund ? amount : 0m));
            }
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

        await using (var debtCmd = new FbCommand("""
            SELECT D.CREATED_AT, D.CODE, D.DEBT_KIND, D.ORIGINAL_AMOUNT_VND
            FROM PAYABLE_DEBTS D
            WHERE D.SUPPLIER_ID = @SID
              AND D.PARTY_TYPE = 'SUPPLIER'
              AND D.STATUS_CODE <> 'CANCELLED'
              AND D.BRANCH_ID = @BID
            ORDER BY D.CREATED_AT, D.ID
            """, connection))
        {
            debtCmd.Parameters.Add("@SID", FbDbType.BigInt).Value = supplierId;
            debtCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            await using var reader = await debtCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                entries.Add((
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    "Phát sinh " + reader.GetString(2),
                    0m,
                    reader.GetDecimal(3)));
            }
        }

        await using (var payCmd = new FbCommand("""
            SELECT P.PAYMENT_DATE, D.CODE, P.AMOUNT_VND, COALESCE(P.NOTES, '')
            FROM PAYABLE_DEBT_PAYMENTS P
            JOIN PAYABLE_DEBTS D ON D.ID = P.DEBT_ID
            WHERE D.SUPPLIER_ID = @SID
              AND D.PARTY_TYPE = 'SUPPLIER'
              AND D.BRANCH_ID = @BID
            ORDER BY P.PAYMENT_DATE, P.ID
            """, connection))
        {
            payCmd.Parameters.Add("@SID", FbDbType.BigInt).Value = supplierId;
            payCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
            await using var reader = await payCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var notes = reader.GetString(3);
                entries.Add((
                    reader.GetDateTime(0),
                    reader.GetString(1),
                    string.IsNullOrWhiteSpace(notes) ? "Thanh toán công nợ" : notes,
                    reader.GetDecimal(2),
                    0m));
            }
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

        var result = new List<PayableDebtListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT D.ID, D.CODE, D.PARTY_TYPE, COALESCE(S.NAME, ''),
                   D.DEBT_KIND, D.ORIGIN_DOCUMENT_TYPE, D.ORIGIN_DOCUMENT_ID,
                   D.ORIGINAL_AMOUNT_VND, D.PAID_AMOUNT_VND, D.REMAINING_AMOUNT_VND,
                   D.STATUS_CODE, D.CREATED_AT
            FROM PAYABLE_DEBTS D
            LEFT JOIN SUPPLIERS S ON S.ID = D.SUPPLIER_ID
            WHERE D.SUPPLIER_ID = @SID
              AND D.PARTY_TYPE = 'SUPPLIER'
              AND D.STATUS_CODE IN ('OPEN', 'PARTIAL')
              AND D.BRANCH_ID = @BID
            ORDER BY D.CREATED_AT, D.ID
            """, connection);
        command.Parameters.Add("@SID", FbDbType.BigInt).Value = supplierId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new PayableDebtListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt64(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.GetDecimal(9),
                reader.GetString(10),
                reader.GetDateTime(11)));
        }

        return result;
    }

    public async Task CollectCustomerPaymentAsync(
        CustomerCollectPaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);
        if (request.AmountVnd <= 0)
            throw new InvalidOperationException("Số tiền thu phải > 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var openOrders = new List<(long OrderId, decimal Remaining)>();
            await using (var cmd = new FbCommand("""
                SELECT O.ID,
                       O.GRAND_TOTAL - COALESCE((
                           SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                           FROM ORDER_PAYMENTS P
                           WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                FROM SALES_ORDERS O
                WHERE O.CUSTOMER_ID = @CID
                  AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                ORDER BY O.ORDER_DATE, O.ID
                """, connection, fbTx))
            {
                cmd.Parameters.Add("@CID", FbDbType.BigInt).Value = request.CustomerId;
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    var remaining = reader.GetDecimal(1);
                    if (remaining > 0)
                        openOrders.Add((reader.GetInt64(0), remaining));
                }
            }

            var left = request.AmountVnd;
            var totalOpen = openOrders.Sum(x => x.Remaining);
            if (left > totalOpen)
                throw new InvalidOperationException($"Số thu ({left:N0}) vượt tổng còn nợ ({totalOpen:N0}).");

            var method = string.IsNullOrWhiteSpace(request.PaymentMethod)
                ? PaymentMethods.Cash
                : request.PaymentMethod.Trim().ToUpperInvariant();
            if (method == PaymentMethods.BankTransfer)
                method = "BANK";

            foreach (var order in openOrders)
            {
                if (left <= 0)
                    break;
                var apply = Math.Min(left, order.Remaining);
                await using var insert = new FbCommand("""
                    INSERT INTO ORDER_PAYMENTS
                        (ORDER_ID, PAYMENT_DATE, PAYMENT_TYPE, AMOUNT, PAYMENT_METHOD,
                         REFERENCE_NO, NOTES, IS_VOIDED, CREATED_BY, CREATED_AT)
                    VALUES
                        (@OID, CURRENT_TIMESTAMP, 'PAYMENT', @AMT, @METHOD,
                         @REF, @NOTES, FALSE, @BY, CURRENT_TIMESTAMP)
                    """, connection, fbTx);
                insert.Parameters.Add("@OID", FbDbType.BigInt).Value = order.OrderId;
                insert.Parameters.Add("@AMT", FbDbType.Decimal).Value = apply;
                insert.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value = method;
                insert.Parameters.Add("@REF", FbDbType.VarChar, 100).Value =
                    (object?)request.ReferenceNo ?? DBNull.Value;
                insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    (object?)request.Notes ?? DBNull.Value;
                insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                await insert.ExecuteNonQueryAsync(cancellationToken);
                left -= apply;
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "CUSTOMER_DEBT_COLLECTED",
                session.UserId, session.Username, session.BranchId,
                "CUSTOMER", request.CustomerId.ToString(),
                $"{{\"amount\":{request.AmountVnd}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task PaySupplierFifoAsync(
        long supplierId,
        IReadOnlyList<PaymentAllocationDraft> allocations,
        string? notes,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var amount = allocations.Sum(x => x.AmountVnd);
        if (amount <= 0)
            throw new InvalidOperationException("Số tiền chi phải > 0.");

        var open = await LoadOpenSupplierDebtsAsync(supplierId, session, cancellationToken);
        var totalOpen = open.Sum(x => x.Remaining);
        if (amount > totalOpen)
            throw new InvalidOperationException($"Số chi ({amount:N0}) vượt tổng còn nợ NCC ({totalOpen:N0}).");

        var left = amount;
        foreach (var debt in open)
        {
            if (left <= 0)
                break;
            var apply = Math.Min(left, debt.Remaining);
            // Scale allocations proportionally for this slice
            var slice = ScaleAllocations(allocations, apply, amount);
            await _payables.PayAsync(
                new PayableDebtPaymentRequest(debt.Id, slice, notes),
                session,
                cancellationToken);
            left -= apply;
        }
    }

    private static async Task<List<(long Id, decimal Remaining)>> LoadOpenSupplierDebtsAsync(
        long supplierId,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        var rows = new List<(long Id, decimal Remaining)>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, REMAINING_AMOUNT_VND
            FROM PAYABLE_DEBTS
            WHERE SUPPLIER_ID = @SID
              AND PARTY_TYPE = 'SUPPLIER'
              AND STATUS_CODE IN ('OPEN', 'PARTIAL')
              AND BRANCH_ID = @BID
            ORDER BY CREATED_AT, ID
            """, connection);
        command.Parameters.Add("@SID", FbDbType.BigInt).Value = supplierId;
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetInt64(0), reader.GetDecimal(1)));
        return rows;
    }

    private static IReadOnlyList<PaymentAllocationDraft> ScaleAllocations(
        IReadOnlyList<PaymentAllocationDraft> source,
        decimal targetTotal,
        decimal sourceTotal)
    {
        if (sourceTotal <= 0 || targetTotal == sourceTotal)
            return source;

        var result = new List<PaymentAllocationDraft>();
        decimal assigned = 0;
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            decimal amount;
            if (i == source.Count - 1)
                amount = targetTotal - assigned;
            else
            {
                amount = Math.Round(item.AmountVnd * targetTotal / sourceTotal, 2, MidpointRounding.AwayFromZero);
                assigned += amount;
            }

            if (amount > 0)
                result.Add(item with { AmountVnd = amount });
        }

        return result;
    }

    private static string ResolveStatus(decimal remaining, DateTime? lastDebtDate)
    {
        if (remaining <= 0)
            return DebtDisplayStatuses.Paid;
        if (lastDebtDate.HasValue && lastDebtDate.Value.Date < DateTime.Today.AddDays(-OverdueDays))
            return DebtDisplayStatuses.Overdue;
        return DebtDisplayStatuses.Normal;
    }

    private static bool MatchesStatusFilter(string? filter, string status)
    {
        var normalized = (filter ?? string.Empty).Trim().ToUpperInvariant();
        return normalized.Length == 0 || normalized == status;
    }
}
