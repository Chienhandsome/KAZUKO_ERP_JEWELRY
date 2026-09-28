using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

public sealed class FirebirdExternalWorkerRepository : IExternalWorkerRepository
{
    public async Task<IReadOnlyList<ExternalWorkerListItem>> SearchAsync(
        string searchText,
        bool activeOnly,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var result = new List<ExternalWorkerListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, NAME, COALESCE(PHONE, ''), COALESCE(ADDRESS, ''),
                   COALESCE(ID_TAX_NUMBER, ''), COALESCE(PAYMENT_INFO, ''), COALESCE(NOTES, ''), IS_ACTIVE
            FROM EXTERNAL_WORKERS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
              AND (@HAS_SEARCH = 0
                   OR CAST(CODE AS VARCHAR(40) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR NAME CONTAINING @SEARCH
                   OR COALESCE(PHONE, '') CONTAINING @SEARCH)
            ORDER BY NAME, CODE
            """, connection);
        var search = (searchText ?? string.Empty).Trim();
        command.Parameters.Add("@ACTIVE_ONLY", FbDbType.Integer).Value = activeOnly ? 1 : 0;
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ExternalWorkerListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetBoolean(8)));
        }

        return result;
    }

    public async Task<ExternalWorkerListItem?> GetByIdAsync(
        long id,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        var rows = await SearchAsync(string.Empty, activeOnly: false, session, cancellationToken);
        return rows.FirstOrDefault(x => x.Id == id);
    }

    public async Task<ExternalWorkerListItem> SaveAsync(
        ExternalWorkerSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(session);
        StockPostingSupport.EnsureBranch(session);

        var code = (request.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (request.Name ?? string.Empty).Trim();
        if (code.Length == 0 || name.Length == 0)
            throw new InvalidOperationException("Mã và tên thợ ngoài bắt buộc.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var fbTx = (FbTransaction)transaction;

        try
        {
            var companyId = await StockPostingSupport.GetCompanyIdAsync(connection, fbTx, cancellationToken);
            long id;
            if (request.Id is > 0)
            {
                id = request.Id.Value;
                await using var update = new FbCommand("""
                    UPDATE EXTERNAL_WORKERS
                       SET CODE = @CODE, NAME = @NAME, PHONE = @PHONE, ADDRESS = @ADDR,
                           ID_TAX_NUMBER = @TAX, PAYMENT_INFO = @PAY, NOTES = @NOTES,
                           IS_ACTIVE = @ACTIVE, UPDATED_BY = @BY, UPDATED_AT = CURRENT_TIMESTAMP
                     WHERE ID = @ID AND COMPANY_ID = @CID
                    """, connection, fbTx);
                Bind(update, companyId, code, name, request, session);
                update.Parameters.Add("@ID", FbDbType.BigInt).Value = id;
                if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Không cập nhật được thợ ngoài.");
            }
            else
            {
                await using var insert = new FbCommand("""
                    INSERT INTO EXTERNAL_WORKERS
                        (COMPANY_ID, CREATED_BRANCH_ID, CODE, NAME, PHONE, ADDRESS,
                         ID_TAX_NUMBER, PAYMENT_INFO, NOTES, IS_ACTIVE, CREATED_BY, CREATED_AT)
                    VALUES
                        (@CID, @BID, @CODE, @NAME, @PHONE, @ADDR,
                         @TAX, @PAY, @NOTES, @ACTIVE, @BY, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """, connection, fbTx);
                Bind(insert, companyId, code, name, request, session);
                insert.Parameters.Add("@BID", FbDbType.BigInt).Value = session.BranchId;
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return (await GetByIdAsync(id, session, cancellationToken))!;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void Bind(
        FbCommand command,
        long companyId,
        string code,
        string name,
        ExternalWorkerSaveRequest request,
        AuthenticatedSession session)
    {
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = companyId;
        command.Parameters.Add("@CODE", FbDbType.VarChar, 30).Value = code;
        command.Parameters.Add("@NAME", FbDbType.VarChar, 200).Value = name;
        command.Parameters.Add("@PHONE", FbDbType.VarChar, 40).Value =
            (object?)request.Phone?.Trim() ?? DBNull.Value;
        command.Parameters.Add("@ADDR", FbDbType.VarChar, 500).Value =
            (object?)request.Address?.Trim() ?? DBNull.Value;
        command.Parameters.Add("@TAX", FbDbType.VarChar, 40).Value =
            (object?)request.IdTaxNumber?.Trim() ?? DBNull.Value;
        command.Parameters.Add("@PAY", FbDbType.VarChar, 500).Value =
            (object?)request.PaymentInfo?.Trim() ?? DBNull.Value;
        command.Parameters.Add("@NOTES", FbDbType.VarChar, 1000).Value =
            (object?)request.Notes?.Trim() ?? DBNull.Value;
        command.Parameters.Add("@ACTIVE", FbDbType.Boolean).Value = request.IsActive;
        command.Parameters.Add("@BY", FbDbType.BigInt).Value =
            session.UserId > 0 ? session.UserId : DBNull.Value;
    }
}
