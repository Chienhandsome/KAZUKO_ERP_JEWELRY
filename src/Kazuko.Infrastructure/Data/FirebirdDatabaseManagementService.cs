using System.Diagnostics;
using Dapper;
using FirebirdSql.Data.FirebirdClient;
using Kazuko.Core.Appearance;
using Kazuko.Core.Authentication;
using Kazuko.Core.Settings;
using Kazuko.Infrastructure.Data;

namespace Kazuko.Infrastructure.Data;

/// <summary>
/// Quản lý file cơ sở dữ liệu Firebird Embedded: xem thông tin, kiểm tra,
/// backup, tạo DB trắng mới và chuyển đổi DB đang dùng.
/// </summary>
public sealed class FirebirdDatabaseManagementService : IDatabaseManagementService
{
    private readonly FirebirdBackupManager _backups = new();

    private readonly IPreferencesService _preferences;

    /// <summary>
    /// Các bảng giao dịch sẽ bị xóa dữ liệu khi tạo DB trắng.
    /// Thứ tự đã tính đến ràng buộc khóa ngoại (bảng con trước, bảng cha sau).
    /// </summary>
    private static readonly string[] TransactionalTables =
    [
        // Bán hàng
        "ORDER_PAYMENTS",
        "SALES_ORDER_LINES",
        "SALES_ORDERS",
        // Sản xuất
        "PRODUCTION_MATERIAL_BALANCES",
        "PRODUCTION_ORDER_WORKERS",
        "PRODUCTION_ORDER_LINES",
        "PRODUCTION_QC_UNITS",
        "PRODUCTION_ORDERS",
        // Kho
        "STOCK_MOVEMENTS",
        "STOCK_DOCUMENT_LINES",
        "STOCK_DOCUMENTS",
        "FINISHED_PRODUCT_MOVEMENTS",
        "FINISHED_PRODUCTS",
        // Tài chính
        "SETTLEMENT_APPLICATIONS",
        "FINANCIAL_SETTLEMENTS",
        "CASH_VOUCHER_ALLOCATIONS",
        "CASH_VOUCHERS",
        // Công nợ
        "WORKER_GOLD_DEBTS",
        "RECEIVABLE_DEBTS",
        "PAYABLE_DEBTS",
        // Khách hàng / NCC phát sinh
        "CUSTOMER_TRANSACTIONS",
    ];

