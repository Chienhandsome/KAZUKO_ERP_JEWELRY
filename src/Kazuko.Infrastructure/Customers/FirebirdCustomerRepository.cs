using Dapper;
using Kazuko.Core.Authentication;
using Kazuko.Core.Customers;
using Kazuko.Infrastructure.Data;
using System.Data;

namespace Kazuko.Infrastructure.Customers;

// Dapper phase 2: SQL giữ nguyên 100% so với bản ADO.NET tay, chỉ thay boilerplate
// (FbCommand + reader loop + map theo vị trí cột) bằng Dapper.
// Dapper map cột -> constructor param theo TÊN (case-insensitive) nên mỗi SELECT
// đều có alias khớp tên property của record (vd. C.ID AS Id).
public sealed class FirebirdCustomerRepository : ICustomerRepository
{
    public async Task<IReadOnlyList<CustomerGroupOption>> GetGroupsAsync(
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CustomerGroupOption>(
            new CommandDefinition("""
                SELECT ID AS Id, CODE AS Code, NAME AS Name, IS_ACTIVE AS IsActive, IS_DEFAULT AS IsDefault
                FROM CUSTOMER_GROUPS
                WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND (@ACTIVE_ONLY = 0 OR IS_ACTIVE = TRUE)
                ORDER BY IS_DEFAULT DESC, IS_ACTIVE DESC, MIN_POINTS, NAME
                """,
                new { ACTIVE_ONLY = activeOnly ? 1 : 0 },
                cancellationToken: cancellationToken));
        return rows.AsList();
    }

