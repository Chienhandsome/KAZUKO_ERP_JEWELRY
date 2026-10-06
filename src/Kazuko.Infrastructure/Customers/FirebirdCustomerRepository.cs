using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Customers;

public sealed class FirebirdCustomerRepository : ICustomerRepository
{
    public async Task<IReadOnlyList<CustomerGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CustomerGroupOption>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT ID, CODE, NAME, IS_ACTIVE, IS_DEFAULT
            FROM CUSTOMER_GROUPS
            WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
            ORDER BY IS_DEFAULT DESC, IS_ACTIVE DESC, MIN_POINTS, NAME
            """, connection);
        command.Parameters.Add("@ACTIVE_ONLY", FbDbType.Integer).Value = activeOnly ? 1 : 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CustomerGroupOption(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4)));
        }

        return result;
    }

    public async Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        CustomerSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CustomerListItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.ID, C.CODE, C.NAME, COALESCE(C.PHONE, ''), COALESCE(C.EMAIL, ''), G.NAME,
                   COALESCE(C.REGION, ''),
                   COALESCE((SELECT SUM(O.GRAND_TOTAL) FROM SALES_ORDERS O
                             WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')), 0),
                   COALESCE((SELECT SUM(O.GRAND_TOTAL - COALESCE((
                                SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                                FROM ORDER_PAYMENTS P
                                WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                                - COALESCE((
                                SELECT SUM(X.AMOUNT_VND)
                                FROM SALES_CHECKOUT_OFFSETS X
                                WHERE X.SALES_ORDER_ID = O.ID), 0))
                             FROM SALES_ORDERS O
                             WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')), 0),
                   (SELECT MAX(O.ORDER_DATE) FROM SALES_ORDERS O
                    WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')),
                   C.IS_ACTIVE,
                   COALESCE(C.ADDRESS, ''), COALESCE(C.GENDER, ''), COALESCE(C.OCCUPATION, ''),
                   COALESCE(C.MEMBERSHIP_RANK, ''), COALESCE(C.TAX_CODE, ''), COALESCE(C.NOTES, ''),
                   C.OPENING_POINTS, COALESCE(C.CREDIT_LIMIT, 0),
                   C.BIRTH_OR_ESTABLISHED_DATE, C.JOIN_DATE, C.CUSTOMER_GROUP_ID,
                   C.PHOTO_PATH
            FROM CUSTOMERS C
            JOIN CUSTOMER_GROUPS G ON G.ID = C.CUSTOMER_GROUP_ID
            WHERE C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
              AND (@HAS_ACTIVE = 0 OR C.IS_ACTIVE = @ACTIVE_VALUE)
              AND (@HAS_SEARCH = 0
                   OR CAST(C.CODE AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH
                   OR C.NAME CONTAINING @SEARCH
                   OR COALESCE(C.PHONE, '') CONTAINING @SEARCH
                   OR COALESCE(C.EMAIL, '') CONTAINING @SEARCH
                   OR CAST(COALESCE(C.TAX_CODE, '') AS VARCHAR(30) CHARACTER SET UTF8) CONTAINING @SEARCH)
              AND (@GROUP_ID = 0 OR C.CUSTOMER_GROUP_ID = @GROUP_ID)
              AND (@HAS_REGION = 0 OR COALESCE(C.REGION, '') CONTAINING @REGION)
            ORDER BY C.NAME, C.CODE
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
            result.Add(new CustomerListItem(
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
                reader.GetString(16),
                reader.GetInt32(17),
                reader.GetDecimal(18),
                reader.IsDBNull(19) ? null : reader.GetDateTime(19),
                reader.IsDBNull(20) ? null : reader.GetDateTime(20),
                reader.GetInt64(21),
                reader.IsDBNull(22) ? null : reader.GetString(22)));
        }

        return result;
    }

    public async Task<CustomerDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT C.ID, C.CODE, C.NAME, C.CUSTOMER_GROUP_ID, G.NAME,
                   COALESCE(C.ADDRESS, ''), COALESCE(C.PHONE, ''), COALESCE(C.EMAIL, ''),
                   C.BIRTH_OR_ESTABLISHED_DATE, C.OPENING_POINTS, COALESCE(C.TAX_CODE, ''),
                   COALESCE(C.NOTES, ''), COALESCE(C.GENDER, ''), COALESCE(C.REGION, ''),
                   COALESCE(C.OCCUPATION, ''), COALESCE(C.MEMBERSHIP_RANK, ''),
                   C.JOIN_DATE, COALESCE(C.CREDIT_LIMIT, 0), C.IS_ACTIVE, C.PHOTO_PATH
            FROM CUSTOMERS C
            JOIN CUSTOMER_GROUPS G ON G.ID = C.CUSTOMER_GROUP_ID
            WHERE C.ID = @ID
              AND C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@ID", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new CustomerDetail(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt64(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetDateTime(8),
            reader.GetInt32(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetString(12),
            reader.GetString(13),
            reader.GetString(14),
            reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetDateTime(16),
            reader.GetDecimal(17),
            reader.GetBoolean(18),
            reader.IsDBNull(19) ? null : reader.GetString(19));
    }

    public async Task<string> FindDuplicateWarningAsync(
        CustomerSaveRequest request,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var phone = request.Phone.Trim();
        if (phone.Length > 0)
        {
            await using var phoneCommand = new FbCommand("""
                SELECT FIRST 1 CODE, NAME FROM CUSTOMERS
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