    public FirebirdDatabaseManagementService(IPreferencesService preferences)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
    }

    public async Task<DatabaseInfo> GetInfoAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        var profile = FirebirdDatabase.Profile;
        var path = profile.UseEmbedded ? FirebirdDatabase.DatabasePath : $"{profile.Host}/{profile.Port}:{profile.DatabasePath}";
        var fileInfo = profile.UseEmbedded ? new FileInfo(profile.DatabasePath) : null;

        int schemaVersion = 0;
        int branchCount = 0;
        int userCount = 0;

        if (!profile.UseEmbedded || fileInfo?.Exists == true)
        {
            await using var connection = FirebirdDatabase.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            schemaVersion = await connection.QuerySingleOrDefaultAsync<int>(
                new CommandDefinition(
                    "SELECT COALESCE(MAX(VERSION_NO), 0) FROM SCHEMA_MIGRATIONS",
                    cancellationToken: cancellationToken));
            branchCount = await connection.QuerySingleOrDefaultAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM BRANCHES",
                    cancellationToken: cancellationToken));
            userCount = await connection.QuerySingleOrDefaultAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM USERS",
                    cancellationToken: cancellationToken));
        }

        return new DatabaseInfo(
            path,
            fileInfo?.Exists == true ? fileInfo.Length : 0,
            profile.UseEmbedded ? FormatFileSize(fileInfo?.Exists == true ? fileInfo.Length : 0) : "Trên máy chủ",
            schemaVersion,
            schemaVersion > 0 ? $"v{schemaVersion}" : "—",
            branchCount,
            userCount);
    }

    public async Task<DatabaseValidationResult> ValidateDatabaseFileAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DatabaseValidationResult(false, "Chưa chọn file cơ sở dữ liệu.");

        if (!File.Exists(path))
            return new DatabaseValidationResult(false, "File không tồn tại.");

        if (!string.Equals(Path.GetExtension(path), ".fdb", StringComparison.OrdinalIgnoreCase))
            return new DatabaseValidationResult(false, "File phải có đuôi .fdb.");

        try
        {
            var profile = FirebirdDatabase.Profile with { DatabasePath = Path.GetFullPath(path) };
            var connectionString = FirebirdDatabase.BuildConnectionString(profile);
            await using var connection = new FbConnection(connectionString);
            await connection.OpenAsync(cancellationToken);

            var hasCompanies = await connection.QuerySingleOrDefaultAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'COMPANIES'",
                    cancellationToken: cancellationToken));
            if (hasCompanies == 0)
                return new DatabaseValidationResult(false, "File này không phải cơ sở dữ liệu KAZUKO (thiếu bảng COMPANIES).");

            int schemaVersion = 0;
            try
            {
                schemaVersion = await connection.QuerySingleOrDefaultAsync<int>(
                    new CommandDefinition(
                        "SELECT COALESCE(MAX(VERSION_NO), 0) FROM SCHEMA_MIGRATIONS",
                        cancellationToken: cancellationToken));
            }
            catch
            {
                // SCHEMA_MIGRATIONS chưa có — DB rất cũ hoặc không chuẩn.
            }

            return new DatabaseValidationResult(true, null, schemaVersion);
        }
        catch (Exception ex)
        {
            return new DatabaseValidationResult(false, $"Không mở được file: {Sanitize(ex.Message)}");
        }
    }

    public Task<string> BackupAsync(
        string targetFolder,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _backups.BackupAsync(targetFolder, false, cancellationToken, progress);
    }

    public DatabaseBackupStatus GetBackupStatus() => _backups.GetStatus();

    public Task<BackupVerificationResult> VerifyBackupAsync(
        string backupPath,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        return _backups.VerifyAsync(backupPath, cancellationToken, progress);
    }

    public async Task CreateBlankDatabaseAsync(
        string targetPath,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Chưa chọn nơi lưu file cơ sở dữ liệu mới.", nameof(targetPath));

        targetPath = Path.GetFullPath(targetPath);
        var sourcePath = FirebirdDatabase.DatabasePath;
        if (string.Equals(targetPath, Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Không thể ghi đè lên file cơ sở dữ liệu đang dùng.");

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Không tìm thấy file cơ sở dữ liệu hiện tại.", sourcePath);

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        // Copy schema đầy đủ từ DB hiện tại (giữ nguyên cấu trúc + SCHEMA_MIGRATIONS).
        FbConnection.ClearAllPools();
        await Task.Delay(200, cancellationToken);
        await CopyFileWithRetryAsync(sourcePath, targetPath, cancellationToken);

        // Xóa dữ liệu giao dịch trên bản copy, giữ lại danh mục + tài khoản.
        var profile = FirebirdDatabase.Profile with { DatabasePath = targetPath };
        var connectionString = FirebirdDatabase.BuildConnectionString(profile);
        await using var connection = new FbConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var table in TransactionalTables)
            {
                var exists = await connection.QuerySingleAsync<int>(
                    new CommandDefinition(
                        "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = @T",
                        new { T = table },
                        transaction,
                        cancellationToken: cancellationToken));
                if (exists == 0)
                    continue;
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        $"DELETE FROM {table}",
                        transaction: transaction,
                        cancellationToken: cancellationToken));
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Reset các sequence về 0 để mã chứng từ bắt đầu lại từ đầu.
        await ResetGeneratorsAsync(connection, cancellationToken);

        await EnsureSeedDataAsync(connection, cancellationToken);
    }

    public void SwitchDatabase(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
            throw new ArgumentException("Chưa chọn file cơ sở dữ liệu.", nameof(databasePath));

        var profile = FirebirdDatabase.Profile with { DatabasePath = Path.GetFullPath(databasePath) };
        FirebirdDatabase.SaveAndApplyProfile(profile);
    }

    public async Task RunAutoBackupIfEnabledAsync(CancellationToken cancellationToken = default)
    {
        if (!_preferences.Load().AutoBackupEnabled) return;
        await _backups.BackupAsync(null, true, cancellationToken);
    }

    private static async Task CopyFileWithRetryAsync(
        string source, string target, CancellationToken cancellationToken)
    {
        const int maxAttempts = 5;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await using var sourceStream = new FileStream(
                    source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                    bufferSize: 81920, useAsync: true);
                await using var targetStream = new FileStream(
                    target, FileMode.Create, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
                await sourceStream.CopyToAsync(targetStream, cancellationToken);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                lastError = ex;
                await Task.Delay(300 * attempt, cancellationToken);
            }
        }

        throw new IOException(
            $"Không copy được file cơ sở dữ liệu sau {maxAttempts} lần thử: {Sanitize(lastError?.Message)}",
            lastError);
    }

    private static async Task ResetGeneratorsAsync(
        FbConnection connection, CancellationToken cancellationToken)
    {
        var generators = await connection.QueryAsync<string>(
            new CommandDefinition(
                """
                SELECT TRIM(RDB$GENERATOR_NAME)
                FROM RDB$GENERATORS
                WHERE RDB$SYSTEM_FLAG = 0 OR RDB$SYSTEM_FLAG IS NULL
                """,
                cancellationToken: cancellationToken));

        foreach (var generator in generators)
        {
            if (string.IsNullOrWhiteSpace(generator))
                continue;
            try
            {
                // Firebird 3+: ALTER SEQUENCE ... RESTART WITH 0
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        $"ALTER SEQUENCE {generator} RESTART WITH 0",
                        cancellationToken: cancellationToken));
            }
            catch
            {
                // Generator hệ thống hoặc không reset được — bỏ qua.
            }
        }
    }

    /// <summary>
    /// Đảm bảo dữ liệu mẫu tối thiểu: công ty KAZUKO, chi nhánh mặc định,
    /// tài khoản admin, loại vàng cơ bản, quỹ tiền mặt mặc định.
    /// Vì DB mới copy từ DB hiện tại nên dữ liệu này thường đã có — chỉ bổ sung khi thiếu.
    /// </summary>
    private static async Task EnsureSeedDataAsync(
        FbConnection connection, CancellationToken cancellationToken)
    {
        // Công ty KAZUKO
        var companyCount = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM COMPANIES WHERE CODE = 'KAZUKO'",
                cancellationToken: cancellationToken));
        if (companyCount == 0)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    "INSERT INTO COMPANIES (CODE, NAME, IS_ACTIVE) VALUES ('KAZUKO', 'KAZUKO Jewelry', TRUE)",
                    cancellationToken: cancellationToken));
        }

        var companyId = await connection.QuerySingleAsync<long>(
            new CommandDefinition(
                "SELECT ID FROM COMPANIES WHERE CODE = 'KAZUKO'",
                cancellationToken: cancellationToken));

        // Chi nhánh mặc định
        var branchCount = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM BRANCHES WHERE COMPANY_ID = @C",
                new { C = companyId },
                cancellationToken: cancellationToken));
        if (branchCount == 0)
        {
            await connection.ExecuteAsync(
                new CommandDefinition(
                    """
                    INSERT INTO BRANCHES (COMPANY_ID, CODE, NAME, IS_ACTIVE)
                    VALUES (@C, 'CN01', 'Chi nhánh chính', TRUE)
                    """,
                    new { C = companyId },
                    cancellationToken: cancellationToken));
        }

        // Quỹ tiền mặt mặc định (nếu bảng tồn tại và chưa có)
        var hasCashFunds = await connection.QuerySingleAsync<int>(
            new CommandDefinition(
                "SELECT COUNT(*) FROM RDB$RELATIONS WHERE RDB$RELATION_NAME = 'CASH_FUNDS'",
                cancellationToken: cancellationToken));
        if (hasCashFunds > 0)
        {
            var fundCount = await connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM CASH_FUNDS WHERE COMPANY_ID = @C",
                    new { C = companyId },
                    cancellationToken: cancellationToken));
            if (fundCount == 0)
            {
                var branchId = await connection.QuerySingleAsync<long>(
                    new CommandDefinition(
                        "SELECT ID FROM BRANCHES WHERE COMPANY_ID = @C ORDER BY ID ROWS 1",
                        new { C = companyId },
                        cancellationToken: cancellationToken));
                await connection.ExecuteAsync(
                    new CommandDefinition(
                        """
                        INSERT INTO CASH_FUNDS (COMPANY_ID, BRANCH_ID, CODE, NAME, IS_ACTIVE)
                        VALUES (@C, @B, 'QTM', 'Quỹ tiền mặt chính', TRUE)
                        """,
                        new { C = companyId, B = branchId },
                        cancellationToken: cancellationToken));
            }
        }

        // Ghi chú: tài khoản admin và loại vàng được giữ lại từ DB gốc khi copy,
        // nên không cần tạo mới ở đây.
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:N1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):N1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):N2} GB";
    }

    private static string Sanitize(string? message) =>
        string.IsNullOrWhiteSpace(message) ? "Đã xảy ra lỗi." : message.Trim();
}