    public async Task<CustomerGroupOption> CreateGroupAsync(
        string name,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var normalizedName = (name ?? string.Empty).Trim();
        if (normalizedName.Length == 0)
            throw new InvalidOperationException("Tên nhóm khách hàng là bắt buộc.");
        if (normalizedName.Length > 100)
            throw new InvalidOperationException("Tên nhóm khách hàng không được vượt quá 100 ký tự.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var duplicateId = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition("""
                    SELECT FIRST 1 ID
                    FROM CUSTOMER_GROUPS
                    WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND UPPER(TRIM(NAME)) = UPPER(@NAME)
                    """,
                    new { NAME = normalizedName },
                    transaction,
                    cancellationToken: cancellationToken));
            if (duplicateId is not null)
                throw new InvalidOperationException("Tên nhóm khách hàng đã tồn tại.");

            var temporaryCode = $"TMP_{Guid.NewGuid():N}"[..30];
            var id = await connection.ExecuteScalarAsync<long>(
                new CommandDefinition("""
                    INSERT INTO CUSTOMER_GROUPS
                        (COMPANY_ID, CODE, NAME, MIN_POINTS, DISCOUNT_RATE, IS_DEFAULT, IS_ACTIVE)
                    VALUES
                        ((SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'),
                         @CODE, @NAME, 0, 0, FALSE, TRUE)
                    RETURNING ID
                    """,
                    new { CODE = temporaryCode, NAME = normalizedName },
                    transaction,
                    cancellationToken: cancellationToken));

            var code = $"NKH{id:000000}";
            await connection.ExecuteAsync(
                new CommandDefinition("""
                    UPDATE CUSTOMER_GROUPS
                    SET CODE = @CODE
                    WHERE ID = @ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                    """,
                    new { CODE = code, ID = id },
                    transaction,
                    cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);

            await AuditLogWriter.WriteAsync(
                "CUSTOMER_GROUP_CREATED",
                session.UserId,
                session.Username,
                session.BranchId,
                "CUSTOMER_GROUPS",
                id.ToString(),
                $"Code={code}; Name={normalizedName}; ClassificationOnly=True",
                cancellationToken);

            return new CustomerGroupOption(id, code, normalizedName, true, false);
        }
        catch
        {
            try { await transaction.RollbackAsync(cancellationToken); } catch { /* swallow */ }
            throw;
        }
    }

    public async Task<IReadOnlyList<CustomerListItem>> SearchAsync(
        CustomerSearchFilter filter,
        CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var search = filter.SearchText.Trim();
        var region = filter.Region.Trim();
        var rows = await connection.QueryAsync<CustomerListItem>(
            new CommandDefinition("""
                SELECT C.ID AS Id, C.CODE AS Code, C.NAME AS Name,
                       COALESCE(C.PHONE, '') AS Phone, COALESCE(C.EMAIL, '') AS Email,
                       G.NAME AS CustomerGroupName,
                       COALESCE(C.REGION, '') AS Region,
                       COALESCE((SELECT SUM(O.GRAND_TOTAL) FROM SALES_ORDERS O
                                 WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')), 0) AS TotalSales,
                       COALESCE((SELECT SUM(O.GRAND_TOTAL - COALESCE((
                                    SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                                    FROM ORDER_PAYMENTS P
                                    WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                                    - COALESCE((
                                    SELECT SUM(X.AMOUNT_VND)
                                    FROM SALES_CHECKOUT_OFFSETS X
                                    WHERE X.SALES_ORDER_ID = O.ID), 0))
                                 FROM SALES_ORDERS O
                                 WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')), 0) AS CurrentDebt,
                       (SELECT MAX(O.ORDER_DATE) FROM SALES_ORDERS O
                        WHERE O.CUSTOMER_ID = C.ID AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')) AS LastPurchaseDate,
                       C.IS_ACTIVE AS IsActive,
                       COALESCE(C.ADDRESS, '') AS Address, COALESCE(C.GENDER, '') AS Gender,
                       COALESCE(C.OCCUPATION, '') AS Occupation,
                       COALESCE(C.MEMBERSHIP_RANK, '') AS MembershipRank,
                       COALESCE(C.TAX_CODE, '') AS TaxCode, COALESCE(C.NOTES, '') AS Notes,
                       C.OPENING_POINTS AS OpeningPoints, COALESCE(C.CREDIT_LIMIT, 0) AS CreditLimit,
                       C.BIRTH_OR_ESTABLISHED_DATE AS BirthOrEstablishedDate,
                       C.JOIN_DATE AS JoinDate, C.CUSTOMER_GROUP_ID AS CustomerGroupId,
                       C.PHOTO_PATH AS PhotoPath
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

    public async Task<CustomerDetail?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        return await connection.QuerySingleOrDefaultAsync<CustomerDetail>(
            new CommandDefinition("""
                SELECT C.ID AS Id, C.CODE AS Code, C.NAME AS Name,
                       C.CUSTOMER_GROUP_ID AS CustomerGroupId, G.NAME AS CustomerGroupName,
                       COALESCE(C.ADDRESS, '') AS Address, COALESCE(C.PHONE, '') AS Phone,
                       COALESCE(C.EMAIL, '') AS Email,
                       C.BIRTH_OR_ESTABLISHED_DATE AS BirthOrEstablishedDate,
                       C.OPENING_POINTS AS OpeningPoints, COALESCE(C.TAX_CODE, '') AS TaxCode,
                       COALESCE(C.NOTES, '') AS Notes, COALESCE(C.GENDER, '') AS Gender,
                       COALESCE(C.REGION, '') AS Region,
                       COALESCE(C.OCCUPATION, '') AS Occupation,
                       COALESCE(C.MEMBERSHIP_RANK, '') AS MembershipRank,
                       C.JOIN_DATE AS JoinDate, COALESCE(C.CREDIT_LIMIT, 0) AS CreditLimit,
                       C.IS_ACTIVE AS IsActive, C.PHOTO_PATH AS PhotoPath,
                       C.ID_ISSUE_DATE AS IdIssueDate,
                       COALESCE(C.ID_ISSUE_PLACE, '') AS IdIssuePlace,
                       COALESCE(C.BANK_NAME, '') AS BankName,
                       COALESCE(C.BUYER_NAME, '') AS BuyerName,
                       COALESCE(C.BANK_ACCOUNT_NUMBER, '') AS BankAccountNumber
                FROM CUSTOMERS C
                JOIN CUSTOMER_GROUPS G ON G.ID = C.CUSTOMER_GROUP_ID
                WHERE C.ID = @ID
                  AND C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = id },
                cancellationToken: cancellationToken));
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
            var match = await connection.QueryFirstOrDefaultAsync<DuplicateMatch>(
                new CommandDefinition("""
                    SELECT FIRST 1 CODE AS Code, NAME AS Name FROM CUSTOMERS
                    WHERE ID <> @ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND PHONE = @VALUE
                    """,
                    new { ID = request.Id ?? 0, VALUE = phone },
                    cancellationToken: cancellationToken));
            if (match is not null)
                messages.Add($"Số điện thoại đã được dùng bởi {match.Code} - {match.Name}.");
        }

        var taxCode = request.TaxCode.Trim();
        if (taxCode.Length > 0)
        {
            var match = await connection.QueryFirstOrDefaultAsync<DuplicateMatch>(
                new CommandDefinition("""
                    SELECT FIRST 1 CODE AS Code, NAME AS Name FROM CUSTOMERS
                    WHERE ID <> @ID
                      AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND UPPER(TAX_CODE) = UPPER(@VALUE)
                    """,
                    new { ID = request.Id ?? 0, VALUE = taxCode },
                    cancellationToken: cancellationToken));
            if (match is not null)
                messages.Add($"CMND/CCCD hoặc mã số thuế đã được dùng bởi {match.Code} - {match.Name}.");
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
                ? await GetCustomerGroupIdAsync(connection, transaction, normalized.Id.Value, cancellationToken)
                : 0L;

            await EnsureGroupIsActiveAsync(connection, transaction, normalized.CustomerGroupId, cancellationToken);

            long id;
            string code;
            var isCreate = normalized.Id is null or 0;

            if (isCreate)
            {
                var insertParameters = SaveParameters(normalized, session);
                insertParameters.Add("@TEMP_CODE", $"TMP_{Guid.NewGuid():N}"[..30]);
                id = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("""
                        INSERT INTO CUSTOMERS
                            (COMPANY_ID, CREATED_BRANCH_ID, CUSTOMER_GROUP_ID, CODE, CUSTOMER_TYPE,
                             NAME, ADDRESS, PHONE, EMAIL, BIRTH_OR_ESTABLISHED_DATE, OPENING_POINTS,
                             TAX_CODE, NOTES, GENDER, REGION, OCCUPATION, MEMBERSHIP_RANK,
                             JOIN_DATE, CREDIT_LIMIT, IS_ACTIVE, PHOTO_PATH,
                             ID_ISSUE_DATE, ID_ISSUE_PLACE, BANK_NAME, BUYER_NAME, BANK_ACCOUNT_NUMBER,
                             CREATED_BY, UPDATED_BY, UPDATED_AT)
                        VALUES
                            ((SELECT ID FROM COMPANIES WHERE CODE='KAZUKO'), @BRANCH_ID, @GROUP_ID,
                             @TEMP_CODE, 'INDIVIDUAL', @NAME, @ADDRESS, @PHONE, @EMAIL, @DATE_VALUE,
                             @POINTS, @TAX_CODE, @NOTES, @GENDER, @REGION, @OCCUPATION, @MEMBERSHIP_RANK,
                             @JOIN_DATE, @CREDIT_LIMIT, @IS_ACTIVE, @PHOTO_PATH,
                             @ID_ISSUE_DATE, @ID_ISSUE_PLACE, @BANK_NAME, @BUYER_NAME, @BANK_ACCOUNT_NUMBER,
                             @USER_ID, @USER_ID, CURRENT_TIMESTAMP)
                        RETURNING ID
                        """,
                        insertParameters,
                        transaction,
                        cancellationToken: cancellationToken));
                code = $"KH{id:000000}";

                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE CUSTOMERS SET CODE=@CODE
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
                var updateParameters = SaveParameters(normalized, session);
                updateParameters.Add("@ID", id);
                var affected = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE CUSTOMERS SET CUSTOMER_GROUP_ID=@GROUP_ID, NAME=@NAME,
                            ADDRESS=@ADDRESS, PHONE=@PHONE, EMAIL=@EMAIL,
                            BIRTH_OR_ESTABLISHED_DATE=@DATE_VALUE, OPENING_POINTS=@POINTS,
                            TAX_CODE=@TAX_CODE, NOTES=@NOTES, GENDER=@GENDER, REGION=@REGION,
                            OCCUPATION=@OCCUPATION, MEMBERSHIP_RANK=@MEMBERSHIP_RANK,
                            JOIN_DATE=@JOIN_DATE, CREDIT_LIMIT=@CREDIT_LIMIT, IS_ACTIVE=@IS_ACTIVE,
                            PHOTO_PATH=@PHOTO_PATH,
                            ID_ISSUE_DATE=@ID_ISSUE_DATE, ID_ISSUE_PLACE=@ID_ISSUE_PLACE,
                            BANK_NAME=@BANK_NAME, BUYER_NAME=@BUYER_NAME,
                            BANK_ACCOUNT_NUMBER=@BANK_ACCOUNT_NUMBER,
                            UPDATED_BY=@USER_ID, UPDATED_AT=CURRENT_TIMESTAMP
                        WHERE ID=@ID
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        updateParameters,
                        transaction,
                        cancellationToken: cancellationToken));
                if (affected == 0)
                    throw new InvalidOperationException("Khách hàng không còn tồn tại.");

                code = await GetCodeAsync(connection, transaction, id, cancellationToken);
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
        var affected = await connection.ExecuteAsync(
            new CommandDefinition("""
                UPDATE CUSTOMERS
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

    public async Task<IReadOnlyList<CustomerPurchaseHistoryItem>> GetPurchaseHistoryAsync(
        long customerId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0)
            return Array.Empty<CustomerPurchaseHistoryItem>();

        var take = limit <= 0 ? 100 : Math.Min(limit, 500);
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CustomerPurchaseHistoryItem>(
            new CommandDefinition("""
                SELECT FIRST @LIMIT
                       O.ID AS OrderId, O.ORDER_DATE AS OrderDate, O.CODE AS InvoiceCode,
                       O.GRAND_TOTAL AS TotalAmount,
                       COALESCE((
                           SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                           FROM ORDER_PAYMENTS P
                           WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                       + COALESCE((
                           SELECT SUM(X.AMOUNT_VND)
                           FROM SALES_CHECKOUT_OFFSETS X
                           WHERE X.SALES_ORDER_ID = O.ID), 0) AS PaidAmount
                FROM SALES_ORDERS O
                WHERE O.CUSTOMER_ID = @CID
                  AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                ORDER BY O.ORDER_DATE DESC, O.ID DESC
                """,
                new { LIMIT = take, CID = customerId },
                cancellationToken: cancellationToken));
        return rows.AsList();
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
        var rows = await connection.QueryAsync<DebtOrderRow>(
            new CommandDefinition("""
                SELECT O.ORDER_DATE AS OrderDate, O.GRAND_TOTAL AS GrandTotal,
                       COALESCE((
                           SELECT SUM(CASE P.PAYMENT_TYPE WHEN 'REFUND' THEN -P.AMOUNT ELSE P.AMOUNT END)
                           FROM ORDER_PAYMENTS P
                           WHERE P.ORDER_ID = O.ID AND P.IS_VOIDED = FALSE), 0)
                       + COALESCE((
                           SELECT SUM(X.AMOUNT_VND)
                           FROM SALES_CHECKOUT_OFFSETS X
                           WHERE X.SALES_ORDER_ID = O.ID), 0) AS PaidAmount
                FROM SALES_ORDERS O
                WHERE O.CUSTOMER_ID = @CID
                  AND O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                """,
                new { CID = customerId },
                cancellationToken: cancellationToken));

        foreach (var row in rows)
        {
            var total = row.GrandTotal;
            var orderPaid = row.PaidAmount;
            incurred += total;
            paid += orderPaid;

            var remaining = total - orderPaid;
            if (remaining < 0)
                remaining = 0;
            if (remaining > 0 && row.OrderDate.Date < overdueCutoff)
                overdue += remaining;
        }

        var current = incurred - paid;
        if (current < 0)
            current = 0;

        return new CustomerDebtSnapshot(incurred, paid, current, overdue);
    }

    public async Task<IReadOnlyList<CustomerTopSalesItem>> GetTopCustomersAsync(
        DateTime fromDate,
        DateTime toDate,
        int topN,
        CancellationToken cancellationToken = default)
    {
        if (topN <= 0)
            return Array.Empty<CustomerTopSalesItem>();

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<CustomerTopSalesItem>(
            new CommandDefinition("""
                SELECT FIRST @TOP
                       C.ID AS CustomerId, C.CODE AS Code, C.NAME AS Name,
                       COALESCE(C.PHONE, '') AS Phone,
                       COALESCE(G.NAME, '') AS CustomerGroupName,
                       CAST(COUNT(O.ID) AS INTEGER) AS OrderCount,
                       SUM(O.GRAND_TOTAL) AS TotalSales
                FROM SALES_ORDERS O
                JOIN CUSTOMERS C ON C.ID = O.CUSTOMER_ID
                LEFT JOIN CUSTOMER_GROUPS G ON G.ID = C.CUSTOMER_GROUP_ID
                WHERE O.STATUS_CODE NOT IN ('DRAFT', 'CANCELLED')
                  AND O.ORDER_DATE >= @FROMD AND O.ORDER_DATE < @TOD
                  AND C.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                GROUP BY C.ID, C.CODE, C.NAME, C.PHONE, G.NAME
                ORDER BY TotalSales DESC, C.ID
                """,
                new { TOP = topN, FROMD = fromDate.Date, TOD = toDate.Date.AddDays(1) },
                cancellationToken: cancellationToken));

        return rows.ToList();
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

    private static DynamicParameters SaveParameters(CustomerSaveRequest item, AuthenticatedSession session)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@BRANCH_ID", session.BranchId > 0 ? (object)session.BranchId : DBNull.Value);
        parameters.Add("@GROUP_ID", item.CustomerGroupId);
        parameters.Add("@NAME", item.Name);
        parameters.Add("@ADDRESS", item.Address);
        parameters.Add("@PHONE", item.Phone);
        parameters.Add("@EMAIL", item.Email);
        parameters.Add("@DATE_VALUE", item.BirthOrEstablishedDate ?? (object)DBNull.Value);
        parameters.Add("@POINTS", item.OpeningPoints);
        parameters.Add("@TAX_CODE", item.TaxCode);
        parameters.Add("@NOTES", item.Notes);
        parameters.Add("@GENDER", NullIfEmpty(item.Gender));
        parameters.Add("@REGION", NullIfEmpty(item.Region));
        parameters.Add("@OCCUPATION", NullIfEmpty(item.Occupation));
        parameters.Add("@MEMBERSHIP_RANK", NullIfEmpty(item.MembershipRank));
        parameters.Add("@JOIN_DATE", item.JoinDate ?? (object)DBNull.Value);
        parameters.Add("@CREDIT_LIMIT", item.CreditLimit);
        parameters.Add("@IS_ACTIVE", item.IsActive);
        parameters.Add("@PHOTO_PATH", (object?)item.PhotoPath ?? DBNull.Value);
        parameters.Add("@ID_ISSUE_DATE", item.IdIssueDate ?? (object)DBNull.Value);
        parameters.Add("@ID_ISSUE_PLACE", NullIfEmpty(item.IdIssuePlace));
        parameters.Add("@BANK_NAME", NullIfEmpty(item.BankName));
        parameters.Add("@BUYER_NAME", NullIfEmpty(item.BuyerName));
        parameters.Add("@BANK_ACCOUNT_NUMBER", NullIfEmpty(item.BankAccountNumber));
        parameters.Add("@USER_ID", session.UserId > 0 ? (object)session.UserId : DBNull.Value);
        return parameters;
    }

    private static object NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    private static async Task<long> GetCustomerGroupIdAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long customerId,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<long>(
            new CommandDefinition("""
                SELECT CUSTOMER_GROUP_ID FROM CUSTOMERS
                WHERE ID=@ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = customerId },
                transaction,
                cancellationToken: cancellationToken));
    }

    private static async Task<string> GetCodeAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long customerId,
        CancellationToken cancellationToken)
    {
        return await connection.ExecuteScalarAsync<string>(
            new CommandDefinition("""
                SELECT CODE FROM CUSTOMERS
                WHERE ID=@ID
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = customerId },
                transaction,
                cancellationToken: cancellationToken)) ?? string.Empty;
    }

    private static async Task EnsureGroupIsActiveAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long groupId,
        CancellationToken cancellationToken)
    {
        var count = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition("""
                SELECT COUNT(*) FROM CUSTOMER_GROUPS
                WHERE ID=@ID
                  AND IS_ACTIVE=TRUE
                  AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                """,
                new { ID = groupId },
                transaction,
                cancellationToken: cancellationToken));
        if (count == 0)
            throw new InvalidOperationException("Nhóm khách hàng đã chọn không hoạt động.");
    }

    private sealed record DuplicateMatch(string Code, string Name);

    private sealed record DebtOrderRow(DateTime OrderDate, decimal GrandTotal, decimal PaidAmount);
}
