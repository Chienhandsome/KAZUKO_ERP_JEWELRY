using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdFinancialVoucherPostingService : IFinancialVoucherPostingService
{
    public Task<FinancialVoucherPostResult> PostReceiptAsync(
        FinancialVoucherPostRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        PostStandaloneAsync(FinancialVoucherTypes.In, request, session, cancellationToken);

    public Task<FinancialVoucherPostResult> PostPaymentAsync(
        FinancialVoucherPostRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default) =>
        PostStandaloneAsync(FinancialVoucherTypes.Out, request, session, cancellationToken);

    /// <summary>
    /// Posts a phiếu thu inside an existing Firebird transaction (no nested commit).
    /// </summary>
    public Task<FinancialVoucherPostResult> PostReceiptInTransactionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        FinancialVoucherPostRequest request,
        long? createdBy,
        CancellationToken cancellationToken = default) =>
        PostInTransactionAsync(
            connection, transaction, companyId, branchId,
            FinancialVoucherTypes.In, request, createdBy, cancellationToken);

    /// <summary>
    /// Posts a phiếu chi inside an existing Firebird transaction (no nested commit).
    /// </summary>
    public Task<FinancialVoucherPostResult> PostPaymentInTransactionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        FinancialVoucherPostRequest request,
        long? createdBy,
        CancellationToken cancellationToken = default) =>
        PostInTransactionAsync(
            connection, transaction, companyId, branchId,
            FinancialVoucherTypes.Out, request, createdBy, cancellationToken);

    private async Task<FinancialVoucherPostResult> PostStandaloneAsync(
        string voucherType,
        FinancialVoucherPostRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var result = await PostInTransactionAsync(
                connection, fbTx, companyId, session.BranchId,
                voucherType, request, session.UserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<FinancialVoucherPostResult> PostInTransactionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        string voucherType,
        FinancialVoucherPostRequest request,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Allocation);

        ValidateRequest(request);

        var existing = await TryFindExistingAsync(
            connection, transaction, companyId,
            request.SourceAllocationType, request.SourceAllocationId,
            cancellationToken);
        if (existing is not null)
            return existing with { AlreadyExisted = true };

        var allocation = request.Allocation;
        var method = allocation.PaymentMethod.Trim().ToUpperInvariant();
        await ValidateFundOrBankAsync(
            connection, transaction, companyId, branchId, method,
            allocation.CashFundId, allocation.BankAccountId, cancellationToken);

        var isIn = voucherType == FinancialVoucherTypes.In;
        var sequenceType = isIn ? "CASH_VOUCHER_IN" : "CASH_VOUCHER_OUT";
        var prefix = isIn ? "PT" : "PC";
        var code = await StockPostingSupport.NextDocumentCodeAsync(
            connection, transaction, sequenceType, branchId, prefix, cancellationToken);

        try
        {
            await using var command = new FbCommand("""
                INSERT INTO CASH_VOUCHERS
                    (COMPANY_ID, BRANCH_ID, CODE, VOUCHER_DATE, VOUCHER_TYPE, PAYMENT_METHOD,
                     AMOUNT_VND, CURRENCY_CODE, EXCHANGE_RATE, CASH_FUND_ID, BANK_ACCOUNT_ID,
                     REFERENCE_NUMBER, NOTES, SOURCE_DOCUMENT_TYPE, SOURCE_DOCUMENT_ID,
                     SOURCE_ALLOCATION_TYPE, SOURCE_ALLOCATION_ID, STATUS_CODE, CREATED_BY)
                VALUES
                    (@CID, @BID, @CODE, CURRENT_TIMESTAMP, @VTYPE, @METHOD,
                     @AMT, 'VND', 1, @FUND, @BANK,
                     @REF, @NOTES, @SRC_TYPE, @SRC_ID,
                     @ALLOC_TYPE, @ALLOC_ID, 'POSTED', @BY)
                RETURNING ID
                """, connection, transaction);
            command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            command.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
            command.Parameters.Add("@VTYPE", FbDbType.VarChar, 10).Value = voucherType;
            command.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value = method;
            command.Parameters.Add("@AMT", FbDbType.Decimal).Value = allocation.AmountVnd;
            command.Parameters.Add("@FUND", FbDbType.BigInt).Value =
                allocation.CashFundId is > 0 ? allocation.CashFundId.Value : DBNull.Value;
            command.Parameters.Add("@BANK", FbDbType.BigInt).Value =
                allocation.BankAccountId is > 0 ? allocation.BankAccountId.Value : DBNull.Value;
            command.Parameters.Add("@REF", FbDbType.VarChar, 100).Value =
                (object?)allocation.ReferenceNumber ?? DBNull.Value;
            command.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                (object?)request.Notes ?? DBNull.Value;
            command.Parameters.Add("@SRC_TYPE", FbDbType.VarChar, 40).Value = request.SourceDocumentType.Trim();
            command.Parameters.Add("@SRC_ID", FbDbType.BigInt).Value = request.SourceDocumentId;
            command.Parameters.Add("@ALLOC_TYPE", FbDbType.VarChar, 40).Value =
                request.SourceAllocationType.Trim();
            command.Parameters.Add("@ALLOC_ID", FbDbType.BigInt).Value = request.SourceAllocationId;
            command.Parameters.Add("@BY", FbDbType.BigInt).Value =
                createdBy is > 0 ? createdBy.Value : DBNull.Value;

            var id = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            return new FinancialVoucherPostResult(id, code, voucherType, AlreadyExisted: false);
        }
        catch (FbException ex) when (IsUniqueViolation(ex))
        {
            var raced = await TryFindExistingAsync(
                connection, transaction, companyId,
                request.SourceAllocationType, request.SourceAllocationId,
                cancellationToken);
            if (raced is not null)
                return raced with { AlreadyExisted = true };
            throw;
        }
    }

    private static void ValidateRequest(FinancialVoucherPostRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceDocumentType))
            throw new InvalidOperationException("Thiếu SourceDocumentType cho phiếu thu/chi.");
        if (request.SourceDocumentId <= 0)
            throw new InvalidOperationException("SourceDocumentId không hợp lệ.");
        if (string.IsNullOrWhiteSpace(request.SourceAllocationType))
            throw new InvalidOperationException("Thiếu SourceAllocationType (idempotency).");
        if (request.SourceAllocationId <= 0)
            throw new InvalidOperationException("SourceAllocationId không hợp lệ.");

        var errors = PaymentAllocationValidator.Validate(
            [request.Allocation], request.Allocation.AmountVnd);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static async Task ValidateFundOrBankAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        string method,
        long? cashFundId,
        long? bankAccountId,
        CancellationToken cancellationToken)
    {
        if (method == PaymentMethods.Cash)
        {
            await using var command = new FbCommand("""
                SELECT COUNT(*)
                FROM CASH_FUNDS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID AND IS_ACTIVE = TRUE
                """, connection, transaction);
            command.Parameters.Add("@ID", FbDbType.BigInt).Value = cashFundId!.Value;
            command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
                throw new InvalidOperationException("Quỹ tiền mặt không hợp lệ hoặc không thuộc chi nhánh.");
            return;
        }

        if (method is PaymentMethods.BankTransfer or PaymentMethods.Card)
        {
            await using var command = new FbCommand("""
                SELECT COUNT(*)
                FROM BANK_ACCOUNTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID AND IS_ACTIVE = TRUE
                """, connection, transaction);
            command.Parameters.Add("@ID", FbDbType.BigInt).Value = bankAccountId!.Value;
            command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
            command.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
                throw new InvalidOperationException("Tài khoản ngân hàng không hợp lệ hoặc không thuộc chi nhánh.");
        }
    }

    private static async Task<FinancialVoucherPostResult?> TryFindExistingAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        string sourceAllocationType,
        long sourceAllocationId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT ID, CODE, VOUCHER_TYPE
            FROM CASH_VOUCHERS
            WHERE COMPANY_ID = @CID
              AND SOURCE_ALLOCATION_TYPE = @ATYPE
              AND SOURCE_ALLOCATION_ID = @AID
              AND STATUS_CODE = 'POSTED'
            """, connection, transaction);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@ATYPE", FbDbType.VarChar, 40).Value = sourceAllocationType.Trim();
        command.Parameters.Add("@AID", FbDbType.BigInt).Value = sourceAllocationId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new FinancialVoucherPostResult(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            AlreadyExisted: true);
    }

    private static bool IsUniqueViolation(FbException ex) =>
        ex.ErrorCode == 335544665
        || (ex.Message?.Contains("UQ_CV_SOURCE_ALLOC", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.Message?.Contains("violation of PRIMARY or UNIQUE KEY", StringComparison.OrdinalIgnoreCase) ?? false);
}
