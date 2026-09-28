using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdReceivableDebtRepository : IReceivableDebtRepository
{
    private readonly FirebirdFinancialSettlementService _settlements = new();

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

            string partyType;
            long partyId;
            decimal remaining;
            string status;
            await using (var debtCmd = new FbCommand("""
                SELECT PARTY_TYPE, COALESCE(CUSTOMER_ID, EXTERNAL_WORKER_ID),
                       REMAINING_AMOUNT_VND, STATUS_CODE
                FROM RECEIVABLE_DEBTS
                WHERE ID = @ID AND COMPANY_ID = @CID AND BRANCH_ID = @BID
                WITH LOCK
                """, connection, fbTx))
            {
                debtCmd.Parameters.Add("@ID", FbDbType.BigInt).Value = request.DebtId;
                debtCmd.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                debtCmd.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                await using var reader = await debtCmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Không tìm thấy khoản phải thu.");
                partyType = reader.GetString(0);
                partyId = reader.GetInt64(1);
                remaining = reader.GetDecimal(2);
                status = reader.GetString(3);
            }

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

            await using (var insertCol = new FbCommand("""
                INSERT INTO RECEIVABLE_DEBT_COLLECTIONS
                    (DEBT_ID, COLLECTION_DATE, AMOUNT_VND, SETTLEMENT_ID, NOTES, CREATED_BY, CREATED_AT)
                VALUES
                    (@DEBT, CURRENT_TIMESTAMP, @AMT, @SID, @NOTES, @BY, CURRENT_TIMESTAMP)
                """, connection, fbTx))
            {
                insertCol.Parameters.Add("@DEBT", FbDbType.BigInt).Value = request.DebtId;
                insertCol.Parameters.Add("@AMT", FbDbType.Decimal).Value = amount;
                insertCol.Parameters.Add("@SID", FbDbType.BigInt).Value = settlement.SettlementId;
                insertCol.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    (object?)request.Notes ?? DBNull.Value;
                insertCol.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                await insertCol.ExecuteNonQueryAsync(cancellationToken);
            }

            var newRemaining = remaining - amount;
            var newStatus = newRemaining == 0 ? ReceivableDebtStatuses.Paid : ReceivableDebtStatuses.Partial;
            await using (var update = new FbCommand("""
                UPDATE RECEIVABLE_DEBTS
                   SET COLLECTED_AMOUNT_VND = COLLECTED_AMOUNT_VND + @AMT,
                       REMAINING_AMOUNT_VND = @REM,
                       STATUS_CODE = @STATUS,
                       UPDATED_BY = @BY,
                       UPDATED_AT = CURRENT_TIMESTAMP
                 WHERE ID = @ID
                """, connection, fbTx))
            {
                update.Parameters.Add("@AMT", FbDbType.Decimal).Value = amount;
                update.Parameters.Add("@REM", FbDbType.Decimal).Value = newRemaining;
                update.Parameters.Add("@STATUS", FbDbType.VarChar, 20).Value = newStatus;
                update.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = request.DebtId;
                await update.ExecuteNonQueryAsync(cancellationToken);
            }

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
