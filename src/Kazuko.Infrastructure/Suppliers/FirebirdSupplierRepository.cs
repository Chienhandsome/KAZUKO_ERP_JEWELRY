using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Suppliers;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Suppliers;

// Dapper phase 2: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record đích (vd. COMPANY_ID AS CompanyId,
// các cột snake_case đều phải alias vì Dapper không tự bỏ dấu gạch dưới).
public sealed class FirebirdSupplierRepository : ISupplierRepository
{
    public async Task<IReadOnlyList<SupplierGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SupplierGroupOption>(
            new CommandDefinition("""
                SELECT ID AS Id, CODE AS Code, NAME AS Name, IS_ACTIVE AS IsActive, IS_DEFAULT AS IsDefault
                FROM SUPPLIER_GROUPS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
                ORDER BY IS_DEFAULT DESC, IS_ACTIVE DESC, DISPLAY_ORDER, NAME
                """,
                new { ACTIVE_ONLY = activeOnly ? 1 : 0 },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SupplierListItem>> SearchAsync(
        SupplierSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var search = filter.SearchText.Trim();
        var region = filter.Region.Trim();

        var rows = await connection.QueryAsync<SupplierListItem>(
            new CommandDefinition("""
                SELECT S.ID AS Id, S.CODE AS Code, S.NAME AS Name,
                       COALESCE(S.PHONE, '') AS Phone, COALESCE(S.EMAIL, '') AS Email,
                       G.NAME AS SupplierGroupName,
                       COALESCE(S.REGION, '') AS Region,
                       COALESCE((SELECT SUM(L.LINE_AMOUNT)
                                 FROM STOCK_DOCUMENTS D
                                 JOIN STOCK_DOCUMENT_LINES L ON L.DOCUMENT_ID = D.ID
                                 WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                                   AND D.SUPPLIER_ID = S.ID
                                   AND D.DOCUMENT_TYPE = 'RECEIPT'
                                   AND D.STATUS_CODE = 'POSTED'), 0) AS TotalPurchases,
                       CAST(0.00 AS NUMERIC(18,2)) AS CurrentDebt,
                       (SELECT MAX(D.DOCUMENT_DATE)
                        FROM STOCK_DOCUMENTS D
                        WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                          AND D.SUPPLIER_ID = S.ID
                          AND D.DOCUMENT_TYPE = 'RECEIPT'
                          AND D.STATUS_CODE = 'POSTED') AS LastPurchaseDate,
                       S.IS_ACTIVE AS IsActive,
                       COALESCE(S.ADDRESS, '') AS Address, COALESCE(S.CONTACT_NAME, '') AS ContactName,
                       COALESCE(S.CONTACT_TITLE, '') AS ContactTitle,
                       COALESCE(S.TAX_CODE, '') AS TaxCode, COALESCE(S.NOTES, '') AS Notes,
                       S.SUPPLIER_GROUP_ID AS SupplierGroupId
                FROM SUPPLIERS S
                JOIN SUPPLIER_GROUPS G ON G.ID = S.SUPPLIER_GROUP_ID
                WHERE S.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@HAS_ACTIVE = 0 OR S.IS_ACTIVE = @ACTIVE_VALUE)
                  AND (@HAS_SEARCH = 0
                       OR CAST(S.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR S.NAME CONTAINING @SEARCH
                       OR COALESCE(S.PHONE, '') CONTAINING @SEARCH
                       OR COALESCE(S.EMAIL, '') CONTAINING @SEARCH
                       OR CAST(COALESCE(S.TAX_CODE, '') AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                       OR COALESCE(S.CONTACT_NAME, '') CONTAINING @SEARCH)
                  AND (@GROUP_ID = 0 OR S.SUPPLIER_GROUP_ID = @GROUP_ID)
                  AND (@HAS_REGION = 0 OR COALESCE(S.REGION, '') CONTAINING @REGION)
                ORDER BY S.NAME, S.CODE
                """,
                new
                {
                    HAS_SEARCH = search.Length > 0 ? 1 : 0,
                    SEARCH = search,
                    GROUP_ID = filter.GroupId,
                    HAS_REGION = region.Length > 0 ? 1 : 0,
                    REGION = region,
                    HAS_ACTIVE = filter.IsActive.HasValue ? 1 : 0,
                    ACTIVE_VALUE = filter.IsActive ?? true
                },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<SupplierDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<SupplierDetail>(
            new CommandDefinition("""
                SELECT S.ID AS Id, S.CODE AS Code, S.NAME AS Name,
                       S.SUPPLIER_GROUP_ID AS SupplierGroupId, G.NAME AS SupplierGroupName,
                       COALESCE(S.CONTACT_NAME, '') AS ContactName, COALESCE(S.CONTACT_TITLE, '') AS ContactTitle,
                       COALESCE(S.PHONE, '') AS Phone, COALESCE(S.EMAIL, '') AS Email,
                       COALESCE(S.ADDRESS, '') AS Address,
                       COALESCE(S.REGION, '') AS Region, COALESCE(S.TAX_CODE, '') AS TaxCode,
                       COALESCE(S.NOTES, '') AS Notes,
                       S.IS_ACTIVE AS IsActive
                FROM SUPPLIERS S
                JOIN SUPPLIER_GROUPS G ON G.ID = S.SUPPLIER_GROUP_ID
                WHERE S.ID = @ID
                  AND S.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = id },
                cancellationToken: cancellationToken));
    }

    public async Task<string> FindDuplicateWarningAsync(
        SupplierSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var taxCode = request.TaxCode.Trim();
        if (taxCode.Length > 0)
        {
            var taxHit = await connection.QuerySingleOrDefaultAsync<DuplicateHit>(
                new CommandDefinition("""
                    SELECT FIRST 1 CODE AS Code, NAME AS Name FROM SUPPLIERS
                    WHERE ID <> @ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND UPPER(TAX_CODE) = UPPER(@VALUE)
                    """,
                    new { ID = request.Id ?? 0, VALUE = taxCode },
                    cancellationToken: cancellationToken));
            if (taxHit is not null)
                messages.Add($"Mã số thuế đã được dùng bởi {taxHit.Code} - {taxHit.Name}.");
        }

        var phone = request.Phone.Trim();
        if (phone.Length > 0)
        {
            var phoneHit = await connection.QuerySingleOrDefaultAsync<DuplicateHit>(
                new CommandDefinition("""
                    SELECT FIRST 1 CODE AS Code, NAME AS Name FROM SUPPLIERS
                    WHERE ID <> @ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND PHONE = @VALUE
                    """,
                    new { ID = request.Id ?? 0, VALUE = phone },
                    cancellationToken: cancellationToken));
            if (phoneHit is not null)
                messages.Add($"Số điện thoại đã được dùng bởi {phoneHit.Code} - {phoneHit.Name}.");
        }

        return string.Join(Environment.NewLine, messages);
    }

    public async Task<SupplierEditResult> SaveAsync(
        SupplierSaveRequest request,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalized = Normalize(request);
        Validate(normalized);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var committed = false;

        try
        {
            var oldGroupId = normalized.Id is > 0
                ? await GetSupplierGroupIdAsync(connection, (FbTransaction)transaction, normalized.Id.Value, cancellationToken)
                : 0L;

            await EnsureGroupIsActiveAsync(connection, (FbTransaction)transaction, normalized.SupplierGroupId, cancellationToken);

            long id;
            string code;
            var isCreate = normalized.Id is null or 0;

            if (isCreate)
            {
                id = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("""
                        INSERT INTO SUPPLIERS
                            (COMPANY_ID, CREATED_BRANCH_ID, SUPPLIER_GROUP_ID, CODE, NAME,
                             PHONE, EMAIL, ADDRESS, REGION, TAX_CODE, CONTACT_NAME, CONTACT_TITLE,
                             NOTES, IS_ACTIVE, CREATED_BY, UPDATED_BY, UPDATED_AT)
                        VALUES
                            ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BRANCH_ID, @GROUP_ID,
                             @TEMP_CODE, @NAME, @PHONE, @EMAIL, @ADDRESS, @REGION, @TAX_CODE,
                             @CONTACT_NAME, @CONTACT_TITLE, @NOTES, @IS_ACTIVE, @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                        RETURNING ID
                        """,
                        new
                        {
                            BRANCH_ID = session.BranchId > 0 ? (object)session.BranchId : DBNull.Value,
                            GROUP_ID = normalized.SupplierGroupId,
                            TEMP_CODE = $"TMP_{Guid.NewGuid():N}"[..30],
                            NAME = normalized.Name,
                            PHONE = normalized.Phone,
                            EMAIL = normalized.Email,
                            ADDRESS = normalized.Address,
                            REGION = NullIfEmpty(normalized.Region),
                            TAX_CODE = NullIfEmpty(normalized.TaxCode),
                            CONTACT_NAME = NullIfEmpty(normalized.ContactName),
                            CONTACT_TITLE = NullIfEmpty(normalized.ContactTitle),
                            NOTES = NullIfEmpty(normalized.Notes),
                            IS_ACTIVE = normalized.IsActive,
                            USER_ID = session.UserId > 0 ? (object)session.UserId : DBNull.Value
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                code = $"NCC{id:000000}";

                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE SUPPLIERS SET CODE=@CODE
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new { CODE = code, ID = id },
                        transaction,
                        cancellationToken: cancellationToken));
            }
            else
            {
                id = normalized.Id!.Value;
                var affected = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE SUPPLIERS SET SUPPLIER_GROUP_ID=@GROUP_ID, NAME=@NAME,
                            PHONE=@PHONE, EMAIL=@EMAIL, ADDRESS=@ADDRESS, REGION=@REGION,
                            TAX_CODE=@TAX_CODE, CONTACT_NAME=@CONTACT_NAME, CONTACT_TITLE=@CONTACT_TITLE,
                            NOTES=@NOTES, IS_ACTIVE=@IS_ACTIVE,
                            UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new
                        {
                            GROUP_ID = normalized.SupplierGroupId,
                            NAME = normalized.Name,
                            PHONE = normalized.Phone,
                            EMAIL = normalized.Email,
                            ADDRESS = normalized.Address,
                            REGION = NullIfEmpty(normalized.Region),
                            TAX_CODE = NullIfEmpty(normalized.TaxCode),
                            CONTACT_NAME = NullIfEmpty(normalized.ContactName),
                            CONTACT_TITLE = NullIfEmpty(normalized.ContactTitle),
                            NOTES = NullIfEmpty(normalized.Notes),
                            IS_ACTIVE = normalized.IsActive,
                            USER_ID = session.UserId > 0 ? (object)session.UserId : DBNull.Value,
                            ID = id
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                if (affected == 0)
                    throw new InvalidOperationException("Nhà cung cấp không còn tồn tại.");

                code = await GetCodeAsync(connection, (FbTransaction)transaction, id, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await AuditLogWriter.WriteAsync(
                isCreate ? "SUPPLIER_CREATED" : "SUPPLIER_UPDATED",
                session.UserId,
                session.Username,
                session.BranchId,
                "SUPPLIERS",
                id.ToString(),
                $"Name={normalized.Name}; Group={normalized.SupplierGroupId}",
                cancellationToken);

            if (!isCreate && oldGroupId > 0 && oldGroupId != normalized.SupplierGroupId)
            {
                await AuditLogWriter.WriteAsync(
                    "SUPPLIER_GROUP_CHANGED",
                    session.UserId,
                    session.Username,
                    session.BranchId,
                    "SUPPLIERS",
                    id.ToString(),
                    $"OldGroup={oldGroupId}; NewGroup={normalized.SupplierGroupId}",
                    cancellationToken);
            }

            return new SupplierEditResult(id, code, normalized.Name, normalized.Phone, normalized.Address, normalized.IsActive);
        }
        catch
        {
            if (!committed)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch
                {
                    // Swallow rollback failures; rethrow original.
                }
            }

            throw;
        }
    }

    public async Task SetActiveAsync(
        long id,
        bool isActive,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE SUPPLIERS
                SET IS_ACTIVE=@ACTIVE, UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                WHERE ID=@ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new
                {
                    ACTIVE = isActive,
                    USER_ID = session.UserId > 0 ? (object)session.UserId : DBNull.Value,
                    ID = id
                },
                cancellationToken: cancellationToken));
        if (affected == 0)
            throw new InvalidOperationException("Nhà cung cấp không còn tồn tại.");

        await AuditLogWriter.WriteAsync(
            isActive ? "SUPPLIER_REACTIVATED" : "SUPPLIER_ARCHIVED",
            session.UserId,
            session.Username,
            session.BranchId,
            "SUPPLIERS",
            id.ToString(),
            cancellationToken: cancellationToken);
    }

    private static SupplierSaveRequest Normalize(SupplierSaveRequest request) =>
        request with
        {
            Name = (request.Name ?? string.Empty).Trim(),
            Address = (request.Address ?? string.Empty).Trim(),
            Phone = (request.Phone ?? string.Empty).Trim(),
            Email = (request.Email ?? string.Empty).Trim(),
            TaxCode = (request.TaxCode ?? string.Empty).Trim(),
            Notes = (request.Notes ?? string.Empty).Trim(),
            Region = (request.Region ?? string.Empty).Trim(),
            ContactName = (request.ContactName ?? string.Empty).Trim(),
            ContactTitle = (request.ContactTitle ?? string.Empty).Trim()
        };

    private static void Validate(SupplierSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên nhà cung cấp là bắt buộc.");
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new InvalidOperationException("Số điện thoại là bắt buộc.");
        if (request.SupplierGroupId <= 0)
            throw new InvalidOperationException("Nhà cung cấp phải thuộc một nhóm đang hoạt động.");
        if (!string.IsNullOrWhiteSpace(request.Email) && !IsValidEmail(request.Email))
            throw new InvalidOperationException("Email không đúng định dạng.");
    }

    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0;
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<long> GetSupplierGroupIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long supplierId,
        CancellationToken cancellationToken)
    {
        var result = await connection.ExecuteScalarAsync<long?>(
            new CommandDefinition("""
                SELECT SUPPLIER_GROUP_ID FROM SUPPLIERS
                WHERE ID=@ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = supplierId },
                transaction,
                cancellationToken: cancellationToken));
        return result ?? 0;
    }

    private static async Task<string> GetCodeAsync(
        FbConnection connection,
        FbTransaction transaction,
        long supplierId,
        CancellationToken cancellationToken)
    {
        var code = await connection.ExecuteScalarAsync<string>(
            new CommandDefinition("""
                SELECT CODE FROM SUPPLIERS
                WHERE ID=@ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = supplierId },
                transaction,
                cancellationToken: cancellationToken));
        return code ?? string.Empty;
    }

    private static async Task EnsureGroupIsActiveAsync(
        FbConnection connection,
        FbTransaction transaction,
        long groupId,
        CancellationToken cancellationToken)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("""
                SELECT COUNT(*) FROM SUPPLIER_GROUPS
                WHERE ID=@ID
                  AND IS_ACTIVE=TRUE
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = groupId },
                transaction,
                cancellationToken: cancellationToken));
        if (count == 0)
            throw new InvalidOperationException("Nhóm nhà cung cấp đã chọn không hoạt động.");
    }

    // DTO nhỏ nội bộ cho query cảnh báo trùng lặp (CODE/NAME -> alias khớp property).
    private sealed record DuplicateHit(string Code, string Name);
}
