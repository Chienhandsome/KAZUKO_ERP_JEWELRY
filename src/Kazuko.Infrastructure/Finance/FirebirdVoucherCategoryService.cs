using System.Text.Json;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Finance;
using Kazuko.Infrastructure.Data;
using Kazuko.Infrastructure.Warehouse;

namespace Kazuko.Infrastructure.Finance;

public sealed class FirebirdVoucherCategoryService : IVoucherCategoryService
{
    public async Task<IReadOnlyList<VoucherCategory>> ListAsync(string voucherType, bool includeInactive, AuthenticatedSession session, CancellationToken ct = default)
    {
        StockPostingSupport.EnsureBranch(session);
        var type = VoucherCategoryRules.NormalizeType(voucherType);
        await using var connection = FirebirdDatabase.CreateConnection(); await connection.OpenAsync(ct);
        var rows = await connection.QueryAsync<VoucherCategory>(new CommandDefinition("""
            SELECT C.ID AS Id, C.VOUCHER_TYPE AS VoucherType, C.CODE AS Code, C.NAME AS Name, C.IS_ACTIVE AS IsActive
            FROM VOUCHER_CATEGORIES C JOIN BRANCHES B ON B.COMPANY_ID=C.COMPANY_ID
            WHERE B.ID=@BranchId AND C.VOUCHER_TYPE=@Type AND (@IncludeInactive=1 OR C.IS_ACTIVE=TRUE)
            ORDER BY C.IS_ACTIVE DESC,C.NAME,C.ID
            """, new { session.BranchId, Type = type, IncludeInactive = includeInactive ? 1 : 0 }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<VoucherCategory> SaveAsync(VoucherCategorySaveRequest request, AuthenticatedSession session, CancellationToken ct = default)
    {
        StockPostingSupport.EnsureBranch(session);
        var input = VoucherCategoryRules.Normalize(request);
        await using var connection = FirebirdDatabase.CreateConnection(); await connection.OpenAsync(ct);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var companyId = await connection.QuerySingleAsync<long>(new CommandDefinition(
                "SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { session.BranchId }, transaction, cancellationToken: ct));
            // Serialize catalog changes within the company before duplicate checking.
            await connection.QuerySingleAsync<long>(new CommandDefinition("SELECT ID FROM COMPANIES WHERE ID=@Id WITH LOCK", new { Id = companyId }, transaction, cancellationToken: ct));
            VoucherCategory? before = null;
            if (input.Id is long existingId)
            {
                before = await connection.QuerySingleOrDefaultAsync<VoucherCategory>(new CommandDefinition("""
                    SELECT ID AS Id,VOUCHER_TYPE AS VoucherType,CODE AS Code,NAME AS Name,IS_ACTIVE AS IsActive
                    FROM VOUCHER_CATEGORIES WHERE ID=@Id AND COMPANY_ID=@CompanyId AND VOUCHER_TYPE=@VoucherType WITH LOCK
                    """, new { Id = existingId, CompanyId = companyId, input.VoucherType }, transaction, cancellationToken: ct));
                if (before is null) throw new InvalidOperationException("Không tìm thấy loại thu/chi trong doanh nghiệp hoặc sai hướng Thu/Chi.");
            }
            var duplicate = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
                SELECT COUNT(*) FROM VOUCHER_CATEGORIES
                WHERE COMPANY_ID=@CompanyId AND VOUCHER_TYPE=@VoucherType AND CODE=@Code AND (@Id IS NULL OR ID<>@Id)
                """, new { CompanyId = companyId, input.VoucherType, input.Code, input.Id }, transaction, cancellationToken: ct));
            if (duplicate > 0) throw new InvalidOperationException("Mã loại thu/chi đã tồn tại.");
            var parameters = new { input.Id, CompanyId = companyId, input.VoucherType, input.Code, input.Name, input.IsActive, By = session.UserId };
            long id;
            if (input.Id is long updateId)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE VOUCHER_CATEGORIES SET CODE=@Code,NAME=@Name,IS_ACTIVE=@IsActive,UPDATED_BY=@By,UPDATED_AT=CURRENT_TIMESTAMP
                    WHERE ID=@Id AND COMPANY_ID=@CompanyId AND VOUCHER_TYPE=@VoucherType
                    """, parameters, transaction, cancellationToken: ct));
                id = updateId;
            }
            else id = await connection.QuerySingleAsync<long>(new CommandDefinition("""
                INSERT INTO VOUCHER_CATEGORIES(COMPANY_ID,VOUCHER_TYPE,CODE,NAME,IS_ACTIVE,CREATED_BY)
                VALUES(@CompanyId,@VoucherType,@Code,@Name,@IsActive,@By) RETURNING ID
                """, parameters, transaction, cancellationToken: ct));
            var result = new VoucherCategory(id, input.VoucherType, input.Code, input.Name, input.IsActive);
            await AuditLogWriter.WriteAsync(connection, transaction, "VOUCHER_CATEGORY_SAVED", session.UserId, session.Username,
                session.BranchId, "VOUCHER_CATEGORIES", id.ToString(), JsonSerializer.Serialize(new { Before = before, After = result }), ct);
            await transaction.CommitAsync(ct); return result;
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task DeleteAsync(long id, string voucherType, AuthenticatedSession session, CancellationToken ct = default)
    {
        StockPostingSupport.EnsureBranch(session);
        if (id <= 0) throw new InvalidOperationException("Chọn loại thu/chi cần xóa.");
        var type = VoucherCategoryRules.NormalizeType(voucherType);
        await using var connection = FirebirdDatabase.CreateConnection(); await connection.OpenAsync(ct);
        await using var transaction = (FbTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var company = await connection.QuerySingleAsync<long>(new CommandDefinition("SELECT COMPANY_ID FROM BRANCHES WHERE ID=@BranchId", new { session.BranchId }, transaction, cancellationToken: ct));
            await connection.QuerySingleAsync<long>(new CommandDefinition("SELECT ID FROM COMPANIES WHERE ID=@company WITH LOCK", new { company }, transaction, cancellationToken: ct));
            var before = await connection.QuerySingleOrDefaultAsync<VoucherCategory>(new CommandDefinition("""
                SELECT ID AS Id,VOUCHER_TYPE AS VoucherType,CODE AS Code,NAME AS Name,IS_ACTIVE AS IsActive
                FROM VOUCHER_CATEGORIES WHERE ID=@id AND COMPANY_ID=@company AND VOUCHER_TYPE=@type WITH LOCK
                """, new { id, company, type }, transaction, cancellationToken: ct))
                ?? throw new InvalidOperationException("Không tìm thấy loại thu/chi trong doanh nghiệp hoặc sai hướng Thu/Chi.");
            if (await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM CASH_VOUCHERS WHERE CATEGORY_ID=@id", new { id }, transaction, cancellationToken: ct)) > 0)
                throw new InvalidOperationException("Loại thu/chi đã có phiếu sử dụng, kể cả phiếu đã hủy. Hãy bỏ chọn Đang sử dụng thay vì xóa.");
            await connection.ExecuteAsync(new CommandDefinition("DELETE FROM VOUCHER_CATEGORIES WHERE ID=@id AND COMPANY_ID=@company AND VOUCHER_TYPE=@type", new { id, company, type }, transaction, cancellationToken: ct));
            await AuditLogWriter.WriteAsync(connection, transaction, "VOUCHER_CATEGORY_DELETED", session.UserId, session.Username, session.BranchId,
                "VOUCHER_CATEGORIES", id.ToString(), JsonSerializer.Serialize(before), ct);
            await transaction.CommitAsync(ct);
        }
        catch (FbException ex) when (ex.ErrorCode == 335544466)
        {
            await transaction.RollbackAsync(ct);
            throw new InvalidOperationException("Loại thu/chi đang được sử dụng. Hãy ngừng sử dụng thay vì xóa.", ex);
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    internal static async Task<VoucherCategory> ResolveForVoucherAsync(FbConnection connection, FbTransaction transaction,
        long companyId, string voucherType, long? categoryId, CancellationToken ct)
    {
        var result = await connection.QuerySingleOrDefaultAsync<VoucherCategory>(new CommandDefinition("""
            SELECT ID AS Id,VOUCHER_TYPE AS VoucherType,CODE AS Code,NAME AS Name,IS_ACTIVE AS IsActive
            FROM VOUCHER_CATEGORIES WHERE COMPANY_ID=@CompanyId AND VOUCHER_TYPE=@VoucherType AND IS_ACTIVE=TRUE
            AND ((@CategoryId IS NOT NULL AND ID=@CategoryId) OR (@CategoryId IS NULL AND CODE='OTHER')) WITH LOCK
            """, new { CompanyId = companyId, VoucherType = voucherType, CategoryId = categoryId }, transaction, cancellationToken: ct));
        return result ?? throw new InvalidOperationException("Chọn loại thu/chi đang sử dụng, đúng doanh nghiệp và đúng hướng Thu/Chi.");
    }
}
