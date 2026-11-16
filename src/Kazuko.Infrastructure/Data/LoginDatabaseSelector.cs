using System.Security.Cryptography;
using Dapper;
using FirebirdSql.Data.FirebirdClient;

namespace Kazuko.Infrastructure.Data;

/// <summary>Validates an existing local DB before publishing a login connection choice.</summary>
public sealed class LoginDatabaseSelector
{
    public async Task<ConnectionProfile> ValidateAsync(string path, ConnectionProfile current,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath) || !Path.GetExtension(fullPath).Equals(".fdb", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Vui lòng chọn file cơ sở dữ liệu .fdb hiện có.");

        // Embedded opens local files; never inherit a remote host or server account.
        var candidate = new ConnectionProfile("localhost", 3050, fullPath, "SYSDBA",
            string.IsNullOrWhiteSpace(current.Password) ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : current.Password, true);
        var builder = new FbConnectionStringBuilder(FirebirdDatabase.BuildConnectionString(candidate)) { Pooling = false };
        try
        {
            await using var connection = new FbConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            var version = await connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COALESCE(MAX(VERSION_NO), 0) FROM SCHEMA_MIGRATIONS", cancellationToken: cancellationToken));
            if (version != LocalDatabaseBootstrapper.SchemaVersion)
                throw new InvalidOperationException($"DB đang ở phiên bản v{version}; ứng dụng này cần v{LocalDatabaseBootstrapper.SchemaVersion}. Chọn DB đã được nâng cấp đúng phiên bản.");

            // Resolve the columns used at sign-in without reading credentials or changing data.
            await connection.QueryAsync(new CommandDefinition("""
                SELECT U.ID, U.USERNAME, U.PASSWORD_HASH, U.PASSWORD_SALT, U.PASSWORD_ITERATIONS,
                       U.DISPLAY_NAME, U.IS_ACTIVE, R.CODE, B.ID, B.CODE, B.NAME
                FROM USERS U JOIN ROLES R ON R.ID = U.ROLE_ID
                CROSS JOIN BRANCHES B CROSS JOIN COMPANIES C
                WHERE 1 = 0
                """, cancellationToken: cancellationToken));
            var branches = await connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT COUNT(*) FROM BRANCHES WHERE IS_ACTIVE = TRUE", cancellationToken: cancellationToken));
            if (branches == 0)
                throw new InvalidOperationException("DB chưa có chi nhánh hoạt động để đăng nhập.");
        }
        catch (OperationCanceledException) { throw; }
        catch (FbException)
        {
            throw new InvalidOperationException("Không mở được DB KAZUKO hợp lệ. Kiểm tra file được chọn và quyền truy cập file.");
        }
        return candidate;
    }
}
