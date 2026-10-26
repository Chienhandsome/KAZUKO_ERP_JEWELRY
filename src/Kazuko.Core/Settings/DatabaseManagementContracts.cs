using Kazuko.Core.Authentication;

namespace Kazuko.Core.Settings;

/// <summary>Thông tin tổng quan về file cơ sở dữ liệu hiện tại.</summary>
public sealed record DatabaseInfo(
    string FilePath,
    long FileSizeBytes,
    string FileSizeDisplay,
    int SchemaVersion,
    string SchemaVersionDisplay,
    int BranchCount,
    int UserCount);

/// <summary>Kết quả kiểm tra một file .fdb có phải DB KAZUKO hợp lệ không.</summary>
public sealed record DatabaseValidationResult(
    bool IsValid,
    string? ErrorMessage = null,
    int SchemaVersion = 0);

public sealed record DatabaseBackupStatus(
    DateTimeOffset? LastSuccessAt = null,
    string? LastSuccessPath = null,
    DateTimeOffset? LastAttemptAt = null,
    string? LastError = null,
    DateTimeOffset? LastVerificationAt = null,
    string? LastVerifiedPath = null);

public sealed record BackupVerificationResult(int SchemaVersion, int CompanyCount);

/// <summary>Quản lý file cơ sở dữ liệu: xem thông tin, chuyển đổi, backup, tạo mới.</summary>
public interface IDatabaseManagementService
{
    /// <summary>Lấy thông tin DB hiện tại (đường dẫn, dung lượng, schema, số chi nhánh/người dùng).</summary>
    Task<DatabaseInfo> GetInfoAsync(
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Kiểm tra file .fdb có phải DB KAZUKO hợp lệ không.</summary>
    Task<DatabaseValidationResult> ValidateDatabaseFileAsync(
        string path,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Backup DB hiện tại vào thư mục chỉ định.
    /// Trả về đường dẫn đầy đủ của file backup vừa tạo.
    /// </summary>
    Task<string> BackupAsync(
        string targetFolder,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);

    DatabaseBackupStatus GetBackupStatus();

    Task<BackupVerificationResult> VerifyBackupAsync(
        string backupPath,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default,
        IProgress<string>? progress = null);

    /// <summary>
    /// Tạo DB trắng mới tại đường dẫn chỉ định (copy schema từ DB hiện tại,
    /// xóa dữ liệu giao dịch, giữ lại danh mục + seed dữ liệu mặc định).
    /// </summary>
    Task CreateBlankDatabaseAsync(
        string targetPath,
        AuthenticatedSession session,
        CancellationToken cancellationToken = default);

    /// <summary>Lưu profile để app dùng DB khác ở lần khởi động tiếp theo.</summary>
    void SwitchDatabase(string databasePath);

    /// <summary>Backup tự động khi khởi động app (nếu được bật trong preferences).</summary>
    Task RunAutoBackupIfEnabledAsync(CancellationToken cancellationToken = default);
}
