using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Suppliers;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Suppliers;

public sealed class FirebirdSupplierRepository : ISupplierRepository
{
    public async Task<IReadOnlyList<SupplierGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SupplierGroupOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, NAME, IS_ACTIVE, IS_DEFAULT
            FROM SUPPLIER_GROUPS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
            ORDER BY IS_DEFAULT DESC, IS_ACTIVE DESC, DISPLAY_ORDER, NAME
            """, connection);
        command.Parameters.Add("@ACTIVE_ONLY", FbDbType.Integer).Value = activeOnly ? 1 : 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SupplierGroupOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<SupplierListItem>> SearchAsync(
        SupplierSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var result = new List<SupplierListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT S.ID, S.CODE, S.NAME, COALESCE(S.PHONE, ''), COALESCE(S.EMAIL, ''), G.NAME,
                   COALESCE(S.REGION, ''),
                   COALESCE((SELECT SUM(L.LINE_AMOUNT)
                             FROM STOCK_DOCUMENTS D
                             JOIN STOCK_DOCUMENT_LINES L ON L.DOCUMENT_ID = D.ID
                             WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                               AND D.SUPPLIER_ID = S.ID
                               AND D.DOCUMENT_TYPE = 'RECEIPT'
                               AND D.STATUS_CODE = 'POSTED'), 0),
                   CAST(0.00 AS NUMERIC(18,2)),
                   (SELECT MAX(D.DOCUMENT_DATE)
                    FROM STOCK_DOCUMENTS D
                    WHERE D.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND D.SUPPLIER_ID = S.ID
                      AND D.DOCUMENT_TYPE = 'RECEIPT'
                      AND D.STATUS_CODE = 'POSTED'),
                   S.IS_ACTIVE,
                   COALESCE(S.ADDRESS, ''), COALESCE(S.CONTACT_NAME, ''), COALESCE(S.CONTACT_TITLE, ''),
                   COALESCE(S.TAX_CODE, ''), COALESCE(S.NOTES, ''),
                   S.SUPPLIER_GROUP_ID
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
            """, connection);

        var search = filter.SearchText.Trim();
        var region = filter.Region.Trim();
        command.Parameters.Add("@HAS_SEARCH", FbDbType.Integer).Value = search.Length > 0 ? 1 : 0;
        command.Parameters.Add("@SEARCH", FbDbType.VarChar, 255).Value = search;
        command.Parameters.Add("@GROUP_ID", FbDbType.BigInt).Value = filter.GroupId;
        command.Parameters.Add("@HAS_REGION", FbDbType.Integer).Value = region.Length > 0 ? 1 : 0;
        command.Parameters.Add("@REGION", FbDbType.VarChar, 100).Value = region;
        command.Parameters.Add("@HAS_ACTIVE", FbDbType.Integer).Value = filter.IsActive.HasValue ? 1 : 0;
        command.Parameters.Add("@ACTIVE_VALUE", FbDbType.Boolean).Value = filter.IsActive ?? true;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new SupplierListItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDecimal(7),
                reader.GetDecimal(8),
                reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                reader.GetBoolean(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetString(13),
                reader.GetString(14),
                reader.GetString(15),
                reader.GetInt64(16)));
        }

        return result;
    }

    public async Task<SupplierDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT S.ID, S.CODE, S.NAME, S.SUPPLIER_GROUP_ID, G.NAME,
                   COALESCE(S.CONTACT_NAME, ''), COALESCE(S.CONTACT_TITLE, ''),
                   COALESCE(S.PHONE, ''), COALESCE(S.EMAIL, ''), COALESCE(S.ADDRESS, ''),
                   COALESCE(S.REGION, ''), COALESCE(S.TAX_CODE, ''), COALESCE(S.NOTES, ''),
                   S.IS_ACTIVE
            FROM SUPPLIERS S
            JOIN SUPPLIER_GROUPS G ON G.ID = S.SUPPLIER_GROUP_ID
            WHERE S.ID = @ID
              AND S.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@ID", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new SupplierDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8),
            reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetBoolean(13));
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
            await using var taxCommand = new FbCommand("""
                SELECT FIRST 1 CODE, NAME FROM SUPPLIERS
                WHERE ID <> @ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND UPPER(TAX_CODE) = UPPER(@VALUE)
                """, connection);
            taxCommand.Parameters.AddWithValue("@ID", request.Id ?? 0);
            taxCommand.Parameters.AddWithValue("@VALUE", taxCode);
            await using var reader = await taxCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                messages.Add($"Mã số thuế đã được dùng bởi {reader.GetString(0)} - {reader.GetString(1)}.");
        }

        var phone = request.Phone.Trim();
        if (phone.Length > 0)
        {
            await using var phoneCommand = new FbCommand("""
                SELECT FIRST 1 CODE, NAME FROM SUPPLIERS
                WHERE ID <> @ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND PHONE = @VALUE
                """, connection);
            phoneCommand.Parameters.AddWithValue("@ID", request.Id ?? 0);
            phoneCommand.Parameters.AddWithValue("@VALUE", phone);
            await using var reader = await phoneCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                messages.Add($"Số điện thoại đã được dùng bởi {reader.GetString(0)} - {reader.GetString(1)}.");
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
                await using var insert = new FbCommand("""
                    INSERT INTO SUPPLIERS
                        (COMPANY_ID, CREATED_BRANCH_ID, SUPPLIER_GROUP_ID, CODE, NAME,
                         PHONE, EMAIL, ADDRESS, REGION, TAX_CODE, CONTACT_NAME, CONTACT_TITLE,
                         NOTES, IS_ACTIVE, CREATED_BY, UPDATED_BY, UPDATED_AT)
                    VALUES
                        ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BRANCH_ID, @GROUP_ID,
                         @TEMP_CODE, @NAME, @PHONE, @EMAIL, @ADDRESS, @REGION, @TAX_CODE,
                         @CONTACT_NAME, @CONTACT_TITLE, @NOTES, @IS_ACTIVE, @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """, connection, (FbTransaction)transaction);
                AddSaveParameters(insert, normalized, session);
                insert.Parameters.AddWithValue("@TEMP_CODE", $"TMP_{Guid.NewGuid():N}"[..30]);
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
                code = $"NCC{id:000000}";

                await using var updateCode = new FbCommand("""
                    UPDATE SUPPLIERS SET CODE=@CODE
                    WHERE ID=@ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, connection, (FbTransaction)transaction);
                updateCode.Parameters.AddWithValue("@CODE", code);
                updateCode.Parameters.AddWithValue("@ID", id);
                await updateCode.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                id = normalized.Id!.Value;
                await using var update = new FbCommand("""
                    UPDATE SUPPLIERS SET SUPPLIER_GROUP_ID=@GROUP_ID, NAME=@NAME,
                        PHONE=@PHONE, EMAIL=@EMAIL, ADDRESS=@ADDRESS, REGION=@REGION,
                        TAX_CODE=@TAX_CODE, CONTACT_NAME=@CONTACT_NAME, CONTACT_TITLE=@CONTACT_TITLE,
                        NOTES=@NOTES, IS_ACTIVE=@IS_ACTIVE,
                        UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                    WHERE ID=@ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, connection, (FbTransaction)transaction);
                AddSaveParameters(update, normalized, session);
                update.Parameters.AddWithValue("@ID", id);
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
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
        await using var command = new FbCommand("""
            UPDATE SUPPLIERS
            SET IS_ACTIVE=@ACTIVE, UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@ACTIVE", isActive);
        command.Parameters.AddWithValue("@USER_ID", session.UserId > 0 ? session.UserId : DBNull.Value);
        command.Parameters.AddWithValue("@ID", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
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

    private static void AddSaveParameters(FbCommand command, SupplierSaveRequest item, AuthenticatedSession session)
    {
        command.Parameters.AddWithValue("@BRANCH_ID", session.BranchId > 0 ? session.BranchId : DBNull.Value);
        command.Parameters.AddWithValue("@GROUP_ID", item.SupplierGroupId);
        command.Parameters.AddWithValue("@NAME", item.Name);
        command.Parameters.AddWithValue("@PHONE", item.Phone);
        command.Parameters.AddWithValue("@EMAIL", item.Email);
        command.Parameters.AddWithValue("@ADDRESS", item.Address);
        command.Parameters.AddWithValue("@REGION", NullIfEmpty(item.Region));
        command.Parameters.AddWithValue("@TAX_CODE", NullIfEmpty(item.TaxCode));
        command.Parameters.AddWithValue("@CONTACT_NAME", NullIfEmpty(item.ContactName));
        command.Parameters.AddWithValue("@CONTACT_TITLE", NullIfEmpty(item.ContactTitle));
        command.Parameters.AddWithValue("@NOTES", NullIfEmpty(item.Notes));
        command.Parameters.AddWithValue("@IS_ACTIVE", item.IsActive);
        command.Parameters.AddWithValue("@USER_ID", session.UserId > 0 ? session.UserId : DBNull.Value);
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<long> GetSupplierGroupIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long supplierId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT SUPPLIER_GROUP_ID FROM SUPPLIERS
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", supplierId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<string> GetCodeAsync(
        FbConnection connection,
        FbTransaction transaction,
        long supplierId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE FROM SUPPLIERS
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", supplierId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    private static async Task EnsureGroupIsActiveAsync(
        FbConnection connection,
        FbTransaction transaction,
        long groupId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM SUPPLIER_GROUPS
            WHERE ID=@ID
              AND IS_ACTIVE=TRUE
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", groupId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0)
            throw new InvalidOperationException("Nhóm nhà cung cấp đã chọn không hoạt động.");
    }
}
