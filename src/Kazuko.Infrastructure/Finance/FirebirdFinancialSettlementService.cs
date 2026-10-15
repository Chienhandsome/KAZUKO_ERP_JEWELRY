using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdFinancialSettlementService : IFinancialSettlementService
{
    private readonly FirebirdFinancialVoucherPostingService _vouchers = new();

    public async Task<FinancialSettlementResult> PostAsync(
        FinancialSettlementDraft draft,
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
            var result = await PostInTransactionAsync(
                connection, fbTx, companyId, session.BranchId, draft, session.UserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Posts settlement + allocation vouchers inside an existing transaction.
    /// Callers are responsible for debt side-effects (ORDER_PAYMENTS / PAYABLE updates).
    /// </summary>
    public async Task<FinancialSettlementResult> PostInTransactionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        FinancialSettlementDraft draft,
        long? createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(draft);

        ValidateDraft(draft);

        var settlementType = draft.SettlementType.Trim().ToUpperInvariant();
        var (settlementId, code) = await InsertSettlementHeaderAsync(
            connection, transaction, companyId, branchId, draft,
            settlementType, createdBy, cancellationToken);

        var voucherIds = new List<long>();
        var line = 0;
        foreach (var allocation in draft.Allocations)
        {
            line++;
            var allocationId = await InsertAllocationRowAsync(
                connection, transaction, settlementId, line, allocation, cancellationToken);

            var voucherRequest = new FinancialVoucherPostRequest(
                allocation,
                "FINANCIAL_SETTLEMENT",
                settlementId,
                FinancialVoucherSourceAllocationTypes.SettlementAllocation,
                allocationId,
                draft.Notes);

            var voucher = settlementType == SettlementTypes.Receipt
                ? await _vouchers.PostReceiptInTransactionAsync(
                    connection, transaction, companyId, branchId, voucherRequest, createdBy, cancellationToken)
                : await _vouchers.PostPaymentInTransactionAsync(
                    connection, transaction, companyId, branchId, voucherRequest, createdBy, cancellationToken);

            await LinkAllocationVoucherAsync(
                connection, transaction, allocationId, voucher.VoucherId, cancellationToken);

            voucherIds.Add(voucher.VoucherId);
        }

        var applicationIds = await InsertApplicationRowsAsync(
            connection, transaction, settlementId, draft.Applications, cancellationToken);

        return new FinancialSettlementResult(settlementId, code, voucherIds, applicationIds);
    }

    /// <summary>
    /// Posts a "thu/chi toàn bộ" settlement: ONE settlement + M allocation rows
    /// (one per payment method) + exactly ONE cash voucher for the total +
    /// N application rows (one per target debt), all in the caller's transaction.
    /// Debt side-effects (ORDER_PAYMENTS / PAYABLE / RECEIVABLE updates) stay with the caller.
    /// </summary>
    public async Task<FinancialSettlementResult> PostBulkSettlementInTransactionAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        FinancialSettlementDraft draft,
        long? createdBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(draft);

        ValidateDraft(draft);

        var settlementType = draft.SettlementType.Trim().ToUpperInvariant();
        var (settlementId, code) = await InsertSettlementHeaderAsync(
            connection, transaction, companyId, branchId, draft,
            settlementType, createdBy, cancellationToken);

        var line = 0;
        var allocationIds = new List<long>();
        foreach (var allocation in draft.Allocations)
        {
            line++;
            allocationIds.Add(await InsertAllocationRowAsync(
                connection, transaction, settlementId, line, allocation, cancellationToken));
        }

        var voucherType = settlementType == SettlementTypes.Receipt
            ? FinancialVoucherTypes.In
            : FinancialVoucherTypes.Out;
        var voucher = await _vouchers.PostBulkInTransactionAsync(
            connection, transaction, companyId, branchId, voucherType,
            draft.Allocations,
            "FINANCIAL_SETTLEMENT",
            settlementId,
            FinancialVoucherSourceAllocationTypes.SettlementBulk,
            settlementId,
            draft.Notes,
            createdBy,
            cancellationToken);

        foreach (var allocationId in allocationIds)
        {
            await LinkAllocationVoucherAsync(
                connection, transaction, allocationId, voucher.VoucherId, cancellationToken);
        }

        var applicationIds = await InsertApplicationRowsAsync(
            connection, transaction, settlementId, draft.Applications, cancellationToken);

        return new FinancialSettlementResult(
            settlementId, code, [voucher.VoucherId], applicationIds);
    }

    private static async Task<(long SettlementId, string Code)> InsertSettlementHeaderAsync(
        FbConnection connection,
        FbTransaction transaction,
        long companyId,
        long branchId,
        FinancialSettlementDraft draft,
        string settlementType,
        long? createdBy,
        CancellationToken cancellationToken)
    {
        var total = draft.Allocations.Sum(x => x.AmountVnd);
        var partyType = draft.PartyType.Trim().ToUpperInvariant();
        var code = await StockPostingSupport.NextDocumentCodeAsync(
            connection, transaction, "FINANCIAL_SETTLEMENT", branchId, "TT", cancellationToken);

        await using var insert = new FbCommand("""
            INSERT INTO FINANCIAL_SETTLEMENTS
                (COMPANY_ID, BRANCH_ID, CODE, SETTLEMENT_TYPE, PARTY_TYPE, PARTY_ID,
                 SETTLEMENT_DATE, TOTAL_AMOUNT_VND, NOTES, STATUS_CODE, CREATED_BY)
            VALUES
                (@CID, @BID, @CODE, @STYPE, @PTYPE, @PID,
                 @SDATE, @TOTAL, @NOTES, 'POSTED', @BY)
            RETURNING ID
            """, connection, transaction);
        insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        insert.Parameters.Add("@BID", FbDbType.BigInt).Value = branchId;
        insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
        insert.Parameters.Add("@STYPE", FbDbType.VarChar, 20).Value = settlementType;
        insert.Parameters.Add("@PTYPE", FbDbType.VarChar, 30).Value = partyType;
        insert.Parameters.Add("@PID", FbDbType.BigInt).Value = draft.PartyId;
        insert.Parameters.Add("@SDATE", FbDbType.TimeStamp).Value = draft.SettlementDate;
        insert.Parameters.Add("@TOTAL", FbDbType.Decimal).Value = total;
        insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
            (object?)draft.Notes ?? DBNull.Value;
        insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
            createdBy is > 0 ? createdBy.Value : DBNull.Value;
        var settlementId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
        return (settlementId, code);
    }

    private static async Task<long> InsertAllocationRowAsync(
        FbConnection connection,
        FbTransaction transaction,
        long settlementId,
        int lineNumber,
        PaymentAllocationDraft allocation,
        CancellationToken cancellationToken)
    {
        await using var insertAlloc = new FbCommand("""
            INSERT INTO FINANCIAL_SETTLEMENT_ALLOCATIONS
                (SETTLEMENT_ID, LINE_NUMBER, PAYMENT_METHOD, AMOUNT_VND,
                 CASH_FUND_ID, BANK_ACCOUNT_ID, REFERENCE_NUMBER)
            VALUES
                (@SID, @LN, @METHOD, @AMT, @FUND, @BANK, @REF)
            RETURNING ID
            """, connection, transaction);
        insertAlloc.Parameters.Add("@SID", FbDbType.BigInt).Value = settlementId;
        insertAlloc.Parameters.Add("@LN", FbDbType.Integer).Value = lineNumber;
        insertAlloc.Parameters.Add("@METHOD", FbDbType.VarChar, 20).Value =
            allocation.PaymentMethod.Trim().ToUpperInvariant();
        insertAlloc.Parameters.Add("@AMT", FbDbType.Decimal).Value = allocation.AmountVnd;
        insertAlloc.Parameters.Add("@FUND", FbDbType.BigInt).Value =
            allocation.CashFundId is > 0 ? allocation.CashFundId.Value : DBNull.Value;
        insertAlloc.Parameters.Add("@BANK", FbDbType.BigInt).Value =
            allocation.BankAccountId is > 0 ? allocation.BankAccountId.Value : DBNull.Value;
        insertAlloc.Parameters.Add("@REF", FbDbType.VarChar, 100).Value =
            (object?)allocation.ReferenceNumber ?? DBNull.Value;
        return Convert.ToInt64(await insertAlloc.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task LinkAllocationVoucherAsync(
        FbConnection connection,
        FbTransaction transaction,
        long allocationId,
        long voucherId,
        CancellationToken cancellationToken)
    {
        await using var updateAlloc = new FbCommand("""
            UPDATE FINANCIAL_SETTLEMENT_ALLOCATIONS
               SET CASH_VOUCHER_ID = @VID
             WHERE ID = @ID
            """, connection, transaction);
        updateAlloc.Parameters.Add("@VID", FbDbType.BigInt).Value = voucherId;
        updateAlloc.Parameters.Add("@ID", FbDbType.BigInt).Value = allocationId;
        await updateAlloc.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<long>> InsertApplicationRowsAsync(
        FbConnection connection,
        FbTransaction transaction,
        long settlementId,
        IReadOnlyList<SettlementApplicationDraft> applications,
        CancellationToken cancellationToken)
    {
        var applicationIds = new List<long>();
        foreach (var application in applications)
        {
            await using var insertApp = new FbCommand("""
                INSERT INTO FINANCIAL_SETTLEMENT_APPLICATIONS
                    (SETTLEMENT_ID, TARGET_TYPE, TARGET_ID, AMOUNT_VND)
                VALUES
                    (@SID, @TTYPE, @TID, @AMT)
                RETURNING ID
                """, connection, transaction);
            insertApp.Parameters.Add("@SID", FbDbType.BigInt).Value = settlementId;
            insertApp.Parameters.Add("@TTYPE", FbDbType.VarChar, 40).Value =
                application.TargetType.Trim().ToUpperInvariant();
            insertApp.Parameters.Add("@TID", FbDbType.BigInt).Value = application.TargetId;
            insertApp.Parameters.Add("@AMT", FbDbType.Decimal).Value = application.AmountVnd;
            applicationIds.Add(Convert.ToInt64(await insertApp.ExecuteScalarAsync(cancellationToken)));
        }

        return applicationIds;
    }

    private static void ValidateDraft(FinancialSettlementDraft draft)
    {
        var type = (draft.SettlementType ?? string.Empty).Trim().ToUpperInvariant();
        if (type is not (SettlementTypes.Receipt or SettlementTypes.Payment))
            throw new InvalidOperationException("SETTLEMENT_TYPE phải là RECEIPT hoặc PAYMENT.");

        var party = (draft.PartyType ?? string.Empty).Trim().ToUpperInvariant();
        if (party is not (
            SettlementPartyTypes.Supplier or
            SettlementPartyTypes.Customer or
            SettlementPartyTypes.ExternalWorker))
            throw new InvalidOperationException("PARTY_TYPE không hợp lệ.");

        if (draft.PartyId <= 0)
            throw new InvalidOperationException("PARTY_ID không hợp lệ.");

        var allocations = draft.Allocations ?? Array.Empty<PaymentAllocationDraft>();
        var applications = draft.Applications ?? Array.Empty<SettlementApplicationDraft>();
        if (allocations.Count == 0)
            throw new InvalidOperationException("Settlement cần ít nhất một dòng thanh toán.");
        if (applications.Count == 0)
            throw new InvalidOperationException("Settlement cần ít nhất một dòng phân bổ công nợ.");

        var totalAlloc = allocations.Sum(x => x.AmountVnd);
        var totalApp = applications.Sum(x => x.AmountVnd);
        var errors = PaymentAllocationValidator.Validate(allocations, totalAlloc).ToList();
        if (totalAlloc != totalApp)
            errors.Add($"Tổng allocations ({totalAlloc:N0}) phải bằng tổng applications ({totalApp:N0}).");
        if (totalAlloc <= 0)
            errors.Add("Tổng số tiền settlement phải > 0.");

        var appLine = 0;
        foreach (var app in applications)
        {
            appLine++;
            if (app.AmountVnd <= 0)
                errors.Add($"Dòng phân bổ công nợ {appLine}: số tiền phải > 0.");
            var target = (app.TargetType ?? string.Empty).Trim().ToUpperInvariant();
            if (target is not (
                SettlementApplicationTargetTypes.SalesOrder or
                SettlementApplicationTargetTypes.PayableDebt or
                SettlementApplicationTargetTypes.ReceivableDebt))
                errors.Add($"Dòng phân bổ công nợ {appLine}: TARGET_TYPE không hợp lệ.");
            if (app.TargetId <= 0)
                errors.Add($"Dòng phân bổ công nợ {appLine}: TARGET_ID không hợp lệ.");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }
}
