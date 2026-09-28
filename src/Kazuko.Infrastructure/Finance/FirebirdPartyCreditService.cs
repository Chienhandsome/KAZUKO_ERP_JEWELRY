using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdPartyCreditService : IPartyCreditService
{
    public async Task<PartyCreditResult> CreateAsync(
        PartyCreditDraft draft,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var partyType = (draft.PartyType ?? string.Empty).Trim().ToUpperInvariant();
        if (partyType is not (SettlementPartyTypes.Supplier or SettlementPartyTypes.Customer))
            throw new InvalidOperationException("PARTY_TYPE credit phải là SUPPLIER hoặc CUSTOMER.");
        if (draft.PartyId <= 0)
            throw new InvalidOperationException("PARTY_ID không hợp lệ.");
        if (draft.AmountVnd <= 0)
            throw new InvalidOperationException("Số credit phải > 0.");

        var kind = (draft.CreditKind ?? string.Empty).Trim().ToUpperInvariant();
        if (kind is not (
            PartyCreditKinds.SupplierReturnHold or
            PartyCreditKinds.CustomerReturnHold or
            PartyCreditKinds.ManualAdjust))
            throw new InvalidOperationException("CREDIT_KIND không hợp lệ.");

        // Guard: credit must never be confused with cash voucher posting.
        if (kind.Contains("CASH", StringComparison.Ordinal) || kind.Contains("VOUCHER", StringComparison.Ordinal))
            throw new InvalidOperationException("Credit đối tác không được ghi dưới dạng phiếu thu/chi.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            var code = await StockPostingSupport.NextDocumentCodeAsync(
                connection, fbTx, "PARTY_CREDIT", session.BranchId, "CR", cancellationToken);

            long creditId;
            await using (var insert = new FbCommand("""
                INSERT INTO PARTY_CREDITS
                    (COMPANY_ID, BRANCH_ID, CODE, PARTY_TYPE, PARTY_ID, CREDIT_KIND,
                     ORIGINAL_AMOUNT_VND, USED_AMOUNT_VND, REMAINING_AMOUNT_VND,
                     ORIGIN_DOCUMENT_TYPE, ORIGIN_DOCUMENT_ID, STATUS_CODE, NOTES, CREATED_BY)
                VALUES
                    (@CID, @BID, @CODE, @PTYPE, @PID, @KIND,
                     @AMT, 0, @AMT, @OTYPE, @OID, 'OPEN', @NOTES, @BY)
                RETURNING ID
                """, connection, fbTx))
            {
                insert.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
                insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                insert.Parameters.Add("@CODE", FbDbType.VarChar, 40).Value = code;
                insert.Parameters.Add("@PTYPE", FbDbType.VarChar, 30).Value = partyType;
                insert.Parameters.Add("@PID", FbDbType.BigInt).Value = draft.PartyId;
                insert.Parameters.Add("@KIND", FbDbType.VarChar, 40).Value = kind;
                insert.Parameters.Add("@AMT", FbDbType.Decimal).Value = draft.AmountVnd;
                insert.Parameters.Add("@OTYPE", FbDbType.VarChar, 40).Value = draft.OriginDocumentType.Trim();
                insert.Parameters.Add("@OID", FbDbType.BigInt).Value = draft.OriginDocumentId;
                insert.Parameters.Add("@NOTES", FbDbType.VarChar, 500).Value =
                    (object?)draft.Notes ?? DBNull.Value;
                insert.Parameters.Add("@BY", FbDbType.BigInt).Value =
                    session.UserId > 0 ? session.UserId : DBNull.Value;
                creditId = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            await AuditLogWriter.WriteAsync(
                connection, fbTx, "PARTY_CREDIT_CREATED",
                session.UserId, session.Username, session.BranchId,
                "PARTY_CREDIT", creditId.ToString(),
                $"{{\"partyType\":\"{partyType}\",\"amount\":{draft.AmountVnd},\"kind\":\"{kind}\",\"cashVoucher\":false}}",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new PartyCreditResult(creditId, code);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
