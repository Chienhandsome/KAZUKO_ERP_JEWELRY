using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdCashFundRepository : ICashFundRepository
{
    public async Task<IReadOnlyList<CashFundOption>> GetActiveFundsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var result = new List<CashFundOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, NAME, IS_DEFAULT
            FROM CASH_FUNDS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND BRANCH_ID = @BID
              AND IS_ACTIVE = TRUE
            ORDER BY IS_DEFAULT DESC, NAME
            """, connection);
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CashFundOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3)));
        }

        return result;
    }
}

public sealed class FirebirdBankAccountRepository : IBankAccountRepository
{
    public async Task<IReadOnlyList<BankAccountOption>> GetActiveAccountsAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var result = new List<BankAccountOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, BANK_NAME, ACCOUNT_NUMBER
            FROM BANK_ACCOUNTS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND BRANCH_ID = @BID
              AND IS_ACTIVE = TRUE
            ORDER BY BANK_NAME, ACCOUNT_NUMBER
            """, connection);
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new BankAccountOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return result;
    }
}

public sealed class FirebirdPayableDebtRepository : IPayableDebtRepository
{
    private readonly FirebirdFinancialVoucherPostingService _vouchers = new();

    public async Task<IReadOnlyList<PayableDebtListItem>> SearchAsync(
        PayableDebtSearchFilter filter,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var result = new List<PayableDebtListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT D.ID, D.CODE, D.PARTY_TYPE,
                   COALESCE(S.NAME, C.NAME, W.NAME, ''),
                   D.DEBT_KIND, D.ORIGIN_DOCUMENT_TYPE, D.ORIGIN_DOCUMENT_ID,
                   D.ORIGINAL_AMOUNT_VND, D.PAID_AMOUNT_VND, D.REMAINING_AMOUNT_VND,
                   D.STATUS_CODE, D.CREATED_AT
            FROM PAYABLE_DEBTS D
            LEFT JOIN SUPPLIERS S ON S.ID = D.SUPPLIER_ID
            LEFT JOIN CUSTOMERS C ON C.ID = D.CUSTOMER_ID
            LEFT JOIN EXTERNAL_WORKERS W ON W.ID = D.EXTERNAL_WORKER_ID
            WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND D.BRANCH_ID = @BID
              AND (@HAS_STATUS = 0 OR D.STATUS_CODE = @STATUS)
              AND (@HAS_PARTY = 0 OR D.PARTY_TYPE = @PARTY)
              AND (@HAS_SEARCH = 0
                   OR CAST(D.CODE AS VARCHAR(40) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR COALESCE(S.NAME, C.NAME, W.NAME, '') CONTAINING @SEARCH)
            ORDER BY D.CREATED_AT DESC, D.CODE
            """, connection);

        var search = (filter.SearchText ?? string.Empty).Trim();
        var status = (filter.StatusCode ?? string.Empty).Trim().ToUpperInvariant();
        var party = (filter.PartyType ?? string.Empty).Trim().ToUpperInvariant();
        command.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
        command.Parameters.Add("@HAS_STATUS", FbDbType.Integer).Value = status.Length > 0 ? 1 : 0;
        command.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = status;
        command.Parameters.Add("@HAS_PARTY", FbDbType.Integer).Value = party.Length > 0 ? 1 : 0;
        command.Parameters.Add("@PARTY", FbDbType.VarChar, 30).Value = party;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;

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

    public async Task PayAsync(
        PayableDebtPaymentRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var paid = request.Allocations?.Sum(x => x.AmountVnd) ?? 0;
        var errors = PaymentAllocationValidator.Validate(
            request.Allocations ?? Array.Empty<PaymentAllocationDraft>(), paid);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
        if (paid <= 0)
            throw new InvalidOperationException("Số thanh toán phải > 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            decimal remaining;
            await using (var debtCmd = new FbCommand("""
                SELECT REMAINING_AMOUNT_VND, STATUS_CODE
                FROM PAYABLE_DEBTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                debtCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = request.DebtId;
                debtCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                debtCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await debtCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy công nợ.");
                remaining = reader.GetDecimal(0);
                var status = reader.GetString(1);
                if (status is PayableDebtStatuses.Paid or PayableDebtStatuses.Cancelled)
                    throw new InvalidOperationException("Công nợ đã đóng.");
            }

            if (paid > remaining)
                throw new InvalidOperationException("Số thanh toán vượt số còn nợ.");

            long paymentId;
            await using (var insertPay = new FbCommand("""
                INSERT INTO PAYABLE_DEBT_PAYMENTS (DEBT_ID, PAYMENT_DATE, AMOUNT_VND, NOTES, CREATED_BY, CREATED_AT)
                VALUES (@DEBT, CURRENT_TIMESTAMP, @AMT, @NOTES, @BY, CURRENT_TIMESTAMP)
                RETURNING ID
                """, connection, fbTx))
            {
                insertPay.Parameters.Add("@DEBT", FbDbType.BigInt).Value = request.DebtId;
                insertPay.Parameters.Add("@AMT", FbDbType.Decimal).Value = paid;
                insertPay.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    (object?)request.Notes ?? DBNull.Value;
                insertPay.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                paymentId = Convert.ToInt64(await insertPay.ExecuteScalarAsync(cancellationToken));
            }

            var line = 0;
            foreach (var allocation in request.Allocations!)
            {
                line++;
                long allocationId;
                await using (var insertAlloc = new FbCommand("""
                    INSERT INTO PAYABLE_DEBT_PAYMENT_ALLOCATIONS
                        (DEBT_PAYMENT_ID, LINE_NUMBER, PAYMENT_METHOD, AMOUNT_VND,
                         CASH_FUND_ID, BANK_ACCOUNT_ID, REFERENCE_NUMBER)
                    VALUES
                        (@PAY, @LN, @METHOD, @AMT, @FUND, @BANK, @REF)
                    RETURNING ID
                    """, connection, fbTx))
                {
                    insertAlloc.Parameters.Add("@PAY", FbDbType.BigInt).Value = paymentId;
                    insertAlloc.Parameters.Add("@LN", FbDbType.Integer).Value = line;
                    insertAlloc.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value =
                        allocation.PaymentMethod.Trim().ToUpperInvariant();
                    insertAlloc.Parameters.Add("@AMT", FbDbType.Decimal).Value = allocation.AmountVnd;
                    insertAlloc.Parameters.Add("@FUND", FbDbType.BigInt).Value =
                        allocation.CashFundId is > 0 ? allocation.CashFundId.Value : DBNull.Value;
                    insertAlloc.Parameters.Add("@BANK", FbDbType.BigInt).Value =
                        allocation.BankAccountId is > 0 ? allocation.BankAccountId.Value : DBNull.Value;
                    insertAlloc.Parameters.Add("@REF", FbDbType.VarChar, 100).Value =
                        (object?)allocation.ReferenceNumber ?? DBNull.Value;
                    allocationId = Convert.ToInt64(await insertAlloc.ExecuteScalarAsync(cancellationToken));
                }

                var voucher = await _vouchers.PostPaymentInTransactionAsync(
                    connection, fbTx, companyId, session.BranchId,
                    new FinancialVoucherPostRequest(
                        allocation,
                        "PAYABLE_DEBT",
                        request.DebtId,
                        FinancialVoucherSourceAllocationTypes.PayableDebtPaymentAllocation,
                        allocationId),
                    session.UserId,
                    cancellationToken);

                await using var linkVoucher = new FbCommand("""
                    UPDATE PAYABLE_DEBT_PAYMENT_ALLOCATIONS
                       SET CASH_VOUCHER_ID = @VID
                     WHERE ID = @ID
                    """, connection, fbTx);
                linkVoucher.Parameters.Add("@VID", FbDbType.BigInt).Value = voucher.VoucherId;
                linkVoucher.Parameters.Add("@ID", FbDbType.BigInt).Value = allocationId;
                await linkVoucher.ExecuteNonQueryAsync(cancellationToken);
            }

            var newRemaining = remaining - paid;
            var newStatus = newRemaining == 0 ? PayableDebtStatuses.Paid : PayableDebtStatuses.Partial;
            await using (var updateDebt = new FbCommand("""
                UPDATE PAYABLE_DEBTS
                   SET PAID_AMOUNT_VND = PAID_AMOUNT_VND + @PAID,
                       REMAINING_AMOUNT_VND = @REM,
                       STATUS_CODE = @STATUS,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                """, connection, fbTx))
            {
                updateDebt.Parameters.Add("@PAID", FbDbType.Decimal).Value = paid;
                updateDebt.Parameters.Add("@REM", FbDbType.Decimal).Value = newRemaining;
                updateDebt.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = newStatus;
                updateDebt.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                updateDebt.Parameters.Add("@ID", FbDbType.BigInt).Value = request.DebtId;
                await updateDebt.ExecuteNonQueryAsync(cancellationToken);
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PAYABLE_DEBT_PAID",
                session.UserId, session.Username, session.BranchId,
                "PAYABLE_DEBT", request.DebtId.ToString(),
                $"{{\"paid\":{paid},\"remaining\":{newRemaining}}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