        var taxCode = request.TaxCode.Trim();
        if (taxCode.Length > 0)
        {
            await using var taxCommand = new FbCommand("""
                SELECT FIRST 1 CODE, NAME FROM CUSTOMERS
                WHERE ID <> @ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND UPPER(TAX_CODE) = UPPER(@VALUE)
                """, connection);
            taxCommand.Parameters.AddWithValue("@ID", request.Id ?? 0);
            taxCommand.Parameters.AddWithValue("@VALUE", taxCode);
            await using var reader = await taxCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                messages.Add($"CMND/CCCD hoặc mã số thuế đã được dùng bởi {reader.GetString(0)} - {reader.GetString(1)}.");
        }

        return string.Join(Environment.NewLine, messages);
    }

    public async Task<CustomerEditResult> SaveAsync(
        CustomerSaveRequest request,
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
                ? await GetCustomerGroupIdAsync(connection, (FbTransaction)transaction, normalized.Id.Value, cancellationToken)
                : 0L;

            await EnsureGroupIsActiveAsync(connection, (FbTransaction)transaction, normalized.CustomerGroupId, cancellationToken);

            long id;
            string code;
            var isCreate = normalized.Id is null or 0;

            if (isCreate)
            {
                await using var insert = new FbCommand("""
                    INSERT INTO CUSTOMERS
                        (COMPANY_ID, CREATED_BRANCH_ID, CUSTOMER_GROUP_ID, CODE, CUSTOMER_TYPE,
                         NAME, ADDRESS, PHONE, EMAIL, BIRTH_OR_ESTABLISHED_DATE, OPENING_POINTS,
                         TAX_CODE, NOTES, GENDER, REGION, OCCUPATION, MEMBERSHIP_RANK,
                         JOIN_DATE, CREDIT_LIMIT, IS_ACTIVE, PHOTO_PATH, CREATED_BY, UPDATED_BY, UPDATED_AT)
                    VALUES
                        ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BRANCH_ID, @GROUP_ID,
                         @TEMP_CODE, 'INDIVIDUAL', @NAME, @ADDRESS, @PHONE, @EMAIL, @DATE_VALUE,
                         @POINTS, @TAX_CODE, @NOTES, @GENDER, @REGION, @OCCUPATION, @MEMBERSHIP_RANK,
                         @JOIN_DATE, @CREDIT_LIMIT, @IS_ACTIVE, @PHOTO_PATH, @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                    RETURNING ID
                    """, connection, (FbTransaction)transaction);
                AddSaveParameters(insert, normalized, session);
                insert.Parameters.AddWithValue("@TEMP_CODE", $"TMP_{Guid.NewGuid():N}"[..30]);
                id = Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken));
                code = $"KH{id:000000}";

                await using var updateCode = new FbCommand("""
                    UPDATE CUSTOMERS SET CODE=@CODE
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
                    UPDATE CUSTOMERS SET CUSTOMER_GROUP_ID=@GROUP_ID, NAME=@NAME,
                        ADDRESS=@ADDRESS, PHONE=@PHONE, EMAIL=@EMAIL,
                        BIRTH_OR_ESTABLISHED_DATE=@DATE_VALUE, OPENING_POINTS=@POINTS,
                        TAX_CODE=@TAX_CODE, NOTES=@NOTES, GENDER=@GENDER, REGION=@REGION,
                        OCCUPATION=@OCCUPATION, MEMBERSHIP_RANK=@MEMBERSHIP_RANK,
                        JOIN_DATE=@JOIN_DATE, CREDIT_LIMIT=@CREDIT_LIMIT, IS_ACTIVE=@IS_ACTIVE,
                        PHOTO_PATH=@PHOTO_PATH,
                        UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                    WHERE ID=@ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """, connection, (FbTransaction)transaction);
                AddSaveParameters(update, normalized, session);
                update.Parameters.AddWithValue("@ID", id);
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                    throw new InvalidOperationException("Khách hàng không còn tồn tại.");

                code = await GetCodeAsync(connection, (FbTransaction)transaction, id, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            committed = true;

            await AuditLogWriter.WriteAsync(
                isCreate ? "CUSTOMER_CREATED" : "CUSTOMER_UPDATED",
                session.UserId,
                session.Username,
                session.BranchId,
                "CUSTOMERS",
                id.ToString(),
                $"Name={normalized.Name}; Group={normalized.CustomerGroupId}",
                cancellationToken);

            if (!isCreate && oldGroupId > 0 && oldGroupId != normalized.CustomerGroupId)
            {
                await AuditLogWriter.WriteAsync(
                    "CUSTOMER_GROUP_CHANGED",
                    session.UserId,
                    session.Username,
                    session.BranchId,
                    "CUSTOMERS",
                    id.ToString(),
                    $"OldGroup={oldGroupId}; NewGroup={normalized.CustomerGroupId}",
                    cancellationToken);
            }

            return new CustomerEditResult(id, code, normalized.Name, normalized.Phone, normalized.Address, normalized.IsActive);
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
            UPDATE CUSTOMERS
            SET IS_ACTIVE=@ACTIVE, UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection);
        command.Parameters.AddWithValue("@ACTIVE", isActive);
        command.Parameters.AddWithValue("@USER_ID", session.UserId > 0 ? session.UserId : DBNull.Value);
        command.Parameters.AddWithValue("@ID", id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
            throw new InvalidOperationException("Khách hàng không còn tồn tại.");

        await AuditLogWriter.WriteAsync(
            isActive ? "CUSTOMER_REACTIVATED" : "CUSTOMER_ARCHIVED",
            session.UserId,
            session.Username,
            session.BranchId,
            "CUSTOMERS",
            id.ToString(),
            cancellationToken: cancellationToken);
    }

    private static CustomerSaveRequest Normalize(CustomerSaveRequest request) =>
        request with
        {
            Name = request.Name.Trim(),
            Address = request.Address.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim(),
            TaxCode = request.TaxCode.Trim(),
            Notes = request.Notes.Trim(),
            Gender = request.Gender.Trim(),
            Region = request.Region.Trim(),
            Occupation = request.Occupation.Trim(),
            MembershipRank = request.MembershipRank.Trim(),
            PhotoPath = string.IsNullOrWhiteSpace(request.PhotoPath) ? null : request.PhotoPath.Trim()
        };

    private static void Validate(CustomerSaveRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Tên khách hàng là bắt buộc.");
        if (string.IsNullOrWhiteSpace(request.Phone))
            throw new InvalidOperationException("Số điện thoại là bắt buộc.");
        if (request.CustomerGroupId <= 0)
            throw new InvalidOperationException("Khách hàng phải thuộc một nhóm đang hoạt động.");
        if (request.OpeningPoints < 0)
            throw new InvalidOperationException("Điểm tích lũy không được âm.");
        if (request.CreditLimit < 0)
            throw new InvalidOperationException("Hạn mức công nợ không được âm.");
        if (!string.IsNullOrWhiteSpace(request.Email) && !IsValidEmail(request.Email))
            throw new InvalidOperationException("Email không đúng định dạng.");
    }

    private static bool IsValidEmail(string email)
    {
        var at = email.IndexOf('@');
        return at > 0 && at < email.Length - 1 && email.IndexOf('@', at + 1) < 0;
    }

    private static void AddSaveParameters(FbCommand command, CustomerSaveRequest item, AuthenticatedSession session)
    {
        command.Parameters.AddWithValue("@BRANCH_ID", session.BranchId > 0 ? session.BranchId : DBNull.Value);
        command.Parameters.AddWithValue("@GROUP_ID", item.CustomerGroupId);
        command.Parameters.AddWithValue("@NAME", item.Name);
        command.Parameters.AddWithValue("@ADDRESS", item.Address);
        command.Parameters.AddWithValue("@PHONE", item.Phone);
        command.Parameters.AddWithValue("@EMAIL", item.Email);
        command.Parameters.AddWithValue("@DATE_VALUE", item.BirthOrEstablishedDate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@POINTS", item.OpeningPoints);
        command.Parameters.AddWithValue("@TAX_CODE", item.TaxCode);
        command.Parameters.AddWithValue("@NOTES", item.Notes);
        command.Parameters.AddWithValue("@GENDER", NullIfEmpty(item.Gender));
        command.Parameters.AddWithValue("@REGION", NullIfEmpty(item.Region));
        command.Parameters.AddWithValue("@OCCUPATION", NullIfEmpty(item.Occupation));
        command.Parameters.AddWithValue("@MEMBERSHIP_RANK", NullIfEmpty(item.MembershipRank));
        command.Parameters.AddWithValue("@JOIN_DATE", item.JoinDate ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("@CREDIT_LIMIT", item.CreditLimit);
        command.Parameters.AddWithValue("@IS_ACTIVE", item.IsActive);
        command.Parameters.AddWithValue("@PHOTO_PATH", (object?)item.PhotoPath ?? DBNull.Value);
        command.Parameters.AddWithValue("@USER_ID", session.UserId > 0 ? session.UserId : DBNull.Value);
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<long> GetCustomerGroupIdAsync(
        FbConnection connection,
        FbTransaction transaction,
        long customerId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CUSTOMER_GROUP_ID FROM CUSTOMERS
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", customerId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    private static async Task<string> GetCodeAsync(
        FbConnection connection,
        FbTransaction transaction,
        long customerId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT CODE FROM CUSTOMERS
            WHERE ID=@ID
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", customerId);
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)) ?? string.Empty;
    }

    public async Task<IReadOnlyList<CustomerPurchaseHistoryItem>> GetPurchaseHistoryAsync(
        long customerId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
            return Array.Empty<CustomerPurchaseHistoryItem>();

        var take = limit <= 0 ? 100 : Math.Min(limit, 500);
        var result = new List<CustomerPurchaseHistoryItem>();
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT FIRST @LIMIT
                   O.ID, O.ORDER_DATE, O.CODE, O.GRAND_TOTAL,
                   COALESCE((
                       SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                       FROM ORDER_PAYMENTS P
                       WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                   + COALESCE((
                       SELECT SUM(X.AMOUNT_VND)
                       FROM SALES_CHECKOUT_OFFSETS X
                       WHERE X.SALES_ORDER_ID = O.ID), 0)
            FROM SALES_ORDERS O
            WHERE O.CUSTOMER_ID = @CID
              AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
            ORDER BY O.ORDER_DATE DESC, O.ID DESC
            """, connection);
        command.Parameters.Add("@LIMIT", FbDbType.Integer).Value = take;
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = customerId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new CustomerPurchaseHistoryItem(
                reader.GetInt64(0),
                reader.GetDateTime(1),
                reader.GetString(2),
                reader.GetDecimal(3),
                reader.GetDecimal(4)));
        }

        return result;
    }

    public async Task<CustomerDebtSnapshot> GetDebtSnapshotAsync(
        long customerId,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
            return new CustomerDebtSnapshot(0, 0, 0, 0);

        const int overdueDays = 30;
        var overdueCutoff = DateTime.Today.AddDays(-overdueDays);
        decimal incurred = 0;
        decimal paid = 0;
        decimal overdue = 0;

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = new FbCommand("""
            SELECT O.ORDER_DATE, O.GRAND_TOTAL,
                   COALESCE((
                       SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                       FROM ORDER_PAYMENTS P
                       WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                   + COALESCE((
                       SELECT SUM(X.AMOUNT_VND)
                       FROM SALES_CHECKOUT_OFFSETS X
                       WHERE X.SALES_ORDER_ID = O.ID), 0)
            FROM SALES_ORDERS O
            WHERE O.CUSTOMER_ID = @CID
              AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
            """, connection);
        command.Parameters.Add("@CID", FbDbType.BigInt).Value = customerId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var orderDate = reader.GetDateTime(0);
            var total = reader.GetDecimal(1);
            var orderPaid = reader.GetDecimal(2);
            incurred += total;
            paid += orderPaid;

            var remaining = total - orderPaid;
            if (remaining < 0)
                remaining = 0;
            if (remaining > 0 && orderDate.Date < overdueCutoff)
                overdue += remaining;
        }

        var current = incurred - paid;
        if (current < 0)
            current = 0;

        return new CustomerDebtSnapshot(incurred, paid, current, overdue);
    }

    private static async Task EnsureGroupIsActiveAsync(
        FbConnection connection,
        FbTransaction transaction,
        long groupId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM CUSTOMER_GROUPS
            WHERE ID=@ID
              AND IS_ACTIVE=TRUE
              AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
            """, connection, transaction);
        command.Parameters.AddWithValue("@ID", groupId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0)
            throw new InvalidOperationException("Nhóm khách hàng đã chọn không hoạt động.");
    }
}
