using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdReceivableDebtRepository : IReceivableDebtRepository
{
    private readonly FirebirdFinancialSettlementService _settlements = new();

    private sealed record DebtLockRow(
        string PartyType,
        long PartyId,
        decimal RemainingAmount,
        string StatusCode);

    public async Task CollectAsync(
        ReceivableDebtCollectRequest request,
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
            throw new InvalidOperationException("Số thu phải > 0.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);

            var debt = await connection.QuerySingleOrDefaultAsync<DebtLockRow>(
                new CommandDefinition("""
                    -- SUPPLIER_ID: migration 73 — NCC cũng có thể là party của khoản phải thu.
                    SELECT PARTY_TYPE AS PartyType, COALESCE(CUSTOMER_ID, EXTERNAL_WORKER_ID, SUPPLIER_ID) AS PartyId,
                           REMAINING_AMOUNT_VND AS RemainingAmount, STATUS_CODE AS StatusCode
                    FROM RECEIVABLE_DEBTS
                    WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                    WITH LOCK
                    """,
                    new
                    {
                        ID = request.DebtId,
                        CID = companyId,
                        BID = session.BranchId
                    },
                    transaction,
                    cancellationToken: cancellationToken));
            if (debt is null)
                throw new InvalidOperationException("Không tìm thấy khoản phải thu.");

            var partyType = debt.PartyType;
            var partyId = debt.PartyId;
            var remaining = debt.RemainingAmount;
            var status = debt.StatusCode;

            if (status is ReceivableDebtStatuses.Paid or ReceivableDebtStatuses.Cancelled)
                throw new InvalidOperationException("Khoản phải thu đã đóng.");
            if (amount > remaining)
                throw new InvalidOperationException("Số thu vượt số còn phải thu.");

            var settlement = await _settlements.PostInTransactionAsync(
                connection, fbTx, companyId, session.BranchId,
                new FinancialSettlementDraft(
                    SettlementTypes.Receipt,
                    partyType,
                    partyId,
                    DateTime.Now,
                    allocations,
                    [new SettlementApplicationDraft(
                        SettlementApplicationTargetTypes.ReceivableDebt, request.DebtId, amount)],
                    request.Notes),
                session.UserId,
                cancellationToken);

            await connection.ExecuteAsync(
                new CommandDefinition("""
                    INSERT INTO RECEIVABLE_DEBT_COLLECTIONS
                        (DEBT_ID, COLLECTION_DATE, AMOUNT_VND, SETTLEMENT_ID, NOTES, CREATED_BY, CREATED_AT)
                    VALUES
                        (@DEBT, CURRENT_TIMESTAMP, @AMT, @SID, @NOTES, @BY, CURRENT_TIMESTAMP)
                    """,
                    new
                    {
                        DEBT = request.DebtId,
                        AMT = amount,
                        SID = settlement.SettlementId,
                        NOTES = (object?)request.Notes ?? DBNull.Value,
                        BY = (object)(session.UserId > 0 ? session.UserId : DBNull.Value)
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            var newRemaining = remaining - amount;
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
                        AMT = amount,
                        REM = newRemaining,
                        STATUS = newStatus,
                        BY = (object)(session.UserId > 0 ? session.UserId : DBNull.Value),
                        ID = request.DebtId
                    },
                    transaction,
                    cancellationToken: cancellationToken));

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "RECEIVABLE_DEBT_COLLECTED",
                session.UserId, session.Username, session.BranchId,
                "RECEIVABLE_DEBT", request.DebtId.ToString(),
                $"{{\"settlementId\":{settlement.SettlementId},\"amount\":{amount}}}",
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
