using System.Data;
using System.Security.Cryptography;
using Dapper;
using Kazuko.Core.UserManagement;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.UserManagement;

// Quản lý người dùng + phân quyền theo NHÓM (migration 0076).
// Độc lập với hệ thống USERS/ROLES/PERMISSIONS cũ ở base schema — chỉ dùng
// USERS như một bảng lưu trữ (không đụng các bảng ROLES/PERMISSIONS cũ).
//
// Các quyết định thiết kế:
// 1. Email người dùng không có cột riêng trên USERS (migration 76 không thêm),
//    nên lấy/lưu vào HR_EMPLOYEES.EMAIL — 1 nhân viên = 1 tài khoản.
// 2. Mật khẩu: PBKDF2-SHA256, salt 16 bytes ngẫu nhiên, 100000 vòng lặp —
//    copy đúng chuẩn của FirebirdAuthenticationService để đăng nhập cũ đọc được.
// 3. Tài khoản hệ thống (ROLE CODE = 'ADMIN') KHÔNG bao giờ xuất hiện trong
//    danh sách và không thể xóa qua repository này.
// 4. Tài khoản mới dùng chi nhánh phiên hiện tại (fallback: chi nhánh mặc định
//    của người tạo / chi nhánh active đầu tiên), đồng bộ các cột legacy bắt buộc.
public sealed class FirebirdUserManagementRepository : IUserManagementRepository
{
    private const int PasswordIterations = 100000;
    private const int PasswordSaltBytes = 16;
    private const int PasswordHashBytes = 32;

