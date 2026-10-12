using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Warehouse;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Warehouse;

// Dapper: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record (vd. ID AS Id).
// Giữ using FirebirdSql vì vẫn cần cast (FbTransaction) cho StockPostingSupport.GetCompanyIdAsync.
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

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var search = (searchText ?? string.Empty).Trim();
        var rows = await connection.QueryAsync<ExternalWorkerListItem>(
            new CommandDefinition("""
                SELECT ID AS Id, CODE AS Code, NAME AS Name,
                       COALESCE(PHONE, '') AS Phone, COALESCE(ADDRESS, '') AS Address,
                       COALESCE(ID_TAX_NUMBER, '') AS IdTaxNumber, COALESCE(PAYMENT_INFO, '') AS PaymentInfo,
                       COALESCE(NOTES, '') AS Notes, IS_ACTIVE AS IsActive
                FROM EXTERNAL_WORKERS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
                  AND (@HAS_SEARCH = 0
                       OR CAST(CODE AS VARCHAR(40) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR NAME CONTAINING @SEARCH
                       OR COALESCE(PHONE, '') CONTAINING @SEARCH)
                ORDER BY NAME, CODE
                """,
                new
                {
                    ACTIVE_ONLY = activeOnly ? 1 : 0,
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
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
            // Param chung của UPDATE/INSERT (bản ADO.NET cũ bind trong helper Bind):
            // giữ nguyên ngữ nghĩa — chuỗi null -> DBNull.Value, @BY null khi UserId <= 0.
            var shared = new
            {
                CID = companyId,
                CODE = code,
                NAME = name,
                PHONE = (object?)request.Phone?.Trim() ?? DBNull.Value,
                ADDR = (object?)request.Address?.Trim() ?? DBNull.Value,
                TAX = (object?)request.IdTaxNumber?.Trim() ?? DBNull.Value,
                PAY = (object?)request.PaymentInfo?.Trim() ?? DBNull.Value,
                NOTES = (object?)request.Notes?.Trim() ?? DBNull.Value,
                ACTIVE = request.IsActive,
                BY = session.UserId > 0 ? (object)session.UserId : DBNull.Value
            };

            long id;
            if (request.Id is > 0)
            {
                id = request.Id.Value;
                var affected = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE EXTERNAL_WORKERS
                           SET CODE = @CODE, NAME = @NAME, PHONE = @PHONE, ADDRESS = @ADDR,
                               ID_TAX_NUMBER = @TAX, PAYMENT_INFO = @PAY, NOTES = @NOTES,
                               IS_ACTIVE = @ACTIVE, UPDATED_BY = @BY, UPDATED_AT = CURRENT_TIMESTAMP
                         WHERE ID = @ID AND COMPANY_ID = @CID
                        """,
                        new
                        {
                            shared.CID,
                            shared.CODE,
                            shared.NAME,
                            shared.PHONE,
                            shared.ADDR,
                            shared.TAX,
                            shared.PAY,
                            shared.NOTES,
                            shared.ACTIVE,
                            shared.BY,
                            ID = id
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                if (affected != 1)
                    throw new InvalidOperationException("Không cập nhật được thợ ngoài.");
            }
            else
            {
                id = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("""
                        INSERT INTO EXTERNAL_WORKERS
                            (COMPANY_ID, CREATED_BRANCH_ID, CODE, NAME, PHONE, ADDRESS,
                             ID_TAX_NUMBER, PAYMENT_INFO, NOTES, IS_ACTIVE, CREATED_BY, CREATED_AT)
                        VALUES
                            (@CID, @BID, @CODE, @NAME, @PHONE, @ADDR,
                             @TAX, @PAY, @NOTES, @ACTIVE, @BY, CURRENT_TIMESTAMP)
                        RETURNING ID
                        """,
                        new
                        {
                            shared.CID,
                            BID = session.BranchId,
                            shared.CODE,
                            shared.NAME,
                            shared.PHONE,
                            shared.ADDR,
                            shared.TAX,
                            shared.PAY,
                            shared.NOTES,
                            shared.ACTIVE,
                            shared.BY
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            return (await GetByIdAsync(id, session, cancellationToken))!;
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { /* swallow */ }
            throw;
        }
    }
}
