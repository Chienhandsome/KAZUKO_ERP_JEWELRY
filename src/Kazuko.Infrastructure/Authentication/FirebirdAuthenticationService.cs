using System.Security.Cryptography;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Authentication;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Authentication;

public sealed class FirebirdAuthenticationService : IAuthenticationService
{
    public async Task<IReadOnlyList<BranchOption>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var branches = new List<BranchOption>();
            await using var connection = FirebirdDatabase.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new FbCommand(
                "SELECT ID, CODE, NAME FROM BRANCHES WHERE IS_ACTIVE = TRUE ORDER BY NAME",
                connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                branches.Add(new BranchOption(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            return branches;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Không thể tải danh sách chi nhánh từ database.");
        }
    }

    public async Task<AuthenticationResult> SignInAsync(
        string username,
        string password,
        string branchCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
            return new AuthenticationResult(false, ErrorMessage: "Vui lòng nhập tên đăng nhập.");

        if (string.IsNullOrWhiteSpace(branchCode))
            return new AuthenticationResult(false, ErrorMessage: "Vui lòng chọn chi nhánh.");

        try
        {
            await using var connection = FirebirdDatabase.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new FbCommand("""
                SELECT U.ID, U.USERNAME, U.PASSWORD_HASH, U.PASSWORD_SALT, U.PASSWORD_ITERATIONS,
                       U.DISPLAY_NAME, R.CODE, U.IS_ACTIVE,
                       B.ID, B.CODE, B.NAME, U.DEFAULT_BRANCH_ID
                FROM USERS U
                JOIN ROLES R ON R.ID = U.ROLE_ID
                JOIN BRANCHES B ON B.CODE = @BRANCH_CODE AND B.IS_ACTIVE = TRUE
                WHERE U.USERNAME = @USERNAME
                """, connection);
            command.Parameters.AddWithValue("@USERNAME", username.Trim().ToLowerInvariant());
            command.Parameters.AddWithValue("@BRANCH_CODE", branchCode);

            long userId;
            string storedUserName;
            string passwordHash;
            string passwordSalt;
            int passwordIterations;
            string displayName;
            string roleCode;
            bool isActive;
            long branchId;
            string selectedBranchCode;
            long defaultBranchId;

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                    return new AuthenticationResult(false, ErrorMessage: "Tên đăng nhập, mật khẩu hoặc chi nhánh không đúng.");

                userId = reader.GetInt64(0);
                storedUserName = reader.GetString(1);
                passwordHash = reader.GetString(2);
                passwordSalt = reader.GetString(3);
                passwordIterations = reader.GetInt32(4);
                displayName = reader.GetString(5);
                roleCode = reader.GetString(6);
                isActive = reader.GetBoolean(7);
                branchId = reader.GetInt64(8);
                selectedBranchCode = reader.GetString(9);
                defaultBranchId = reader.IsDBNull(11) ? 0 : reader.GetInt64(11);
            }

            if (!isActive)
                return new AuthenticationResult(false, ErrorMessage: "Tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.");

            var expectedHash = Convert.FromHexString(passwordHash);
            var suppliedHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                Convert.FromHexString(passwordSalt),
                passwordIterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            if (!CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash))
            {
                AuditLogWriter.Write(
                    "LOGIN_FAILED",
                    eventData: $"Username={username}; Branch={branchCode}");
                return new AuthenticationResult(false, ErrorMessage: "Tên đăng nhập hoặc mật khẩu không đúng.");
            }

            if (!string.Equals(roleCode, "ADMIN", StringComparison.OrdinalIgnoreCase) &&
                defaultBranchId != branchId &&
                !await HasBranchScopeAsync(connection, userId, branchId, cancellationToken))
            {
                return new AuthenticationResult(false, ErrorMessage: "Tài khoản không có quyền truy cập chi nhánh đã chọn.");
            }

            await using var updateCommand = new FbCommand(
                "UPDATE USERS SET LAST_LOGIN_AT = CURRENT_TIMESTAMP WHERE ID = @ID",
                connection);
            updateCommand.Parameters.AddWithValue("@ID", userId);
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);

            AuditLogWriter.Write(
                "LOGIN_SUCCESS",
                userId,
                storedUserName,
                branchId,
                "USERS",
                userId.ToString(),
                $"Branch={selectedBranchCode}");

            return new AuthenticationResult(true, displayName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Không thể", StringComparison.Ordinal))
        {
            return new AuthenticationResult(false, ErrorMessage: ex.Message);
        }
        catch (Exception)
        {
            return new AuthenticationResult(false, ErrorMessage: "Không thể kết nối dịch vụ đăng nhập. Vui lòng thử lại.");
        }
    }

    private static async Task<bool> HasBranchScopeAsync(
        FbConnection connection,
        long userId,
        long branchId,
        CancellationToken cancellationToken)
    {
        await using var command = new FbCommand("""
            SELECT COUNT(*) FROM USER_SCOPES
            WHERE USER_ID = @USER_ID AND SCOPE_TYPE = 'BRANCH' AND SCOPE_ID = @BRANCH_ID AND CAN_ACCESS = TRUE
            """, connection);
        command.Parameters.AddWithValue("@USER_ID", userId);
        command.Parameters.AddWithValue("@BRANCH_ID", branchId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }
}