    public async Task<IReadOnlyList<UserGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<UserGroup>(
            new CommandDefinition("""
                SELECT G.ID AS Id,
                       G.CODE AS Code,
                       G.NAME AS Name,
                       G.ICON AS Icon,
                       G.DESCRIPTION AS Description,
                       G.IS_ACTIVE AS IsActive,
                       G.DISPLAY_ORDER AS DisplayOrder,
                       (SELECT COUNT(*) FROM USERS U WHERE U.GROUP_ID = G.ID) AS UserCount
                FROM USER_GROUPS G
                WHERE G.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                ORDER BY G.DISPLAY_ORDER, G.NAME
                """,
                cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task<IReadOnlyList<SystemUser>> GetUsersAsync(long groupId, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<SystemUser>(
            new CommandDefinition("""
                SELECT U.ID AS Id,
                       U.USERNAME AS Username,
                       U.DISPLAY_NAME AS DisplayName,
                       U.EMPLOYEE_ID AS EmployeeId,
                       E.FULL_NAME AS EmployeeName,
                       E.EMAIL AS Email,
                       U.WAREHOUSE_ID AS WarehouseId,
                       L.NAME AS WarehouseName,
                       U.GROUP_ID AS GroupId,
                       G.NAME AS GroupName,
                       U.IS_ACTIVE AS IsActive,
                       U.NOTES AS Notes
                FROM USERS U
                JOIN ROLES R ON R.ID = U.ROLE_ID
                LEFT JOIN HR_EMPLOYEES E ON E.ID = U.EMPLOYEE_ID
                LEFT JOIN LOCATIONS L ON L.ID = U.WAREHOUSE_ID
                LEFT JOIN USER_GROUPS G ON G.ID = U.GROUP_ID
                WHERE U.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND U.GROUP_ID = @GroupId
                  AND R.CODE <> 'ADMIN'
                ORDER BY U.DISPLAY_NAME
                """,
                new { GroupId = groupId },
                cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task<SystemUser?> GetUserByIdAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<SystemUser?>(
            new CommandDefinition("""
                SELECT U.ID AS Id,
                       U.USERNAME AS Username,
                       U.DISPLAY_NAME AS DisplayName,
                       U.EMPLOYEE_ID AS EmployeeId,
                       E.FULL_NAME AS EmployeeName,
                       E.EMAIL AS Email,
                       U.WAREHOUSE_ID AS WarehouseId,
                       L.NAME AS WarehouseName,
                       U.GROUP_ID AS GroupId,
                       G.NAME AS GroupName,
                       U.IS_ACTIVE AS IsActive,
                       U.NOTES AS Notes
                FROM USERS U
                LEFT JOIN HR_EMPLOYEES E ON E.ID = U.EMPLOYEE_ID
                LEFT JOIN LOCATIONS L ON L.ID = U.WAREHOUSE_ID
                LEFT JOIN USER_GROUPS G ON G.ID = U.GROUP_ID
                WHERE U.ID = @UserId
                """,
                new { UserId = userId },
                cancellationToken: cancellationToken));
    }

    public async Task<UserGroup?> GetGroupByIdAsync(long groupId, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        return await connection.QuerySingleOrDefaultAsync<UserGroup?>(
            new CommandDefinition("""
                SELECT G.ID AS Id,
                       G.CODE AS Code,
                       G.NAME AS Name,
                       G.ICON AS Icon,
                       G.DESCRIPTION AS Description,
                       G.IS_ACTIVE AS IsActive,
                       G.DISPLAY_ORDER AS DisplayOrder,
                       (SELECT COUNT(*) FROM USERS U WHERE U.GROUP_ID = G.ID) AS UserCount
                FROM USER_GROUPS G
                WHERE G.ID = @GroupId
                """,
                new { GroupId = groupId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<GroupPermissionRow>> GetGroupPermissionsAsync(
        long groupId, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<GroupPermissionRow>(
            new CommandDefinition("""
                SELECT P.GROUP_ID AS GroupId,
                       P.FUNCTION_CODE AS FunctionCode,
                       P.CAN_VIEW AS CanView,
                       P.CAN_ADD AS CanAdd,
                       P.CAN_EDIT AS CanEdit,
                       P.CAN_DELETE AS CanDelete,
                       P.IS_LOCKED AS IsLocked
                FROM GROUP_PERMISSIONS P
                WHERE P.GROUP_ID = @GroupId
                """,
                new { GroupId = groupId },
                cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task SaveGroupPermissionsAsync(
        long groupId,
        IReadOnlyList<PermissionSaveRow> rows,
        long updatedBy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var previous = (await connection.QueryAsync<GroupPermissionRow>(new CommandDefinition("""
                SELECT GROUP_ID AS GroupId, FUNCTION_CODE AS FunctionCode, CAN_VIEW AS CanView,
                       CAN_ADD AS CanAdd, CAN_EDIT AS CanEdit, CAN_DELETE AS CanDelete, IS_LOCKED AS IsLocked
                FROM GROUP_PERMISSIONS WHERE GROUP_ID = @GroupId
                """, new { GroupId = groupId }, transaction, cancellationToken: cancellationToken))).ToArray();
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM GROUP_PERMISSIONS WHERE GROUP_ID = @GroupId",
                    new { GroupId = groupId },
                    transaction,
                    cancellationToken: cancellationToken));

            var isAdminGroup = await connection.QuerySingleAsync<bool>(new CommandDefinition(
                "SELECT CASE WHEN CODE = 'ADMIN' THEN TRUE ELSE FALSE END FROM USER_GROUPS WHERE ID = @Id",
                new { Id = groupId }, transaction, cancellationToken: cancellationToken));
            var effectiveRows = isAdminGroup
                ? SystemFunctionCatalog.All.Select(f => new PermissionSaveRow(f.Code, true, true, true, true, false)).ToArray()
                : rows;
            foreach (var row in effectiveRows)
            {
                if (SystemFunctionCatalog.Find(row.FunctionCode) is null)
                    throw new InvalidOperationException("Chức năng phân quyền không hợp lệ.");
                await connection.ExecuteAsync(
                    new CommandDefinition("""
                        INSERT INTO GROUP_PERMISSIONS
                            (COMPANY_ID, GROUP_ID, FUNCTION_CODE,
                             CAN_VIEW, CAN_ADD, CAN_EDIT, CAN_DELETE, IS_LOCKED,
                             UPDATED_BY, UPDATED_AT)
                        VALUES
                            ((SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'),
                             @GroupId, @FunctionCode,
                             @CanView, @CanAdd, @CanEdit, @CanDelete, @IsLocked,
                             @UpdatedBy, CURRENT_TIMESTAMP)
                        """,
                        new
                        {
                            GroupId = groupId,
                            FunctionCode = row.FunctionCode.Trim().ToUpperInvariant(),
                            CanView = isAdminGroup || row.CanView && !row.IsLocked,
                            CanAdd = isAdminGroup || row.CanView && row.CanAdd && !row.IsLocked,
                            CanEdit = isAdminGroup || row.CanView && row.CanEdit && !row.IsLocked,
                            CanDelete = isAdminGroup || row.CanView && row.CanDelete && !row.IsLocked,
                            IsLocked = !isAdminGroup && row.IsLocked,
                            UpdatedBy = updatedBy,
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await AuditLogWriter.WriteAsync(connection, (FirebirdSql.Data.FirebirdClient.FbTransaction)transaction,
                "GROUP_PERMISSIONS_UPDATED", updatedBy, entityType: "USER_GROUPS", entityId: groupId.ToString(),
                eventData: System.Text.Json.JsonSerializer.Serialize(new { Before = previous, Requested = rows, ProtectedAdminGroup = isAdminGroup }),
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<UserGroup> SaveGroupAsync(UserGroupSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var name = request.Name.Trim();
        if (name.Length == 0)
            throw new InvalidOperationException("Tên nhóm là bắt buộc.");

        var code = request.Code.Trim().ToUpperInvariant();
        if (code.Length == 0)
            code = $"GRP{name.GetHashCode():X8}";

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var duplicate = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition("""
                    SELECT FIRST 1 ID FROM USER_GROUPS
                    WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND UPPER(CODE) = @Code
                      AND (@Id IS NULL OR ID <> @Id)
                    """,
                    new { Code = code, Id = request.Id },
                    transaction,
                    cancellationToken: cancellationToken));
            if (duplicate is not null)
                throw new InvalidOperationException($"Mã nhóm '{code}' đã tồn tại.");

            long groupId;
            if (request.Id is null)
            {
                var nextOrder = await connection.QuerySingleAsync<int>(
                    new CommandDefinition("""
                        SELECT COALESCE(MAX(DISPLAY_ORDER), 0) + 1 FROM USER_GROUPS
                        WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        transaction: transaction,
                        cancellationToken: cancellationToken));

                groupId = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("""
                        INSERT INTO USER_GROUPS
                            (COMPANY_ID, CODE, NAME, ICON, DESCRIPTION, IS_ACTIVE, DISPLAY_ORDER, CREATED_BY)
                        VALUES
                            ((SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'),
                             @Code, @Name, @Icon, @Description, @IsActive, @Order, @CreatedBy)
                        RETURNING ID
                        """,
                        new
                        {
                            Code = code,
                            Name = name,
                            Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim(),
                            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                            IsActive = request.IsActive,
                            Order = nextOrder,
                            CreatedBy = request.CreatedBy,
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }
            else
            {
                groupId = request.Id.Value;
                var affected = await connection.ExecuteAsync(
                    new CommandDefinition("""
                        UPDATE USER_GROUPS SET
                            CODE = @Code,
                            NAME = @Name,
                            ICON = @Icon,
                            DESCRIPTION = @Description,
                            IS_ACTIVE = @IsActive,
                            UPDATED_BY = @UpdatedBy,
                            UPDATED_AT = CURRENT_TIMESTAMP
                        WHERE ID = @Id
                        """,
                        new
                        {
                            Id = groupId,
                            Code = code,
                            Name = name,
                            Icon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim(),
                            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                            IsActive = request.IsActive,
                            UpdatedBy = request.CreatedBy,
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                if (affected == 0)
                    throw new InvalidOperationException("Nhóm người dùng không tồn tại.");
            }

            await transaction.CommitAsync(cancellationToken);
            var saved = await GetGroupByIdAsync(groupId, cancellationToken);
            return saved ?? throw new InvalidOperationException("Không đọc lại được nhóm vừa lưu.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteGroupAsync(long groupId, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var group = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                "SELECT CODE, NAME FROM USER_GROUPS WHERE ID = @GroupId",
                new { GroupId = groupId },
                cancellationToken: cancellationToken));
        if (group is null)
            throw new InvalidOperationException("Nhóm người dùng không tồn tại.");

        var code = (string)group.CODE;
        if (string.Equals(code, "ADMIN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không được xóa nhóm Quản trị.");

        var userCount = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM USERS WHERE GROUP_ID = @GroupId",
                new { GroupId = groupId },
                cancellationToken: cancellationToken));
        if (userCount > 0)
            throw new InvalidOperationException(
                $"Nhóm '{(string)group.NAME}' còn {userCount} người dùng, không thể xóa.");

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM GROUP_PERMISSIONS WHERE GROUP_ID = @GroupId",
                    new { GroupId = groupId },
                    transaction,
                    cancellationToken: cancellationToken));
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "DELETE FROM USER_GROUPS WHERE ID = @GroupId",
                    new { GroupId = groupId },
                    transaction,
                    cancellationToken: cancellationToken));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SystemUser> SaveUserAsync(UserSaveRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var username = request.Username.Trim().ToLowerInvariant();
        if (username.Length == 0)
            throw new InvalidOperationException("Tài khoản là bắt buộc.");
        if (username.Length > 80)
            throw new InvalidOperationException("Tài khoản không được vượt quá 80 ký tự.");
        if (request.Id is null && string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("Mật khẩu là bắt buộc khi thêm mới.");

        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            // Tài khoản unique (không phân biệt hoa thường).
            var duplicateUser = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition("""
                    SELECT FIRST 1 ID FROM USERS
                    WHERE COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                      AND UPPER(USERNAME) = UPPER(@Username)
                      AND (@Id IS NULL OR ID <> @Id)
                    """,
                    new { Username = username, Id = request.Id },
                    transaction,
                    cancellationToken: cancellationToken));
            if (duplicateUser is not null)
                throw new InvalidOperationException($"Tài khoản '{username}' đã tồn tại.");

            // 1 nhân viên = 1 tài khoản.
            var duplicateEmployee = await connection.QuerySingleOrDefaultAsync<long?>(
                new CommandDefinition("""
                    SELECT FIRST 1 ID FROM USERS
                    WHERE EMPLOYEE_ID = @EmployeeId
                      AND (@Id IS NULL OR ID <> @Id)
                    """,
                    new { EmployeeId = request.EmployeeId, Id = request.Id },
                    transaction,
                    cancellationToken: cancellationToken));
            if (duplicateEmployee is not null)
                throw new InvalidOperationException("Nhân viên này đã có tài khoản.");

            // Nhân viên và nhóm phải tồn tại.
            var employee = await connection.QuerySingleOrDefaultAsync(
                new CommandDefinition(
                    "SELECT ID, FULL_NAME FROM HR_EMPLOYEES WHERE ID = @EmployeeId",
                    new { EmployeeId = request.EmployeeId },
                    transaction,
                    cancellationToken: cancellationToken));
            if (employee is null)
                throw new InvalidOperationException("Nhân viên không tồn tại.");

            var groupExists = await connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM USER_GROUPS WHERE ID = @GroupId",
                    new { GroupId = request.GroupId },
                    transaction,
                    cancellationToken: cancellationToken));
            if (groupExists == 0)
                throw new InvalidOperationException("Nhóm người dùng không tồn tại.");

            var displayName = (string)employee.FULL_NAME;
            var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();

            long userId;
            if (request.Id is null)
            {
                var (hash, salt) = HashPassword(request.Password!);
                var defaultBranchId = await ResolveDefaultBranchAsync(connection, transaction, request.CreatedBy, cancellationToken);
                var branch = await connection.QuerySingleOrDefaultAsync(
                    new CommandDefinition("""
                        SELECT ID, CODE, NAME FROM BRANCHES
                        WHERE ID = @BranchId AND IS_ACTIVE = TRUE
                          AND COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                        """,
                        new { BranchId = request.BranchId ?? defaultBranchId }, transaction,
                        cancellationToken: cancellationToken));
                if (branch is null)
                    throw new InvalidOperationException("Không tìm thấy chi nhánh hoạt động cho tài khoản.");

                // Nhóm CASHIER và nhóm tự tạo dùng role bán hàng cơ bản; không kế thừa Admin của người tạo.
                var role = await connection.QuerySingleOrDefaultAsync(
                    new CommandDefinition("""
                        SELECT R.ID, R.CODE FROM ROLES R
                        WHERE R.CODE = COALESCE(
                            (SELECT R2.CODE FROM ROLES R2 JOIN USER_GROUPS G ON G.CODE = R2.CODE WHERE G.ID = @GroupId),
                            'SALE')
                        """,
                        new { GroupId = request.GroupId }, transaction,
                        cancellationToken: cancellationToken));
                if (role is null)
                    throw new InvalidOperationException("Chưa cấu hình vai trò đăng nhập cho tài khoản.");

                userId = await connection.ExecuteScalarAsync<long>(
                    new CommandDefinition("""
                        INSERT INTO USERS
                            (COMPANY_ID, USERNAME, DISPLAY_NAME,
                             PASSWORD_HASH, PASSWORD_SALT, PASSWORD_ITERATIONS,
                             ROLE_ID, ROLE_CODE, BRANCH_CODE, BRANCH_NAME,
                             EMPLOYEE_ID, GROUP_ID, WAREHOUSE_ID,
                             DEFAULT_BRANCH_ID, IS_ACTIVE, NOTES)
                        VALUES
                            ((SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'),
                             @Username, @DisplayName,
                             @Hash, @Salt, @Iterations,
                             @RoleId, @RoleCode, @BranchCode, @BranchName,
                             @EmployeeId, @GroupId, @WarehouseId,
                             @BranchId, @IsActive, @Notes)
                        RETURNING ID
                        """,
                        new
                        {
                            Username = username,
                            DisplayName = displayName,
                            Hash = hash,
                            Salt = salt,
                            Iterations = PasswordIterations,
                            RoleId = (long)role.ID,
                            RoleCode = (string)role.CODE,
                            BranchCode = (string)branch.CODE,
                            BranchName = (string)branch.NAME,
                            EmployeeId = request.EmployeeId,
                            GroupId = request.GroupId,
                            WarehouseId = request.WarehouseId,
                            BranchId = (long)branch.ID,
                            IsActive = request.IsActive,
                            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                        },
                        transaction,
                        cancellationToken: cancellationToken));
            }
            else
            {
                userId = request.Id.Value;

                // Không cho sửa tài khoản Admin hệ thống qua chức năng này.
                var roleCode = await connection.QuerySingleOrDefaultAsync<string?>(
                    new CommandDefinition("""
                        SELECT R.CODE FROM USERS U
                        JOIN ROLES R ON R.ID = U.ROLE_ID
                        WHERE U.ID = @UserId
                        """,
                        new { UserId = userId },
                        transaction,
                        cancellationToken: cancellationToken));
                if (roleCode is null)
                    throw new InvalidOperationException("Người dùng không tồn tại.");
                if (string.Equals(roleCode, "ADMIN", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Không thể sửa tài khoản Admin hệ thống tại đây.");

                if (string.IsNullOrWhiteSpace(request.Password))
                {
                    await connection.ExecuteAsync(
                        new CommandDefinition("""
                            UPDATE USERS SET
                                USERNAME = @Username,
                                DISPLAY_NAME = @DisplayName,
                                EMPLOYEE_ID = @EmployeeId,
                                GROUP_ID = @GroupId,
                                WAREHOUSE_ID = @WarehouseId,
                                IS_ACTIVE = @IsActive,
                                NOTES = @Notes
                            WHERE ID = @UserId
                            """,
                            new
                            {
                                UserId = userId,
                                Username = username,
                                DisplayName = displayName,
                                EmployeeId = request.EmployeeId,
                                GroupId = request.GroupId,
                                WarehouseId = request.WarehouseId,
                                IsActive = request.IsActive,
                                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                            },
                            transaction,
                            cancellationToken: cancellationToken));
                }
                else
                {
                    var (hash, salt) = HashPassword(request.Password);
                    await connection.ExecuteAsync(
                        new CommandDefinition("""
                            UPDATE USERS SET
                                USERNAME = @Username,
                                DISPLAY_NAME = @DisplayName,
                                PASSWORD_HASH = @Hash,
                                PASSWORD_SALT = @Salt,
                                PASSWORD_ITERATIONS = @Iterations,
                                EMPLOYEE_ID = @EmployeeId,
                                GROUP_ID = @GroupId,
                                WAREHOUSE_ID = @WarehouseId,
                                IS_ACTIVE = @IsActive,
                                NOTES = @Notes
                            WHERE ID = @UserId
                            """,
                            new
                            {
                                UserId = userId,
                                Username = username,
                                DisplayName = displayName,
                                Hash = hash,
                                Salt = salt,
                                Iterations = PasswordIterations,
                                EmployeeId = request.EmployeeId,
                                GroupId = request.GroupId,
                                WarehouseId = request.WarehouseId,
                                IsActive = request.IsActive,
                                Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
                            },
                            transaction,
                            cancellationToken: cancellationToken));
                }
            }

            // Email lưu trên hồ sơ nhân viên (USERS không có cột email riêng).
            if (email is not null)
            {
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        "UPDATE HR_EMPLOYEES SET EMAIL = @Email WHERE ID = @EmployeeId",
                        new { Email = email, EmployeeId = request.EmployeeId },
                        transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
            var saved = await GetUserByIdAsync(userId, cancellationToken);
            return saved ?? throw new InvalidOperationException("Không đọc lại được người dùng vừa lưu.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteUserAsync(long userId, CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var roleCode = await connection.QuerySingleOrDefaultAsync<string?>(
            new CommandDefinition("""
                SELECT R.CODE FROM USERS U
                JOIN ROLES R ON R.ID = U.ROLE_ID
                WHERE U.ID = @UserId
                """,
                new { UserId = userId },
                cancellationToken: cancellationToken));
        if (roleCode is null)
            throw new InvalidOperationException("Người dùng không tồn tại.");
        if (string.Equals(roleCode, "ADMIN", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không được xóa tài khoản Admin hệ thống.");

        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM USERS WHERE ID = @UserId",
                new { UserId = userId },
                cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<EmployeeOption>> GetEmployeesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<EmployeeOption>(
            new CommandDefinition("""
                SELECT E.ID AS Id, E.CODE AS Code, E.FULL_NAME AS FullName, E.EMAIL AS Email
                FROM HR_EMPLOYEES E
                WHERE E.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND E.IS_ACTIVE = TRUE
                ORDER BY E.FULL_NAME, E.CODE
                """,
                cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task<IReadOnlyList<WarehouseOption>> GetWarehousesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = FirebirdDatabase.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<WarehouseOption>(
            new CommandDefinition("""
                SELECT L.ID AS Id, L.CODE AS Code, L.NAME AS Name
                FROM LOCATIONS L
                WHERE L.IS_ACTIVE = TRUE
                ORDER BY L.NAME, L.CODE
                """,
                cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    private static (string Hash, string Salt) HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(PasswordSaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            PasswordIterations,
            HashAlgorithmName.SHA256,
            PasswordHashBytes);
        return (Convert.ToHexString(hash), Convert.ToHexString(salt));
    }

    private static async Task<long?> ResolveDefaultBranchAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long createdBy,
        CancellationToken cancellationToken)
    {
        var creatorBranch = await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition(
                "SELECT DEFAULT_BRANCH_ID FROM USERS WHERE ID = @CreatedBy",
                new { CreatedBy = createdBy },
                transaction,
                cancellationToken: cancellationToken));
        if (creatorBranch is not null)
            return creatorBranch;

        return await connection.QuerySingleOrDefaultAsync<long?>(
            new CommandDefinition("""
                SELECT FIRST 1 B.ID FROM BRANCHES B
                WHERE B.COMPANY_ID = (SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO')
                  AND B.IS_ACTIVE = TRUE
                ORDER BY B.ID
                """,
                transaction: transaction,
                cancellationToken: cancellationToken));
    }
}
